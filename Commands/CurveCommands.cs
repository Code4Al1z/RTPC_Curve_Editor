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
    private readonly double _oldX, _oldY, _newX, _newY;
    public string Description => $"Move point to ({_newX:F2}, {_newY:F2})";

    public MovePointCommand(CurvePoint point, double oldX, double oldY, double newX, double newY)
    {
        _point = point;
        _oldX = oldX; _oldY = oldY;
        _newX = newX; _newY = newY;
    }

    public void Execute() { _point.X = _newX; _point.Y = _newY; }
    public void Undo()    { _point.X = _oldX; _point.Y = _oldY; }
}

// ── Move Handle ──────────────────────────────────────────────────────────────

public class MoveHandleCommand : ICurveCommand
{
    private readonly CurvePoint _point;
    private readonly bool _isRight;
    private readonly double _oldX, _oldY, _newX, _newY;
    public string Description => "Adjust handle";

    public MoveHandleCommand(CurvePoint point, bool isRight,
        double oldX, double oldY, double newX, double newY)
    {
        _point = point; _isRight = isRight;
        _oldX = oldX; _oldY = oldY; _newX = newX; _newY = newY;
    }

    public void Execute()
    {
        if (_isRight) { _point.RightHandleX = _newX; _point.RightHandleY = _newY; }
        else          { _point.LeftHandleX  = _newX; _point.LeftHandleY  = _newY; }
    }
    public void Undo()
    {
        if (_isRight) { _point.RightHandleX = _oldX; _point.RightHandleY = _oldY; }
        else          { _point.LeftHandleX  = _oldX; _point.LeftHandleY  = _oldY; }
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
    public void Undo()    { _curve.Points.Clear(); _curve.Points.AddRange(_oldPoints.Select(p => p.Clone())); }
}
