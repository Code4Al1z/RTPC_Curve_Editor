using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RTPCCurveEditor.Models;

/// <summary>
/// A single point on the RTPC curve, with Bézier control handles.
/// X and Y are normalised 0..1 — the export layer maps them to real Wwise ranges.
/// </summary>
public partial class CurvePoint : ObservableObject
{
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;

    // Left and right tangent handles, expressed as offsets from the anchor point.
    public double LeftHandleX { get; set; } = -0.05;
    public double LeftHandleY { get; set; } = 0.0;
    public double RightHandleX { get; set; } = 0.05;
    public double RightHandleY { get; set; } = 0.0;

    [JsonIgnore]
    [ObservableProperty] private bool _isSelected;

    public CurvePoint() { }

    public CurvePoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public CurvePoint Clone() => new()
    {
        X = X,
        Y = Y,
        LeftHandleX = LeftHandleX,
        LeftHandleY = LeftHandleY,
        RightHandleX = RightHandleX,
        RightHandleY = RightHandleY,
        IsSelected = IsSelected
    };
}