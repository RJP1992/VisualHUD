using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Edits;

public sealed class UndoManager
{
    private readonly LinkedList<IHudEdit> _undo = new();
    private readonly Stack<IHudEdit> _redo = new();
    private readonly int _capacity;
    private IHudEdit? _savedMarker;

    public UndoManager(int capacity = 500) { _capacity = capacity; }

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>True when current stack top differs from the one recorded at last save.</summary>
    public bool IsDirty => !ReferenceEquals(_undo.Count == 0 ? null : _undo.Last!.Value, _savedMarker);

    /// <summary>Called after pushing/popping to re-anchor the "clean" state to the current top.</summary>
    public void MarkSaved()
    {
        _savedMarker = _undo.Count == 0 ? null : _undo.Last!.Value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _savedMarker = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Push a NEW edit. Caller is responsible for having already applied it (or immediately do so).</summary>
    public void Push(IHudEdit edit)
    {
        _undo.AddLast(edit);
        if (_undo.Count > _capacity) _undo.RemoveFirst();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public HudBlock? Undo()
    {
        if (_undo.Count == 0) return null;
        var edit = _undo.Last!.Value;
        _undo.RemoveLast();
        edit.Revert();
        _redo.Push(edit);
        Changed?.Invoke(this, EventArgs.Empty);
        return edit.AffectedBlock;
    }

    public HudBlock? Redo()
    {
        if (_redo.Count == 0) return null;
        var edit = _redo.Pop();
        edit.Apply();
        _undo.AddLast(edit);
        Changed?.Invoke(this, EventArgs.Empty);
        return edit.AffectedBlock;
    }

    public string? PeekUndoDescription() => _undo.Count == 0 ? null : _undo.Last!.Value.Description;
    public string? PeekRedoDescription() => _redo.Count == 0 ? null : _redo.Peek().Description;
}
