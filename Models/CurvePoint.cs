using System.Text.Json.Serialization;

namespace RTPCCurveEditor.Models;

/// <summary>
/// A single point on the RTPC curve, with Bézier control handles.
/// X and Y are normalised 0..1 — the export layer maps them to real Wwise ranges.
/// </summary>
public class CurvePoint
{
    public double X { get; set; }
    public double Y { get; set; }

    // Left and right tangent handles, expressed as offsets from the anchor point.
    public double LeftHandleX { get; set; } = -0.05;
    public double LeftHandleY { get; set; } = 0.0;
    public double RightHandleX { get; set; } = 0.05;
    public double RightHandleY { get; set; } = 0.0;

    [JsonIgnore]
    public bool IsSelected { get; set; }

    public CurvePoint() { }

    public CurvePoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public CurvePoint Clone() => new()
    {
        X = X, Y = Y,
        LeftHandleX = LeftHandleX, LeftHandleY = LeftHandleY,
        RightHandleX = RightHandleX, RightHandleY = RightHandleY
    };
}
