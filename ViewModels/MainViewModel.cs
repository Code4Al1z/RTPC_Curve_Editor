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

    // Repaints on any curve's ColorHex/IsVisible/etc changing, not just Document's own properties.
    private readonly HashSet<BezierCurve> _subscribedCurves = new();

    public ICollectionView PresetsView { get; }

    public MainViewModel()
    {
        _activeCurve = Document.PrimaryCurve;

        _activeCurve.EnsureEndpoints();
        _activeCurve.AutoSmoothHandles();

        RefreshAllCurves();
        SyncCurveSubscriptions();

        // First document bypasses OnDocumentChanged (field init, not the setter) — wire it up here.
        Document.PropertyChanged += (s, e) => RaiseCurveChanged();

        PresetsView = CollectionViewSource.GetDefaultView(FilteredPresets);
        PresetsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CurvePreset.Category)));

        UndoRedo.StackChanged += () =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            // RelayCommand doesn't auto-hook CommandManager — notify explicitly or Undo/Redo stay stuck disabled.
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            IsDirty = true;
        };
        RefreshPresets();
    }

    // Resubscribes on every later Document reassignment (New/Open) — constructor only covers the first one.
    partial void OnDocumentChanged(CurveDocument value)
    {
        value.PropertyChanged += (s, e) => RaiseCurveChanged();
        SyncCurveSubscriptions();
    }

    // ── Range fields (ask-first) — Inspector binds here, not to Document, so edits go through RequestRangeChange ──

    public double InputMinField
    {
        get => Document.InputMin;
        set => RequestRangeChange(isInput: true, value, Document.InputMax);
    }

    public double InputMaxField
    {
        get => Document.InputMax;
        set => RequestRangeChange(isInput: true, Document.InputMin, value);
    }

    public double OutputMinField
    {
        get => Document.OutputMin;
        set => RequestRangeChange(isInput: false, value, Document.OutputMax);
    }

    public double OutputMaxField
    {
        get => Document.OutputMax;
        set => RequestRangeChange(isInput: false, Document.OutputMin, value);
    }

    private void RequestRangeChange(bool isInput, double newMin, double newMax)
    {
        double curMin = isInput ? Document.InputMin : Document.OutputMin;
        double curMax = isInput ? Document.InputMax : Document.OutputMax;
        if (Math.Abs(newMin - curMin) < 1e-9 && Math.Abs(newMax - curMax) < 1e-9) return;

        var mode = AskRangeChangeMode();
        if (mode == null) { NotifyRangeFieldsChanged(); return; } // Cancel — revert the textbox display

        if (mode == RangeChangeMode.PreserveRealValues)
        {
            var outOfRange = isInput
                ? Document.GetOutOfRangeInputValues(newMin, newMax)
                : Document.GetOutOfRangeOutputValues(newMin, newMax);

            if (outOfRange.Count > 0 && !AskProceedDespiteOutOfRange(outOfRange, isInput))
            {
                NotifyRangeFieldsChanged();
                return; // Reject the whole range change, keep the previous range
            }
        }

        if (isInput) Document.ApplyInputRange(newMin, newMax, mode.Value);
        else Document.ApplyOutputRange(newMin, newMax, mode.Value);

        NotifyRangeFieldsChanged();
        RaiseCurveChanged();
        IsDirty = true;
    }

    private void NotifyRangeFieldsChanged()
    {
        OnPropertyChanged(nameof(InputMinField));
        OnPropertyChanged(nameof(InputMaxField));
        OnPropertyChanged(nameof(OutputMinField));
        OnPropertyChanged(nameof(OutputMaxField));
    }

    private static RangeChangeMode? AskRangeChangeMode()
    {
        var result = MessageBox.Show(
            "The mapping range is changing. What should existing points do?\n\n" +
            "Yes — keep their real values (the curve rescales to match)\n" +
            "No — keep their current position (their real values will change)\n" +
            "Cancel — don't change the range",
            "Range Changed",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => RangeChangeMode.PreserveRealValues,
            MessageBoxResult.No => RangeChangeMode.KeepPositions,
            _ => null
        };
    }

    private static bool AskProceedDespiteOutOfRange(List<double> outOfRangeValues, bool isInput)
    {
        string axis = isInput ? "input" : "output";
        string values = string.Join(", ", outOfRangeValues.Distinct().OrderBy(v => v).Select(v => v.ToString("0.##")));

        var result = MessageBox.Show(
            $"The new {axis} range doesn't cover some existing point values ({values}).\n\n" +
            "Continue and clamp those points to the nearest edge of the new range, " +
            "or cancel and keep the previous range?",
            "Range Too Small",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.OK;
    }

    private void RefreshAllCurves()
    {
        AllCurves.Clear();
        foreach (var c in Document.Curves)
            AllCurves.Add(c);
    }

    // Syncs _subscribedCurves to Document.Curves — safe to call repeatedly, never double-subscribes.
    private void SyncCurveSubscriptions()
    {
        var current = new HashSet<BezierCurve>(Document.Curves);

        foreach (var curve in _subscribedCurves.Where(c => !current.Contains(c)).ToList())
        {
            curve.PropertyChanged -= OnAnyCurvePropertyChanged;
            _subscribedCurves.Remove(curve);
        }

        foreach (var curve in current.Where(c => !_subscribedCurves.Contains(c)).ToList())
        {
            curve.PropertyChanged += OnAnyCurvePropertyChanged;
            _subscribedCurves.Add(curve);
        }
    }

    private void OnAnyCurvePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BezierCurve.IsVisible) &&
            sender is BezierCurve curve && !curve.IsVisible &&
            SelectedPoint != null && curve.Points.Contains(SelectedPoint))
        {
            SelectedPoint = null;
        }
        RaiseCurveChanged();
    }

    // Keeps ActiveCurve valid after undo/redo removes or re-adds curves — canvas draws it unconditionally.
    private void OnCurvesListChanged()
    {
        RefreshAllCurves();
        SyncCurveSubscriptions();
        if (!Document.Curves.Contains(ActiveCurve))
        {
            ActiveCurve = Document.PrimaryCurve;
            SelectedPoint = null;
        }
    }

    // ── Native C++ Evaluation ───────────────────────────────────────────────

    [ObservableProperty] private double _nativeTestInput = 0.5;

    // Guards the P/Invoke call — a missing/wrong-arch DLL would otherwise crash on launch.
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

                    // Loadable at startup but this call failed — hide the section too.
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
        if (!ActiveCurve.IsVisible) { Status("Make the curve visible before clearing it."); return; }

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
        if (!ActiveCurve.IsVisible) { Status("Make the curve visible before applying a preset."); return; }

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
            File.WriteAllText(dlg.FileName, WwiseXmlService.Export(Document, ActiveCurve));
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
        if (Document.Curves.Count >= 4) { Status("Maximum 4 comparison curves — remove one before importing another."); return; }

        var dlg = new OpenFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            Title = "Import Wwise XML as comparison curve"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var xml = File.ReadAllText(dlg.FileName);
            var imported = WwiseXmlService.Import(xml);
            var importedCurve = imported.PrimaryCurve;
            importedCurve.Name = MakeUniqueCurveName(importedCurve.Name);

            if (!RangesMatch(imported, Document))
            {
                var choice = AskImportRangeMismatch(imported);
                if (choice == null) return; // Cancel

                if (choice == ImportRangeChoice.MatchValues)
                {
                    // Union of both ranges — expands only if the import doesn't already fit.
                    double newInputMin = Math.Min(Document.InputMin, imported.InputMin);
                    double newInputMax = Math.Max(Document.InputMax, imported.InputMax);
                    double newOutputMin = Math.Min(Document.OutputMin, imported.OutputMin);
                    double newOutputMax = Math.Max(Document.OutputMax, imported.OutputMax);

                    if (Math.Abs(newInputMin - Document.InputMin) > 1e-9 || Math.Abs(newInputMax - Document.InputMax) > 1e-9)
                        Document.ApplyInputRange(newInputMin, newInputMax, RangeChangeMode.PreserveRealValues);
                    if (Math.Abs(newOutputMin - Document.OutputMin) > 1e-9 || Math.Abs(newOutputMax - Document.OutputMax) > 1e-9)
                        Document.ApplyOutputRange(newOutputMin, newOutputMax, RangeChangeMode.PreserveRealValues);

                    Document.RemapCurveIntoCurrentRange(importedCurve, imported.InputMin, imported.InputMax, imported.OutputMin, imported.OutputMax);
                    NotifyRangeFieldsChanged();
                }
                // ShapeOnly: leave points as-is, ignoring the file's original range.
            }

            UndoRedo.Execute(new AddCurveCommand(Document, importedCurve, OnCurvesListChanged));
            ActiveCurve = importedCurve;
            SelectedPoint = null;
            RaiseCurveChanged();
            Status($"Imported '{importedCurve.Name}' as a new comparison curve — {importedCurve.Points.Count} points.");
        }
        catch (Exception ex) { Error($"Import failed: {ex.Message}"); }
    }

    private enum ImportRangeChoice { MatchValues, ShapeOnly }

    private static ImportRangeChoice? AskImportRangeMismatch(CurveDocument imported)
    {
        var result = MessageBox.Show(
            $"This file's mapping range " +
            $"([{imported.InputMin:0.##}, {imported.InputMax:0.##}] → [{imported.OutputMin:0.##}, {imported.OutputMax:0.##}]) " +
            "doesn't match your current workspace's. How should it be imported?\n\n" +
            "Yes — match its real values (expands your workspace range if the file's range extends beyond it)\n" +
            "No — fit its shape into your current range as-is (ignores the file's original values)\n" +
            "Cancel — don't import it",
            "Range Mismatch",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => ImportRangeChoice.MatchValues,
            MessageBoxResult.No => ImportRangeChoice.ShapeOnly,
            _ => null
        };
    }

    private static bool RangesMatch(CurveDocument a, CurveDocument b, double epsilon = 1e-6) =>
        Math.Abs(a.InputMin - b.InputMin) < epsilon &&
        Math.Abs(a.InputMax - b.InputMax) < epsilon &&
        Math.Abs(a.OutputMin - b.OutputMin) < epsilon &&
        Math.Abs(a.OutputMax - b.OutputMax) < epsilon;

    private string MakeUniqueCurveName(string baseName)
    {
        if (Document.Curves.All(c => c.Name != baseName)) return baseName;
        int i = 2;
        string candidate;
        do { candidate = $"{baseName} ({i++})"; }
        while (Document.Curves.Any(c => c.Name == candidate));
        return candidate;
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

        // OnCurvesListChanged runs synchronously — ActiveCurve is already fixed up by here.
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