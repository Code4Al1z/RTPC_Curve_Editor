namespace RTPCCurveEditor.Commands;

/// <summary>
/// Base interface for all undoable curve editing operations.
/// </summary>
public interface ICurveCommand
{
    void Execute();
    void Undo();
    string Description { get; }
}

/// <summary>
/// Manages the undo/redo stack. All mutations to the curve must go through here.
/// </summary>
public class UndoRedoStack
{
    private readonly Stack<ICurveCommand> _undoStack = new();
    private readonly Stack<ICurveCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? NextUndoDescription => _undoStack.TryPeek(out var c) ? c.Description : null;
    public string? NextRedoDescription => _redoStack.TryPeek(out var c) ? c.Description : null;

    public event Action? StackChanged;

    public void Execute(ICurveCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
        StackChanged?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
        StackChanged?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
        StackChanged?.Invoke();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StackChanged?.Invoke();
    }
}
