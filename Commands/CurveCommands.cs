using RTPCCurveEditor.Models;

namespace RTPCCurveEditor.Commands;

// ── Add Point ────────────────────────────────────────────────────────────────

public class AddPointCommand : ICurveCommand
{
    private readonly BezierCurve _curve;
    private readonly CurvePoint _point;
    private readonly Guid _pointId;
    public string Description => $"Add point at ({_point.X:F2}, {_point.Y:F2})";

    public AddPointCommand(BezierCurve curve, CurvePoint point)
    {
        _curve = curve;
        _point = point;
        _pointId = point.Id;
    }

    public void Execute()
    {
        // A preset command elsewhere on the undo stack may have already swapped
        // this curve's Points list with fresh clones since this command was made
        // (or since it was last undone). Guard against duplicating the point if a
        // live one with this Id already exists.
        if (!_curve.Points.Any(p => p.Id == _pointId))
            _curve.Points.Add(_point);
    }

    public void Undo()
    {
        var existing = _curve.Points.FirstOrDefault(p => p.Id == _pointId);
        if (existing != null)
            _curve.Points.Remove(existing);
    }
}

// ── Delete Point ─────────────────────────────────────────────────────────────

public class DeletePointCommand : ICurveCommand
{
    private readonly BezierCurve _curve;
    private readonly CurvePoint _point;
    private readonly Guid _pointId;
    public string Description => $"Delete point at ({_point.X:F2}, {_point.Y:F2})";

    public DeletePointCommand(BezierCurve curve, CurvePoint point)
    {
        _curve = curve;
        _point = point;
        _pointId = point.Id;
    }

    public void Execute()
    {
        var existing = _curve.Points.FirstOrDefault(p => p.Id == _pointId);
        if (existing != null)
            _curve.Points.Remove(existing);
    }

    public void Undo()
    {
        if (!_curve.Points.Any(p => p.Id == _pointId))
            _curve.Points.Add(_point);
    }
}

// ── Move Point ───────────────────────────────────────────────────────────────

public class MovePointCommand : ICurveCommand
{
    private readonly BezierCurve _curve;
    private readonly Guid _pointId;
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
        BezierCurve curve,
        CurvePoint point,
        double oldX, double oldY,
        double newX, double newY,
        double oldLHX, double oldLHY, double newLHX, double newLHY,
        double oldRHX, double oldRHY, double newRHX, double newRHY,
        Action? onStateChanged = null)
    {
        _curve = curve;
        _pointId = point.Id;
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
        // Resolve the live point by Id rather than trusting a stored reference:
        // a preset command elsewhere on the stack may have replaced the actual
        // CurvePoint object (via a clear+clone-repopulate) since this command
        // was created.
        var point = _curve.Points.FirstOrDefault(p => p.Id == _pointId);
        if (point == null) return;

        point.X = x;
        point.Y = y;
        point.LeftHandleX = lhx;
        point.LeftHandleY = lhy;
        point.RightHandleX = rhx;
        point.RightHandleY = rhy;

        _onStateChanged?.Invoke();
    }
}

// ── Move Handle ──────────────────────────────────────────────────────────────

public class MoveHandleCommand : ICurveCommand
{
    private readonly BezierCurve _curve;
    private readonly Guid _pointId;
    private readonly bool _isRight;
    private readonly double _oldX, _oldY, _newX, _newY;
    private readonly Action? _onStateChanged;

    public string Description => "Adjust handle";

    public MoveHandleCommand(BezierCurve curve, CurvePoint point, bool isRight,
        double oldX, double oldY, double newX, double newY,
        Action? onStateChanged = null)
    {
        _curve = curve;
        _pointId = point.Id;
        _isRight = isRight;
        _oldX = oldX; _oldY = oldY;
        _newX = newX; _newY = newY;
        _onStateChanged = onStateChanged;
    }

    public void Execute() => ApplyState(_newX, _newY);
    public void Undo() => ApplyState(_oldX, _oldY);

    private void ApplyState(double x, double y)
    {
        // See MovePointCommand.ApplyState: resolve by Id, don't trust the
        // stored reference, since a preset command may have replaced it.
        var point = _curve.Points.FirstOrDefault(p => p.Id == _pointId);
        if (point == null) return;

        if (_isRight) { point.RightHandleX = x; point.RightHandleY = y; }
        else { point.LeftHandleX = x; point.LeftHandleY = y; }
        _onStateChanged?.Invoke();
    }
}

// ── Insert Point Seamlessly ──────────────────────────────────────────────────

// BezierCurve.InsertPointSeamlessly doesn't just add a point: it also adjusts
// the neighboring points' handles so the curve's shape doesn't visibly change.
// That's a whole-curve state change, so this command snapshots the full point
// list before/after, the same way ApplyPresetCommand does, rather than trying
// to track the individual point/handle edits separately.
public class InsertPointCommand : ICurveCommand
{
    private readonly BezierCurve _curve;
    private readonly List<CurvePoint> _oldPoints;
    private readonly List<CurvePoint> _newPoints;
    private readonly Guid _insertedPointId;
    public string Description => "Insert point";

    public InsertPointCommand(BezierCurve curve, List<CurvePoint> oldPointsSnapshot, List<CurvePoint> newPointsSnapshot, Guid insertedPointId)
    {
        _curve = curve;
        _oldPoints = oldPointsSnapshot.Select(p => p.Clone()).ToList();
        _newPoints = newPointsSnapshot.Select(p => p.Clone()).ToList();
        _insertedPointId = insertedPointId;
    }

    public void Execute() { _curve.Points.Clear(); _curve.Points.AddRange(_newPoints.Select(p => p.Clone())); }
    public void Undo() { _curve.Points.Clear(); _curve.Points.AddRange(_oldPoints.Select(p => p.Clone())); }

    /// <summary>Finds the live object for the point this command inserted, e.g. to re-select it after Execute/Redo.</summary>
    public CurvePoint? ResolveInsertedPoint() => _curve.Points.FirstOrDefault(p => p.Id == _insertedPointId);
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