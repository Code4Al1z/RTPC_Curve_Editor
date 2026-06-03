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

        // Need at least 2 selected points (the two boundary anchors) to apply
        if (selected.Count < 2)
        {
            Status("Select at least 2 points (or a segment) to apply a preset.");
            return;
        }

        var leftBound = selected.First();
        var rightBound = selected.Last();

        // Build new point list:
        // Keep all points outside the selected region unchanged.
        // Replace everything between leftBound and rightBound (inclusive)
        // with the preset curve scaled to fit that region.
        var sorted = ActiveCurve.Points.OrderBy(p => p.X).ToList();

        var outsideLeft = sorted.Where(p => p.X < leftBound.X).ToList();
        var outsideRight = sorted.Where(p => p.X > rightBound.X).ToList();

        // Scale preset points into the [leftBound.X .. rightBound.X] x range
        // and [leftBound.Y .. rightBound.Y] y range
        double xMin = leftBound.X, xMax = rightBound.X;
        double yMin = leftBound.Y, yMax = rightBound.Y;
        double xRange = xMax - xMin;
        double yRange = yMax - yMin;

        // Use a subset of preset points that fit cleanly — skip first and last
        // since we'll pin the boundary points exactly
        var presetInner = SelectedPreset.Points
            .Skip(1).Take(SelectedPreset.Points.Count - 2)
            .Select(p => new CurvePoint(
                xMin + p.X * xRange,
                yMin + p.Y * yRange)
            {
                LeftHandleX = p.LeftHandleX * xRange,
                LeftHandleY = p.LeftHandleY * yRange,
                RightHandleX = p.RightHandleX * xRange,
                RightHandleY = p.RightHandleY * yRange
            })
            .ToList();

        // Pin boundary points exactly (preserve their handles outside the region)
        var newLeft = leftBound.Clone();
        var newRight = rightBound.Clone();

        // Adjust boundary handles to blend into the preset
        if (presetInner.Count > 0)
        {
            newLeft.RightHandleX = SelectedPreset.Points.First().RightHandleX * xRange;
            newLeft.RightHandleY = SelectedPreset.Points.First().RightHandleY * yRange;
            newRight.LeftHandleX = SelectedPreset.Points.Last().LeftHandleX * xRange;
            newRight.LeftHandleY = SelectedPreset.Points.Last().LeftHandleY * yRange;
        }

        var newPoints = outsideLeft
            .Concat(new[] { newLeft })
            .Concat(presetInner)
            .Concat(new[] { newRight })
            .Concat(outsideRight)
            .ToList();

        UndoRedo.Execute(new ApplyPresetCommand(ActiveCurve, newPoints, SelectedPreset.Name));

        // Keep the boundary points selected so the user can see the affected region
        // and try another preset immediately
        foreach (var p in ActiveCurve.Points)
        {
            p.IsSelected = p.X >= xMin - 1e-6 && p.X <= xMax + 1e-6;
        }

        RaiseCurveChanged();
        Status($"Applied '{SelectedPreset.Name}' to selected region.");
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