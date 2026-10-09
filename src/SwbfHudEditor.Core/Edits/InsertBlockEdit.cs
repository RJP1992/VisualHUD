using System.Collections.ObjectModel;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Edits;

/// <summary>Insert a block into a parent's Children (or TopLevel). Apply inserts, Revert removes.</summary>
public sealed class InsertBlockEdit : IHudEdit
{
    private readonly ObservableCollection<HudBlock> _container;
    private readonly int _index;
    private readonly HudBlock _block;
    private readonly HudBlock? _affected;
    private readonly string _description;

    public InsertBlockEdit(ObservableCollection<HudBlock> container, int index, HudBlock block, string description, HudBlock? affected = null)
    {
        _container = container;
        _index = index;
        _block = block;
        _description = description;
        _affected = affected ?? block;
    }

    public HudBlock? AffectedBlock => _affected;
    public string Description => _description;

    public void Apply() => _container.Insert(_index, _block);
    public void Revert() => _container.RemoveAt(_index);
}
