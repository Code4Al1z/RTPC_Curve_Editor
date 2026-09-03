using CommunityToolkit.Mvvm.ComponentModel;

namespace RTPCCurveEditor.Models;

/// <summary>How ApplyInputRange/ApplyOutputRange should treat existing points.</summary>
public enum RangeChangeMode
{
    /// <summary>Recompute normalized positions so each point's real value stays fixed.</summary>
    PreserveRealValues,
    /// <summary>Leave normalized positions untouched — real values shift instead.</summary>
    KeepPositions
}

/// <summary>
/// Root document model matching Wwise's normalized curve architecture.
/// X-axis (Game Parameter) is non-negative (>= 0), while Y-axis (Audio Value) 
/// can span negative ranges (e.g. -96 dB to 0 dB).
/// </summary>
public partial class CurveDocument : ObservableObject
{
    [ObservableProperty] private string _title = "Untitled Project";
    [ObservableProperty] private string _wwiseRtpcName = "";

    private double _inputMin = 0.0;
    public double InputMin
    {
        get => _inputMin;
        set => SetProperty(ref _inputMin, Math.Max(0.0, value)); // Clamp X-axis to >= 0
    }

    [ObservableProperty] private double _inputMax = 100.0;
    [ObservableProperty] private double _outputMin = -96.0; // Y-axis can be negative (e.g., -96 dB)
    [ObservableProperty] private double _outputMax = 0.0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<BezierCurve> Curves { get; set; } = new();

    /// <summary>The primary (first) curve in the document.</summary>
    public BezierCurve PrimaryCurve => Curves.Count > 0 ? Curves[0] : new BezierCurve();

    // ── Range change (asks-first, model-side half) ──────────────────────────
    
    /// <summary>
    /// Real (not normalized) point values that would fall outside [newMin, newMax]
    /// under PreserveRealValues mode — empty if the new range covers everything.
    /// Call before ApplyInputRange(..., PreserveRealValues) so the caller can warn
    /// the user first.
    /// </summary>
    public List<double> GetOutOfRangeInputValues(double newMin, double newMax)
    {
        newMin = Math.Max(0.0, newMin);
        var result = new List<double>();
        double oldRange = InputMax - InputMin;
        if (Math.Abs(oldRange) < 1e-9) return result;

        foreach (var curve in Curves)
            foreach (var pt in curve.Points)
            {
                double realX = InputMin + pt.X * oldRange;
                if (realX < newMin - 1e-6 || realX > newMax + 1e-6)
                    result.Add(realX);
            }
        return result;
    }

    /// <summary>Same as GetOutOfRangeInputValues, for the output (Y) axis.</summary>
    public List<double> GetOutOfRangeOutputValues(double newMin, double newMax)
    {
        var result = new List<double>();
        double oldRange = OutputMax - OutputMin;
        if (Math.Abs(oldRange) < 1e-9) return result;

        foreach (var curve in Curves)
            foreach (var pt in curve.Points)
            {
                double realY = OutputMin + pt.Y * oldRange;
                if (realY < newMin - 1e-6 || realY > newMax + 1e-6)
                    result.Add(realY);
            }
        return result;
    }

    /// <summary>
    /// Applies a new input (X) range. In PreserveRealValues mode, points whose
    /// real value falls outside [newMin, newMax] are clamped to the nearest
    /// edge (0 or 1 normalized) — check GetOutOfRangeInputValues first if you
    /// want to warn about that before calling this.
    /// </summary>
    public void ApplyInputRange(double newMin, double newMax, RangeChangeMode mode)
    {
        newMin = Math.Max(0.0, newMin); // matches InputMin's own clamp
        if (mode == RangeChangeMode.PreserveRealValues)
            RemapAxis(InputMin, InputMax, newMin, newMax, isInput: true);

        InputMin = newMin;
        InputMax = newMax;
    }

    /// <summary>Same as ApplyInputRange, for the output (Y) axis.</summary>
    public void ApplyOutputRange(double newMin, double newMax, RangeChangeMode mode)
    {
        if (mode == RangeChangeMode.PreserveRealValues)
            RemapAxis(OutputMin, OutputMax, newMin, newMax, isInput: false);

        OutputMin = newMin;
        OutputMax = newMax;
    }

    private void RemapAxis(double oldMin, double oldMax, double newMin, double newMax, bool isInput)
    {
        double oldRange = oldMax - oldMin;
        double newRange = newMax - newMin;
        if (Math.Abs(newRange) < 1e-9) return;

        double scale = oldRange / newRange;
        double offset = (oldMin - newMin) / newRange;

        foreach (var curve in Curves)
        {
            foreach (var pt in curve.Points)
            {
                if (isInput)
                {
                    pt.X = Math.Clamp(pt.X * scale + offset, 0.0, 1.0);
                    pt.LeftHandleX *= scale;
                    pt.RightHandleX *= scale;
                }
                else
                {
                    pt.Y = Math.Clamp(pt.Y * scale + offset, 0.0, 1.0);
                    pt.LeftHandleY *= scale;
                    pt.RightHandleY *= scale;
                }
            }
        }
    }

    public static CurveDocument CreateDefault()
    {
        var doc = new CurveDocument { Title = "New RTPC Curve" };
        var curve = new BezierCurve { Name = "Curve 1", ColorHex = "#7F77DD" };
        curve.Points.Add(new CurvePoint(0.0, 0.0));
        curve.Points.Add(new CurvePoint(1.0, 1.0));
        doc.Curves.Add(curve);
        return doc;
    }
}