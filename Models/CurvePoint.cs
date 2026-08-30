using CommunityToolkit.Mvvm.ComponentModel;

namespace RTPCCurveEditor.Models;

public partial class CurvePoint : ObservableObject
{
    // Stable identity that survives Clone(). ApplyPresetCommand (and anything else
    // that clears+repopulates a curve's Points list) replaces every CurvePoint
    // object with a fresh clone. Commands that mutate a specific point (Move, Add,
    // Delete) must resolve the live object by this Id instead of holding a direct
    // object reference, or they silently become no-ops once a preset command has
    // run on the same curve.
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _leftHandleX = -0.05;
    [ObservableProperty] private double _leftHandleY = 0.0;
    [ObservableProperty] private double _rightHandleX = 0.05;
    [ObservableProperty] private double _rightHandleY = 0.0;
    [ObservableProperty] private bool _isSelected;

    public CurvePoint() { }

    public CurvePoint(double x, double y)
    {
        _x = x;
        _y = y;
    }

    /// <summary>
    /// Converts normalized coordinates (0..1) into actual domain units (e.g., dB or meters).
    /// </summary>
    public (double realX, double realY) GetRealWorldCoords(CurveDocument doc)
    {
        double realX = doc.InputMin + X * (doc.InputMax - doc.InputMin);
        double realY = doc.OutputMin + Y * (doc.OutputMax - doc.OutputMin);
        return (realX, realY);
    }

    /// <summary>
    /// Sets normalized coordinates (0..1) from user-entered domain units.
    /// </summary>
    public void SetFromRealWorldCoords(double realX, double realY, CurveDocument doc)
    {
        double inRange = doc.InputMax - doc.InputMin;
        double outRange = doc.OutputMax - doc.OutputMin;

        if (Math.Abs(inRange) > 1e-9)
            X = Math.Clamp((realX - doc.InputMin) / inRange, 0.0, 1.0);

        if (Math.Abs(outRange) > 1e-9)
            Y = Math.Clamp((realY - doc.OutputMin) / outRange, 0.0, 1.0);
    }

    public CurvePoint Clone()
    {
        return new CurvePoint
        {
            Id = Id,
            X = X,
            Y = Y,
            LeftHandleX = LeftHandleX,
            LeftHandleY = LeftHandleY,
            RightHandleX = RightHandleX,
            RightHandleY = RightHandleY,
            IsSelected = IsSelected
        };
    }
}