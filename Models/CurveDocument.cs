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