using CommunityToolkit.Mvvm.ComponentModel;

namespace RTPCCurveEditor.Models;

/// <summary>How Apply*Range should treat existing points.</summary>
public enum RangeChangeMode
{
    PreserveRealValues, // recompute normalized position so real value stays fixed
    KeepPositions       // leave normalized position untouched — real value shifts
}

/// <summary>Root document model matching Wwise's normalized curve architecture (X >= 0, Y can be negative).</summary>
public partial class CurveDocument : ObservableObject
{
    [ObservableProperty] private string _title = "Untitled Project";
    [ObservableProperty] private string _wwiseRtpcName = "";

    private double _inputMin = 0.0;
    public double InputMin
    {
        get => _inputMin;
        set => SetProperty(ref _inputMin, Math.Max(0.0, value)); // clamp X-axis to >= 0
    }

    [ObservableProperty] private double _inputMax = 100.0;
    [ObservableProperty] private double _outputMin = -96.0;
    [ObservableProperty] private double _outputMax = 0.0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<BezierCurve> Curves { get; set; } = new();

    public BezierCurve PrimaryCurve => Curves.Count > 0 ? Curves[0] : new BezierCurve();

    // Model exposes facts + actions only — MainViewModel decides the mode and shows any UI.

    /// <summary>Real point values that would fall outside [newMin, newMax] under PreserveRealValues.</summary>
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

    /// <summary>Applies a new input (X) range; PreserveRealValues clamps out-of-range points to the nearest edge.</summary>
    public void ApplyInputRange(double newMin, double newMax, RangeChangeMode mode)
    {
        newMin = Math.Max(0.0, newMin);
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

    /// <summary>Remaps a curve not yet in Curves (e.g. a fresh import) from its own range into this document's current range.</summary>
    public void RemapCurveIntoCurrentRange(
        BezierCurve curve,
        double oldInputMin, double oldInputMax,
        double oldOutputMin, double oldOutputMax)
    {
        RemapCurvePoints(curve, oldInputMin, oldInputMax, InputMin, InputMax, isInput: true);
        RemapCurvePoints(curve, oldOutputMin, oldOutputMax, OutputMin, OutputMax, isInput: false);
    }

    // Handles scale the same as position since Bezier curves are affine-invariant — shape stays exact.
    private void RemapAxis(double oldMin, double oldMax, double newMin, double newMax, bool isInput)
    {
        foreach (var curve in Curves)
            RemapCurvePoints(curve, oldMin, oldMax, newMin, newMax, isInput);
    }

    private static void RemapCurvePoints(BezierCurve curve, double oldMin, double oldMax, double newMin, double newMax, bool isInput)
    {
        double oldRange = oldMax - oldMin;
        double newRange = newMax - newMin;
        if (Math.Abs(newRange) < 1e-9) return;

        double scale = oldRange / newRange;
        double offset = (oldMin - newMin) / newRange;

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