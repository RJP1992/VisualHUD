using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Edits;

/// <summary>Replace a single argument on a HudBlock (typically a property leaf). Covers property-panel edits.</summary>
public sealed class ReplaceArgumentEdit : IHudEdit
{
    private readonly HudBlock _block;
    private readonly int _index;
    private readonly HudArgument _oldArg;
    private readonly HudArgument _newArg;
    private readonly HudBlock? _affected;

    public ReplaceArgumentEdit(HudBlock block, int index, HudArgument oldArg, HudArgument newArg, HudBlock? affected = null)
    {
        _block = block;
        _index = index;
        _oldArg = oldArg;
        _newArg = newArg;
        _affected = affected ?? block;
    }

    public HudBlock? AffectedBlock => _affected;
    public string Description => $"edit {_block.Name} arg {_index}";

    public void Apply() => _block.Arguments[_index] = _newArg;
    public void Revert() => _block.Arguments[_index] = _oldArg;
}
