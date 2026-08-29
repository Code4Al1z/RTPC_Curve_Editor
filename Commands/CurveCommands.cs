using RTPCCurveEditor.Models;

namespace RTPCCurveEditor.Commands;

// ── Add Point ────────────────────────────────────────────────────────────────

public class AddPointCommand : ICurveCommand
{
    private readonly BezierCurve _curve;
    private readonly CurvePoint _point;
    public string Description => $"Add point at ({_point.X:F2}, {_point.Y:F2})";

    public AddPointCommand(BezierCurve curve, CurvePoint point)
    {
        _curve = curve;
        _point = point;
    }

    public void Execute() => _curve.Points.Add(_point);
    public void Undo() => _curve.Points.Remove(_point);
}

// ── Delete Point ─────────────────────────────────────────────────────────────

public class DeletePointCommand : ICurveCommand
{
    private readonly BezierCurve _curve;
    private readonly CurvePoint _point;
    public string Description => $"Delete point at ({_point.X:F2}, {_point.Y:F2})";

    public DeletePointCommand(BezierCurve curve, CurvePoint point)
    {
        _curve = curve;
        _point = point;
    }

    public void Execute() => _curve.Points.Remove(_point);
    public void Undo() => _curve.Points.Add(_point);
}

// ── Move Point ───────────────────────────────────────────────────────────────

public class MovePointCommand : ICurveCommand
{
    private readonly CurvePoint _point;
    private readonly Action? _onStateChanged;

    // Anchor point positions
    private readonly double _oldX, _oldY;
    private readonly double _newX, _newY;

    // Left handle positions
    private readonly double _oldLHX, _oldLHY;
    private readonly double _newLHX, _newLHY;

    // Right handle positions
    private readonly double _oldRHX, _oldRHY;
    private readonly double _newRHX, _newRHY;

    public string Description => $"Move point to ({_newX:F2}, {_newY:F2})";

    public MovePointCommand(
        CurvePoint point,
        double oldX, double oldY,
        double newX, double newY,
        double oldLHX, double oldLHY, double newLHX, double newLHY,
        double oldRHX, double oldRHY, double newRHX, double newRHY,
        Action? onStateChanged = null)
    {
        _point = point;
        _onStateChanged = onStateChanged;

        _oldX = oldX; _oldY = oldY;
        _newX = newX; _newY = newY;

        _oldLHX = oldLHX; _oldLHY = oldLHY;
        _newLHX = newLHX; _newLHY = newLHY;

        _oldRHX = oldRHX; _oldRHY = oldRHY;
        _newRHX = newRHX; _newRHY = newRHY;
    }

    public void Execute()
    {
        ApplyState(_newX, _newY, _newLHX, _newLHY, _newRHX, _newRHY);
    }

    public void Undo()
    {
        ApplyState(_oldX, _oldY, _oldLHX, _oldLHY, _oldRHX, _oldRHY);
    }

    private void ApplyState(double x, double y, double lhx, double lhy, double rhx, double rhy)
    {
        _point.X = x;
        _point.Y = y;
        _point.LeftHandleX = lhx;
        _point.LeftHandleY = lhy;
        _point.RightHandleX = rhx;
        _point.RightHandleY = rhy;

        _onStateChanged?.Invoke();
    }
}

// ── Move Handle ──────────────────────────────────────────────────────────────

public class MoveHandleCommand : ICurveCommand
{
    private readonly CurvePoint _point;
    private readonly bool _isRight;
    private readonly double _oldX, _oldY, _newX, _newY;
    private readonly Action? _onStateChanged;

    public string Description => "Adjust handle";

    public MoveHandleCommand(CurvePoint point, bool isRight,
        double oldX, double oldY, double newX, double newY,
        Action? onStateChanged = null)
    {
        _point = point;
        _isRight = isRight;
        _oldX = oldX; _oldY = oldY;
        _newX = newX; _newY = newY;
        _onStateChanged = onStateChanged;
    }

    public void Execute()
    {
        if (_isRight) { _point.RightHandleX = _newX; _point.RightHandleY = _newY; }
        else { _point.LeftHandleX = _newX; _point.LeftHandleY = _newY; }
        _onStateChanged?.Invoke();
    }

    public void Undo()
    {
        if (_isRight) { _point.RightHandleX = _oldX; _point.RightHandleY = _oldY; }
        else { _point.LeftHandleX = _oldX; _point.LeftHandleY = _oldY; }
        _onStateChanged?.Invoke();
    }
}

// ── Apply Preset ─────────────────────────────────────────────────────────────

public class ApplyPresetCommand : ICurveCommand
{
    private readonly BezierCurve _curve;
    private readonly List<CurvePoint> _oldPoints;
    private readonly List<CurvePoint> _newPoints;
    private readonly string _presetName;
    public string Description => $"Apply preset '{_presetName}'";

    public ApplyPresetCommand(BezierCurve curve, List<CurvePoint> newPoints, string presetName)
    {
        _curve = curve;
        _oldPoints = curve.Points.Select(p => p.Clone()).ToList();
        _newPoints = newPoints.Select(p => p.Clone()).ToList();
        _presetName = presetName;
    }

    public void Execute() { _curve.Points.Clear(); _curve.Points.AddRange(_newPoints.Select(p => p.Clone())); }
    public void Undo() { _curve.Points.Clear(); _curve.Points.AddRange(_oldPoints.Select(p => p.Clone())); }
}