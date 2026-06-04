using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RTPCCurveEditor.Commands;
using RTPCCurveEditor.Models;
using RTPCCurveEditor.Presets;
using RTPCCurveEditor.Services;
using System.IO;

namespace RTPCCurveEditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ── State ─────────────────────────────────────────────────────────────

    [ObservableProperty] private CurveDocument _document = CurveDocument.CreateDefault();
    [ObservableProperty] private BezierCurve _activeCurve;
    [ObservableProperty] private CurvePoint? _selectedPoint;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _windowTitle = "RTPC Curve Editor";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _snapToGrid = true;
    [ObservableProperty] private string _presetFilter = "";
    [ObservableProperty] private CurvePreset? _selectedPreset;

    public UndoRedoStack UndoRedo { get; } = new();
    public ObservableCollection<CurvePreset> FilteredPresets { get; } = new();

    // ── Fix: AllCurves is now a proper backing field, not a new collection
    //         on every read. RefreshAllCurves() syncs it when the curve list changes.
    public ObservableCollection<BezierCurve> AllCurves { get; } = new();

    private string? _currentFilePath;

    public MainViewModel()
    {
        _activeCurve = Document.PrimaryCurve;
        RefreshAllCurves();

        UndoRedo.StackChanged += () =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            IsDirty = true;
        };
        RefreshPresets();
    }

    private void RefreshAllCurves()
    {
        AllCurves.Clear();
        foreach (var c in Document.Curves)
            AllCurves.Add(c);
    }

    // ── Undo/Redo ─────────────────────────────────────────────────────────

    public bool CanUndo => UndoRedo.CanUndo;
    public bool CanRedo => UndoRedo.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() { UndoRedo.Undo(); RaiseCurveChanged(); }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() { UndoRedo.Redo(); RaiseCurveChanged(); }

    // ── Clear curve ───────────────────────────────────────────────────────

    [RelayCommand]
    private void ClearCurve()
    {
        UndoRedo.Execute(new ApplyPresetCommand(ActiveCurve,
            new List<CurvePoint> { new(0, 0), new(1, 1) }, "Clear"));
        SelectedPoint = null;
        ClearPointSelection();
        RaiseCurveChanged();
        Status("Curve cleared.");
    }

    // ── Point manipulation ────────────────────────────────────────────────

    public void AddPoint(double x, double y)
    {
        if (SnapToGrid) { x = Snap(x); y = Snap(y); }
        var pt = new CurvePoint(x, y);
        UndoRedo.Execute(new AddPointCommand(ActiveCurve, pt));
        ClearPointSelection();
        pt.IsSelected = true;
        SelectedPoint = pt;
        RaiseCurveChanged();
        Status($"Added point ({x:F2}, {y:F2})");
    }

    public void DeleteSelectedPoint()
    {
        if (SelectedPoint == null) return;
        if (ActiveCurve.Points.Count <= 2) { Status("Minimum 2 points required."); return; }
        UndoRedo.Execute(new DeletePointCommand(ActiveCurve, SelectedPoint));
        SelectedPoint = null;
        RaiseCurveChanged();
        Status("Point deleted.");
    }

    public void MovePoint(CurvePoint pt, double newX, double newY)
    {
        if (SnapToGrid) { newX = Snap(newX); newY = Snap(newY); }
        newX = Math.Clamp(newX, 0, 1);
        newY = Math.Clamp(newY, 0, 1);
        UndoRedo.Execute(new MovePointCommand(pt, pt.X, pt.Y, newX, newY));
        RaiseCurveChanged();
    }

    // ── Selection helpers ─────────────────────────────────────────────────

    /// Deselects all points on the active curve.
    public void ClearPointSelection()
    {
        foreach (var p in ActiveCurve.Points)
            p.IsSelected = false;
        SelectedPoint = null;
    }

    /// Returns all currently selected points on the active curve, sorted by X.
    public List<CurvePoint> GetSelectedPoints() =>
        ActiveCurve.Points
            .Where(p => p.IsSelected)
            .OrderBy(p => p.X)
            .ToList();

    public int SelectedPointCount => ActiveCurve.Points.Count(p => p.IsSelected);

    // ── Presets ───────────────────────────────────────────────────────────

    partial void OnPresetFilterChanged(string value) => RefreshPresets();

    private void RefreshPresets()
    {
        FilteredPresets.Clear();
        var filter = PresetFilter.Trim().ToLowerInvariant();
        foreach (var p in PresetLibrary.All)
        {
            if (filter.Length == 0
                || p.Name.ToLowerInvariant().Contains(filter)
                || p.Category.ToLowerInvariant().Contains(filter))
                FilteredPresets.Add(p);
        }
    }

    [RelayCommand]
    private void ApplyPreset()
    {
        if (SelectedPreset == null) return;

        var selected = GetSelectedPoints();

        if (selected.Count < 2)
        {
            Status("Select at least 2 points (or a segment) to apply a preset.");
            return;
        }

        var leftBound = selected.First();
        var rightBound = selected.Last();
        var sorted = ActiveCurve.Points.OrderBy(p => p.X).ToList();

        bool isFullCurve = leftBound == sorted.First()
                        && rightBound == sorted.Last()
                        && selected.Count == sorted.Count;

        // Sample the preset at t=0.33 and t=0.67 to derive two handle offsets
        // that approximate the preset shape between the two boundary points.
        // This is the same approach Wwise uses — no new points, just handle adjustment.
        double xRange = rightBound.X - leftBound.X;
        double yRange = rightBound.Y - leftBound.Y;

        // Sample preset at 1/3 and 2/3 to get the shape's tangent influence
        double y1 = SelectedPreset.Points.Count > 0
            ? SamplePreset(SelectedPreset, 1.0 / 3.0)
            : 1.0 / 3.0;
        double y2 = SelectedPreset.Points.Count > 0
            ? SamplePreset(SelectedPreset, 2.0 / 3.0)
            : 2.0 / 3.0;

        // Derive cubic Bézier handles that pass through those two sampled points.
        // For a cubic Bézier from P0 to P3, the control points P1 and P2 satisfy:
        // B(1/3) ≈ y1  →  P1.y ≈ (y1 - (8/27)*P0.y - (1/27)*P3.y) * (27/12)
        // B(2/3) ≈ y2  →  P2.y ≈ (y2 - (1/27)*P0.y - (8/27)*P3.y) * (27/12)
        // Simplified for normalised space where P0=0, P3=1:
        double h1y = (y1 * 3.0 - 0.0);           // right handle Y of left point
        double h2y = (y2 * 3.0 - 1.0);           // left handle Y of right point (inverted)

        // Clamp to reasonable handle range
        h1y = Math.Clamp(h1y, -1.0, 2.0);
        h2y = Math.Clamp(h2y, -1.0, 2.0);

        // Build new point list — same points, just updated handles on the boundaries
        var newPoints = ActiveCurve.Points.Select(p => p.Clone()).ToList();
        var newLeft = newPoints.First(p => Math.Abs(p.X - leftBound.X) < 1e-6);
        var newRight = newPoints.First(p => Math.Abs(p.X - rightBound.X) < 1e-6);

        // Right handle of left boundary point
        newLeft.RightHandleX = xRange / 3.0;
        newLeft.RightHandleY = h1y * yRange / 3.0;

        // Left handle of right boundary point
        newRight.LeftHandleX = -xRange / 3.0;
        newRight.LeftHandleY = -h2y * yRange / 3.0;

        // For full curve replace with just 2 points using the derived handles
        if (isFullCurve)
        {
            var twoPoint = new List<CurvePoint>
        {
            new CurvePoint(0, 0)
            {
                RightHandleX = xRange / 3.0,
                RightHandleY = h1y * yRange / 3.0
            },
            new CurvePoint(1, 1)
            {
                LeftHandleX  = -xRange / 3.0,
                LeftHandleY  = -h2y * yRange / 3.0
            }
        };
            UndoRedo.Execute(new ApplyPresetCommand(ActiveCurve, twoPoint, SelectedPreset.Name));
        }
        else
        {
            UndoRedo.Execute(new ApplyPresetCommand(ActiveCurve, newPoints, SelectedPreset.Name));
        }

        // Keep selection so user can try another preset
        foreach (var p in ActiveCurve.Points)
            p.IsSelected = p.X >= leftBound.X - 1e-6 && p.X <= rightBound.X + 1e-6;

        RaiseCurveChanged();
        Status($"Applied '{SelectedPreset.Name}' to selected region.");
    }

    /// Sample a preset's curve at a normalised x position using linear interpolation
    /// between its defined points.
    private static double SamplePreset(CurvePreset preset, double x)
    {
        var pts = preset.Points.OrderBy(p => p.X).ToList();
        if (pts.Count == 0) return x;
        if (x <= pts[0].X) return pts[0].Y;
        if (x >= pts[^1].X) return pts[^1].Y;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (x >= pts[i].X && x <= pts[i + 1].X)
            {
                double t = (x - pts[i].X) / (pts[i + 1].X - pts[i].X);
                return pts[i].Y + t * (pts[i + 1].Y - pts[i].Y);
            }
        }
        return x;
    }

    // ── File operations ───────────────────────────────────────────────────

    [RelayCommand]
    private void NewDocument()
    {
        if (!ConfirmDiscard()) return;
        Document = CurveDocument.CreateDefault();
        ActiveCurve = Document.PrimaryCurve;
        SelectedPoint = null;
        _currentFilePath = null;
        UndoRedo.Clear();
        IsDirty = false;
        RefreshAllCurves();
        RaiseCurveChanged();
        Status("New document created.");
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog
        {
            Filter = "RTPC Curve Editor (*.rtpce)|*.rtpce|All files (*.*)|*.*",
            Title = "Open project"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Document = await ProjectFileService.LoadAsync(dlg.FileName);
            ActiveCurve = Document.PrimaryCurve;
            SelectedPoint = null;
            _currentFilePath = dlg.FileName;
            UndoRedo.Clear();
            IsDirty = false;
            RefreshAllCurves();
            RaiseCurveChanged();
            Status($"Opened {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex) { Error($"Could not open file: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_currentFilePath == null) { await SaveAsAsync(); return; }
        await SaveToPathAsync(_currentFilePath);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "RTPC Curve Editor (*.rtpce)|*.rtpce",
            FileName = Document.Title,
            DefaultExt = ".rtpce",
            Title = "Save project as"
        };
        if (dlg.ShowDialog() != true) return;
        await SaveToPathAsync(dlg.FileName);
    }

    private async Task SaveToPathAsync(string path)
    {
        try
        {
            await ProjectFileService.SaveAsync(Document, path);
            _currentFilePath = path;
            IsDirty = false;
            Status($"Saved to {Path.GetFileName(path)}");
        }
        catch (Exception ex) { Error($"Save failed: {ex.Message}"); }
    }

    // ── Export ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ExportWwiseXml()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Wwise XML (*.xml)|*.xml",
            FileName = $"{ActiveCurve.Name}_rtpc",
            DefaultExt = ".xml",
            Title = "Export Wwise XML"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, WwiseXmlService.Export(Document));
            Status($"Exported Wwise XML → {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex) { Error($"Export failed: {ex.Message}"); }
    }

    [RelayCommand]
    private void ExportJson()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            FileName = $"{ActiveCurve.Name}_samples",
            DefaultExt = ".json",
            Title = "Export JSON samples"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, JsonExportService.ExportSamples(Document));
            Status($"Exported JSON → {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex) { Error($"Export failed: {ex.Message}"); }
    }

    [RelayCommand]
    private void ExportPng()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png",
            FileName = $"{ActiveCurve.Name}_curve",
            DefaultExt = ".png",
            Title = "Export PNG"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            PngExportService.Export(ActiveCurve, dlg.FileName);
            Status($"Exported PNG → {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex) { Error($"Export failed: {ex.Message}"); }
    }

    [RelayCommand]
    private void ImportWwiseXml()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            Title = "Import Wwise XML curve"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var xml = File.ReadAllText(dlg.FileName);
            Document = WwiseXmlService.Import(xml);
            ActiveCurve = Document.PrimaryCurve;
            SelectedPoint = null;
            UndoRedo.Clear();
            IsDirty = true;
            RefreshAllCurves();
            RaiseCurveChanged();
            Status($"Imported Wwise XML — {Document.PrimaryCurve.Points.Count} points.");
        }
        catch (Exception ex) { Error($"Import failed: {ex.Message}"); }
    }

    // ── Comparison curves ─────────────────────────────────────────────────

    [RelayCommand]
    private void AddComparisonCurve()
    {
        if (Document.Curves.Count >= 4) { Status("Maximum 4 comparison curves."); return; }
        var colors = new[] { "#7F77DD", "#1D9E75", "#D4537E", "#EF9F27" };
        var curve = new BezierCurve
        {
            Name = $"Curve {Document.Curves.Count + 1}",
            ColorHex = colors[Document.Curves.Count % colors.Length]
        };
        curve.Points.Add(new CurvePoint(0, 0));
        curve.Points.Add(new CurvePoint(1, 1));
        Document.Curves.Add(curve);
        RefreshAllCurves();
        Status($"Added {curve.Name}.");
    }

    [RelayCommand]
    private void SetActiveCurve(BezierCurve curve)
    {
        ActiveCurve = curve;
        SelectedPoint = null;
        RaiseCurveChanged();
    }

    [RelayCommand]
    private void RemoveCurve(BezierCurve curve)
    {
        if (Document.Curves.Count <= 1) { Status("Cannot remove the last curve."); return; }
        Document.Curves.Remove(curve);
        if (ActiveCurve == curve) ActiveCurve = Document.PrimaryCurve;
        RefreshAllCurves();
        IsDirty = true;
        RaiseCurveChanged();
        Status($"Removed {curve.Name}.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    public event Action? CurveChanged;
    private void RaiseCurveChanged() => CurveChanged?.Invoke();

    private void Status(string msg) => StatusMessage = msg;
    private void Error(string msg)
    {
        StatusMessage = $"Error: {msg}";
        MessageBox.Show(msg, "RTPC Curve Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static double Snap(double v, double grid = 0.05)
        => Math.Round(v / grid) * grid;

    private bool ConfirmDiscard()
    {
        if (!IsDirty) return true;
        var result = MessageBox.Show(
            "You have unsaved changes. Discard them?",
            "RTPC Curve Editor",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }
}