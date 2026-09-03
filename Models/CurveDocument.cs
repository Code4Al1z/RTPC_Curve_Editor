using CommunityToolkit.Mvvm.ComponentModel;

namespace RTPCCurveEditor.Models;

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
        set
        {
            double clamped = Math.Max(0.0, value); // Clamp X-axis to >= 0
            double old = _inputMin;
            if (SetProperty(ref _inputMin, clamped))
                RemapCurvesToPreserveRealValues(old, InputMax, OutputMin, OutputMax);
        }
    }

    private double _inputMax = 100.0;
    public double InputMax
    {
        get => _inputMax;
        set
        {
            double old = _inputMax;
            if (SetProperty(ref _inputMax, value))
                RemapCurvesToPreserveRealValues(InputMin, old, OutputMin, OutputMax);
        }
    }

    private double _outputMin = -96.0; // Y-axis can be negative (e.g., -96 dB)
    public double OutputMin
    {
        get => _outputMin;
        set
        {
            double old = _outputMin;
            if (SetProperty(ref _outputMin, value))
                RemapCurvesToPreserveRealValues(InputMin, InputMax, old, OutputMax);
        }
    }

    private double _outputMax = 0.0;
    public double OutputMax
    {
        get => _outputMax;
        set
        {
            double old = _outputMax;
            if (SetProperty(ref _outputMax, value))
                RemapCurvesToPreserveRealValues(InputMin, InputMax, OutputMin, old);
        }
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<BezierCurve> Curves { get; set; } = new();

    /// <summary>The primary (first) curve in the document.</summary>
    public BezierCurve PrimaryCurve => Curves.Count > 0 ? Curves[0] : new BezierCurve();

    private void RemapCurvesToPreserveRealValues(
        double oldInputMin, double oldInputMax,
        double oldOutputMin, double oldOutputMax)
    {
        double oldInputRange = oldInputMax - oldInputMin;
        double newInputRange = InputMax - InputMin;
        double oldOutputRange = oldOutputMax - oldOutputMin;
        double newOutputRange = OutputMax - OutputMin;

        if (Math.Abs(newInputRange) < 1e-9 || Math.Abs(newOutputRange) < 1e-9) return;

        double scaleX = oldInputRange / newInputRange;
        double offsetX = (oldInputMin - InputMin) / newInputRange;
        double scaleY = oldOutputRange / newOutputRange;
        double offsetY = (oldOutputMin - OutputMin) / newOutputRange;

        foreach (var curve in Curves)
        {
            foreach (var pt in curve.Points)
            {
                pt.X = Math.Clamp(pt.X * scaleX + offsetX, 0.0, 1.0);
                pt.Y = Math.Clamp(pt.Y * scaleY + offsetY, 0.0, 1.0);
                pt.LeftHandleX *= scaleX;
                pt.LeftHandleY *= scaleY;
                pt.RightHandleX *= scaleX;
                pt.RightHandleY *= scaleY;
            }
        }

        OnPropertyChanged(nameof(Curves));
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