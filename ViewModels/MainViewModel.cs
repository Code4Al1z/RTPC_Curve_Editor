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
    public ObservableCollection<BezierCurve> AllCurves => new(Document.Curves);

    private string? _currentFilePath;

    public MainViewModel()
    {
        _activeCurve = Document.PrimaryCurve;
        UndoRedo.StackChanged += () =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            IsDirty = true;
        };
        RefreshPresets();
    }

    // ── Undo/Redo ─────────────────────────────────────────────────────────

    public bool CanUndo => UndoRedo.CanUndo;
    public bool CanRedo => UndoRedo.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() { UndoRedo.Undo(); RaiseCurveChanged(); }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() { UndoRedo.Redo(); RaiseCurveChanged(); }

    // ── Clear curve ─────────────────────────────────────────────────────────
    [RelayCommand]
    private void ClearCurve()
    {
        var curve = ActiveCurve;
        UndoRedo.Execute(new ApplyPresetCommand(curve,
            new List<CurvePoint>
            {
            new CurvePoint(0, 0),
            new CurvePoint(1, 1)
            }, "Clear"));
        SelectedPoint = null;
        RaiseCurveChanged();
        Status("Curve cleared.");
    }

    // ── Point manipulation (called by canvas) ─────────────────────────────

    public void AddPoint(double x, double y)
    {
        if (SnapToGrid) { x = Snap(x); y = Snap(y); }
        var pt = new CurvePoint(x, y);
        UndoRedo.Execute(new AddPointCommand(ActiveCurve, pt));
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
        UndoRedo.Execute(new ApplyPresetCommand(ActiveCurve, SelectedPreset.Points, SelectedPreset.Name));
        RaiseCurveChanged();
        Status($"Applied preset '{SelectedPreset.Name}'");
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
            Title  = "Open project"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Document      = await ProjectFileService.LoadAsync(dlg.FileName);
            ActiveCurve   = Document.PrimaryCurve;
            SelectedPoint = null;
            _currentFilePath = dlg.FileName;
            UndoRedo.Clear();
            IsDirty = false;
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
            Filter           = "RTPC Curve Editor (*.rtpce)|*.rtpce",
            FileName         = Document.Title,
            DefaultExt       = ".rtpce",
            Title            = "Save project as"
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
            Filter     = "Wwise XML (*.xml)|*.xml",
            FileName   = $"{ActiveCurve.Name}_rtpc",
            DefaultExt = ".xml",
            Title      = "Export Wwise XML"
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
            Filter     = "JSON (*.json)|*.json",
            FileName   = $"{ActiveCurve.Name}_samples",
            DefaultExt = ".json",
            Title      = "Export JSON samples"
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
            Filter     = "PNG Image (*.png)|*.png",
            FileName   = $"{ActiveCurve.Name}_curve",
            DefaultExt = ".png",
            Title      = "Export PNG"
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
            Title  = "Import Wwise XML curve"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var xml = File.ReadAllText(dlg.FileName);
            Document    = WwiseXmlService.Import(xml);
            ActiveCurve = Document.PrimaryCurve;
            SelectedPoint = null;
            UndoRedo.Clear();
            IsDirty = true;
            RaiseCurveChanged();
            Status($"Imported Wwise XML — {Document.PrimaryCurve.Points.Count} points.");
        }
        catch (Exception ex) { Error($"Import failed: {ex.Message}"); }
    }

    // ── Comparison curves ────────────────────────────────────────────────

    [RelayCommand]
    private void AddComparisonCurve()
    {
        if (Document.Curves.Count >= 4)
        {
            Status("Maximum 4 comparison curves.");
            return;
        }
        var colors = new[] { "#7F77DD", "#1D9E75", "#D4537E", "#EF9F27" };
        var curve  = new BezierCurve
        {
            Name     = $"Curve {Document.Curves.Count + 1}",
            ColorHex = colors[Document.Curves.Count % colors.Length]
        };
        curve.Points.Add(new CurvePoint(0, 0));
        curve.Points.Add(new CurvePoint(1, 1));
        Document.Curves.Add(curve);
        OnPropertyChanged(nameof(AllCurves));
        Status($"Added {curve.Name}.");
    }

    [RelayCommand]
    private void SetActiveCurve(BezierCurve curve)
    {
        ActiveCurve   = curve;
        SelectedPoint = null;
        RaiseCurveChanged();
    }

    // ── Remove button for comparison curves ──────────────────────────────────

    [RelayCommand]
    private void RemoveCurve(BezierCurve curve)
    {
        if (Document.Curves.Count <= 1) { Status("Cannot remove the last curve."); return; }
        Document.Curves.Remove(curve);
        if (ActiveCurve == curve) ActiveCurve = Document.PrimaryCurve;
        OnPropertyChanged(nameof(AllCurves));
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
