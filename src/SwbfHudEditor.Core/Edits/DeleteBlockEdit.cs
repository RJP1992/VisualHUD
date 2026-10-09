using System.Collections.ObjectModel;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Edits;

/// <summary>Remove a block from its container. Apply removes, Revert re-inserts at the original index.</summary>
public sealed class DeleteBlockEdit : IHudEdit
{
    private readonly ObservableCollection<HudBlock> _container;
    private readonly int _index;
    private readonly HudBlock _block;
    private readonly HudBlock? _affected;

    public DeleteBlockEdit(ObservableCollection<HudBlock> container, int index, HudBlock block, HudBlock? affected)
    {
        _container = container;
        _index = index;
        _block = block;
        _affected = affected;
    }

    public HudBlock? AffectedBlock => _affected;
    public string Description => $"delete {_block.Name}";

    public void Apply() => _container.RemoveAt(_index);
    public void Revert() => _container.Insert(_index, _block);
}
