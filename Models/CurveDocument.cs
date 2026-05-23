namespace RTPCCurveEditor.Models;

/// <summary>
/// The root document model saved as a .rtpce file.
/// Contains one or more curves (for comparison view) plus metadata.
/// </summary>
public class CurveDocument
{
    public string Title { get; set; } = "Untitled Project";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public string WwiseRtpcName { get; set; } = "";
    public double InputMin { get; set; } = 0;
    public double InputMax { get; set; } = 100;
    public double OutputMin { get; set; } = 0;
    public double OutputMax { get; set; } = 1;
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
