using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Edits;

/// <summary>Groups several edits into one undoable unit (e.g. drag updates both X and Y of a Position).</summary>
public sealed class CompositeEdit : IHudEdit
{
    private readonly IReadOnlyList<IHudEdit> _edits;
    private readonly string _description;

    public CompositeEdit(IEnumerable<IHudEdit> edits, string description)
    {
        _edits = edits.ToList();
        _description = description;
    }

    public HudBlock? AffectedBlock => _edits.Count > 0 ? _edits[0].AffectedBlock : null;
    public string Description => _description;

    public void Apply()
    {
        foreach (var e in _edits) e.Apply();
    }

    public void Revert()
    {
        for (var i = _edits.Count - 1; i >= 0; i--) _edits[i].Revert();
    }
}
