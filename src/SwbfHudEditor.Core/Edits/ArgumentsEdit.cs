using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Edits;

/// <summary>A change of one argument: which line, which argument, from what, to what.</summary>
public readonly record struct ArgumentChange(HudBlock Line, int Index, HudArgument Old, HudArgument New);

/// <summary>
/// Arguments of any lines changed together, as one undo step, which later changes to the same arguments can
/// join while it is the last step: a run of nudges with the arrow keys undoes as one move.
/// </summary>
public sealed class ArgumentsEdit : IHudEdit
{
    private readonly List<(HudBlock Line, int Index, HudArgument Old, HudArgument New)> _changes = new();
    private readonly HudBlock? _affected;

    public ArgumentsEdit(IEnumerable<ArgumentChange> changes, string description, HudBlock? affected = null)
    {
        foreach (var c in changes) _changes.Add((c.Line, c.Index, c.Old, c.New));
        Description = description;
        _affected = affected ?? _changes.FirstOrDefault().Line;
    }

    public HudBlock? AffectedBlock => _affected;
    public string Description { get; }

    /// <summary>Whether these changes touch exactly the arguments this edit holds.</summary>
    public bool Covers(IReadOnlyList<ArgumentChange> changes) =>
        changes.Count == _changes.Count &&
        changes.All(c => _changes.Any(h => ReferenceEquals(h.Line, c.Line) && h.Index == c.Index));

    /// <summary>Take later values for the same arguments, keeping the ones before the first change.</summary>
    public void Extend(IReadOnlyList<ArgumentChange> changes)
    {
        foreach (var c in changes)
        {
            var i = _changes.FindIndex(h => ReferenceEquals(h.Line, c.Line) && h.Index == c.Index);
            if (i >= 0) _changes[i] = _changes[i] with { New = c.New };
        }
    }

    public void Apply()
    {
        foreach (var (line, index, _, value) in _changes) line.Arguments[index] = value;
        foreach (var line in _changes.Select(c => c.Line).Distinct()) line.NotifyArgumentsChanged();
    }

    public void Revert()
    {
        for (var i = _changes.Count - 1; i >= 0; i--)
        {
            var (line, index, old, _) = _changes[i];
            line.Arguments[index] = old;
        }
        foreach (var line in _changes.Select(c => c.Line).Distinct()) line.NotifyArgumentsChanged();
    }
}
