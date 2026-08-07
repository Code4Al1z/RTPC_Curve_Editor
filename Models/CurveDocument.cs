using CommunityToolkit.Mvvm.ComponentModel;

namespace RTPCCurveEditor.Models;

/// <summary>
/// The root document model saved as a .rtpce file.
/// Inherits ObservableObject so the view model can observe property changes live.
/// </summary>
public partial class CurveDocument : ObservableObject
{
    [ObservableProperty] private string _title = "Untitled Project";
    [ObservableProperty] private string _wwiseRtpcName = "";
    [ObservableProperty] private double _inputMin = 0.0;
    [ObservableProperty] private double _inputMax = 100.0;
    [ObservableProperty] private double _outputMin = 0.0;
    [ObservableProperty] private double _outputMax = 100.0;

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