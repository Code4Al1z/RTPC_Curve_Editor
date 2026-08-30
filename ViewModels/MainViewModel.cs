using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RTPCCurveEditor.Commands;
using RTPCCurveEditor.Models;
using RTPCCurveEditor.Native;
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
    public ObservableCollection<BezierCurve> AllCurves { get; } = new();

    private string? _currentFilePath;

    public ICollectionView PresetsView { get; }

    public MainViewModel()
    {
        _activeCurve = Document.PrimaryCurve;

        _activeCurve.EnsureEndpoints();
        _activeCurve.AutoSmoothHandles();

        RefreshAllCurves();

        Document.PropertyChanged += (s, e) => RaiseCurveChanged();

        PresetsView = CollectionViewSource.GetDefaultView(FilteredPresets);
        PresetsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CurvePreset.Category)));

        UndoRedo.StackChanged += () =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            // CommunityToolkit.Mvvm's RelayCommand does NOT hook into
            // CommandManager.RequerySuggested (that was only true of the old
            // MvvmLight-style RelayCommand). Without this, neither the Edit menu's
            // Undo/Redo items nor the Ctrl+Z/Ctrl+Y KeyBindings ever notice that
            // CanUndo/CanRedo changed, and they stay stuck at whatever they were
            // when the app started (disabled, since the stack starts empty).
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
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

    // AddCurveCommand/RemoveCurveCommand pass this as their onChanged callback.
    // Undoing an add (or redoing a remove) can leave ActiveCurve pointing at a
    // BezierCurve object that's no longer in Document.Curves at all — the
    // Inspector's curve list (AllCurves) picks that up fine since it's rebuilt
    // from Document.Curves, but CurveCanvasControl draws VM.ActiveCurve
    // unconditionally, so the orphaned curve kept rendering forever. Re-check
    // the invariant every time the curves list changes, rather than patching
    // each call site that mutates it.
    private void OnCurvesListChanged()
    {
        RefreshAllCurves();
        if (!Document.Curves.Contains(ActiveCurve))
        {
            ActiveCurve = Document.PrimaryCurve;
            SelectedPoint = null;
        }
    }

    partial void OnDocumentChanged(CurveDocument? oldValue, CurveDocument newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= OnDocumentPropertyChanged;

        if (newValue != null)
            newValue.PropertyChanged += OnDocumentPropertyChanged;

        RaiseCurveChanged();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseCurveChanged();

    // ── Native C++ Evaluation ───────────────────────────────────────────────

    [ObservableProperty] private double _nativeTestInput = 0.5;

    // Cached once known. This section calls into RTPCCurveEvaluatorNative.dll
    // via P/Invoke, which throws (DllNotFoundException / BadImageFormatException
    // / EntryPointNotFoundException) if the native project wasn't built, or was
    // built for a different platform than the one the DLL was copied for. That
    // exception would otherwise come straight out of a data-bound property
    // getter with nothing in the app to catch it, crashing the app on launch.
    private bool? _nativeEvaluatorAvailable;

    public bool NativeEvaluatorAvailable
    {
        get
        {
            if (_nativeEvaluatorAvailable.HasValue) return _nativeEvaluatorAvailable.Value;
            _nativeEvaluatorAvailable = TryEvaluateNative(0, 0, 0, 0, 1, 1, 1, 1, 0.5, out _);
            return _nativeEvaluatorAvailable.Value;
        }
    }

    private static bool TryEvaluateNative(
        double p0x, double p0y, double c0x, double c0y,
        double c1x, double c1y, double p1x, double p1y,
        double x, out double result)
    {
        try
        {
            result = NativeEvaluator.EvaluateCubicBezierYAtX(p0x, p0y, c0x, c0y, c1x, c1y, p1x, p1y, x);
            return true;
        }
        catch (Exception)
        {
            result = 0.0;
            return false;
        }
    }

    public double NativeTestOutput
    {
        get
        {
            if (!NativeEvaluatorAvailable) return 0.0;
            if (ActiveCurve?.Points == null) return 0.0;
            var points = ActiveCurve.Points.OrderBy(p => p.X).ToList();
            if (points.Count < 2) return 0.0;

            double x = Math.Clamp(NativeTestInput, points.First().X, points.Last().X);

            for (int i = 0; i < points.Count - 1; i++)
            {
                var p0 = points[i];
                var p1 = points[i + 1];

                if (x >= p0.X && x <= p1.X)
                {
                    if (TryEvaluateNative(
                        p0.X, p0.Y,
                        p0.X + p0.RightHandleX, p0.Y + p0.RightHandleY,
                        p1.X + p1.LeftHandleX, p1.Y + p1.LeftHandleY,
                        p1.X, p1.Y,
                        x, out double result))
                    {
                        return result;
                    }

                    // The DLL was loadable at startup but this specific call
                    // failed; flip the flag so the UI hides the section too.
                    _nativeEvaluatorAvailable = false;
                    OnPropertyChanged(nameof(NativeEvaluatorAvailable));
                    return 0.0;
                }
            }

            return 0.0;
        }
    }

    public double NativeTestOutputReal =>
        Document.OutputMin + NativeTestOutput * (Document.OutputMax - Document.OutputMin);

    public double NativeTestInputReal =>
        Document.InputMin + NativeTestInput * (Document.InputMax - Document.InputMin);

    partial void OnNativeTestInputChanged(double value) => NotifyNativeTestProperties();

    private void NotifyNativeTestProperties()
    {
        OnPropertyChanged(nameof(NativeTestOutput));
        OnPropertyChanged(nameof(NativeTestOutputReal));
        OnPropertyChanged(nameof(NativeTestInputReal));
    }

    // ── Mapped Real-World Point Coordinates ──────────────────────────────────

    public double SelectedPointRealX
    {
        get
        {
            if (SelectedPoint == null) return 0.0;
            return Document.InputMin + SelectedPoint.X * (Document.InputMax - Document.InputMin);
        }
        set
        {
            if (SelectedPoint == null) return;
            double range = Document.InputMax - Document.InputMin;
            if (Math.Abs(range) < 1e-6) return;
            double normalizedX = Math.Clamp((value - Document.InputMin) / range, 0.0, 1.0);

            MovePoint(SelectedPoint, normalizedX, SelectedPoint.Y);
            OnPropertyChanged(nameof(SelectedPointRealX));
        }
    }

    public double SelectedPointRealY
    {
        get
        {
            if (SelectedPoint == null) return 0.0;
            return Document.OutputMin + SelectedPoint.Y * (Document.OutputMax - Document.OutputMin);
        }
        set
        {
            if (SelectedPoint == null) return;
            double range = Document.OutputMax - Document.OutputMin;
            if (Math.Abs(range) < 1e-6) return;
            double normalizedY = Math.Clamp((value - Document.OutputMin) / range, 0.0, 1.0);

            MovePoint(SelectedPoint, SelectedPoint.X, normalizedY);
            OnPropertyChanged(nameof(SelectedPointRealY));
        }
    }

    partial void OnSelectedPointChanged(CurvePoint? value)
    {
        OnPropertyChanged(nameof(SelectedPointRealX));
        OnPropertyChanged(nameof(SelectedPointRealY));
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
        var clearedPoints = new List<CurvePoint> { new(0, 0), new(1, 1) };

        var tempCurve = new BezierCurve { Points = clearedPoints };
        tempCurve.EnsureEndpoints();
        tempCurve.AutoSmoothHandles();

        UndoRedo.Execute(new ApplyPresetCommand(ActiveCurve, tempCurve.Points, "Clear"));
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
        if (pt == null) return;

        double oldX = pt.X;
        double oldY = pt.Y;
        double oldLHX = pt.LeftHandleX;
        double oldLHY = pt.LeftHandleY;
        double oldRHX = pt.RightHandleX;
        double oldRHY = pt.RightHandleY;

        if (SnapToGrid) { newX = Snap(newX); newY = Snap(newY); }
        newX = Math.Clamp(newX, 0, 1);
        newY = Math.Clamp(newY, 0, 1);

        UndoRedo.Execute(new MovePointCommand(
            ActiveCurve,
            pt,
            oldX, oldY, newX, newY,
            oldLHX, oldLHY, oldLHX, oldLHY,
            oldRHX, oldRHY, oldRHX, oldRHY,
            RaiseCurveChanged
        ));
    }

    public void ClearPointSelection()
    {
        foreach (var p in ActiveCurve.Points)
            p.IsSelected = false;
        SelectedPoint = null;
    }

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

        double xRange = rightBound.X - leftBound.X;
        double yRange = rightBound.Y - leftBound.Y;

        var presetLeft = SelectedPreset.Points.OrderBy(p => p.X).First();
        var presetRight = SelectedPreset.Points.OrderBy(p => p.X).Last();

        var sorted = ActiveCurve.Points.OrderBy(p => p.X).ToList();
        bool isFullCurve = Math.Abs(leftBound.X - sorted.First().X) < 1e-6
                        && Math.Abs(rightBound.X - sorted.Last().X) < 1e-6
                        && selected.Count == sorted.Count;

        if (isFullCurve)
        {
            var twoPoint = new List<CurvePoint>
            {
                new CurvePoint(presetLeft.X, presetLeft.Y)
                {
                    RightHandleX = presetLeft.RightHandleX,
                    RightHandleY = presetLeft.RightHandleY
                },
                new CurvePoint(presetRight.X, presetRight.Y)
                {
                    LeftHandleX  = presetRight.LeftHandleX,
                    LeftHandleY  = presetRight.LeftHandleY
                }
            };
            UndoRedo.Execute(new ApplyPresetCommand(ActiveCurve, twoPoint, SelectedPreset.Name));
        }
        else
        {
            var newPoints = new List<CurvePoint>();
            foreach (var p in sorted)
            {
                if (p.X < leftBound.X - 1e-6 || p.X > rightBound.X + 1e-6)
                {
                    newPoints.Add(p.Clone());
                }
            }

            var newLeft = leftBound.Clone();
            var newRight = rightBound.Clone();

            newLeft.RightHandleX = presetLeft.RightHandleX * xRange;
            newLeft.RightHandleY = presetLeft.RightHandleY * yRange;
            newRight.LeftHandleX = presetRight.LeftHandleX * xRange;
            newRight.LeftHandleY = presetRight.LeftHandleY * yRange;

            newPoints.Add(newLeft);
            newPoints.Add(newRight);

            UndoRedo.Execute(new ApplyPresetCommand(ActiveCurve, newPoints.OrderBy(p => p.X).ToList(), SelectedPreset.Name));
        }

        foreach (var p in ActiveCurve.Points)
        {
            p.IsSelected = p.X >= leftBound.X - 1e-6 && p.X <= rightBound.X + 1e-6;
        }
        SelectedPoint = ActiveCurve.Points.FirstOrDefault(p => p.IsSelected);

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

        ActiveCurve.EnsureEndpoints();
        ActiveCurve.AutoSmoothHandles();

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

        curve.EnsureEndpoints();
        curve.AutoSmoothHandles();

        UndoRedo.Execute(new AddCurveCommand(Document, curve, OnCurvesListChanged));
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

        // OnCurvesListChanged (passed below) runs synchronously as part of
        // Execute()/Undo(), so ActiveCurve is already corrected by the time
        // UndoRedo.Execute returns — no separate fixup needed here.
        UndoRedo.Execute(new RemoveCurveCommand(Document, curve, OnCurvesListChanged));
        RaiseCurveChanged();
        Status($"Removed {curve.Name}.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    public event Action? CurveChanged;
    public void RaiseCurveChanged()
    {
        NotifyNativeTestProperties();
        OnPropertyChanged(nameof(SelectedPointRealX));
        OnPropertyChanged(nameof(SelectedPointRealY));
        CurveChanged?.Invoke();
    }

    private void Status(string msg) => StatusMessage = msg;
    private void Error(string msg)
    {
        StatusMessage = $"Error: {msg}";
        MessageBox.Show(msg, "RTPC Curve Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static double Snap(double v, double grid = 0.05)
        => Math.Round(v / grid) * grid;

    public bool ConfirmDiscard()
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