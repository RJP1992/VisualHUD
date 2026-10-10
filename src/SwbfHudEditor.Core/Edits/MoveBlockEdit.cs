using System.Collections.ObjectModel;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Edits;

/// <summary>
/// Move a block within its list or to another one, as a drag in the tree or Move up does. Apply moves
/// it, Revert puts it back. Within one list, <c>toIndex</c> is where it ends up.
/// </summary>
public sealed class MoveBlockEdit : IHudEdit
{
    private readonly ObservableCollection<HudBlock> _from;
    private readonly int _fromIndex;
    private readonly ObservableCollection<HudBlock> _to;
    private readonly int _toIndex;
    private readonly HudBlock _block;

    public MoveBlockEdit(ObservableCollection<HudBlock> from, int fromIndex, ObservableCollection<HudBlock> to, int toIndex, HudBlock block)
    {
        _from = from;
        _fromIndex = fromIndex;
        _to = to;
        _toIndex = toIndex;
        _block = block;
    }

    public HudBlock? AffectedBlock => _block;
    public string Description => $"move {_block.Name} \"{(_block.Arguments.Count > 0 ? _block.Arguments[0].Raw : string.Empty)}\"";

    public void Apply()
    {
        if (ReferenceEquals(_from, _to)) _from.Move(_fromIndex, _toIndex);
        else
        {
            _from.RemoveAt(_fromIndex);
            _to.Insert(_toIndex, _block);
        }
    }

    public void Revert()
    {
        if (ReferenceEquals(_from, _to)) _from.Move(_toIndex, _fromIndex);
        else
        {
            _to.RemoveAt(_toIndex);
            _from.Insert(_fromIndex, _block);
        }
    }
}
