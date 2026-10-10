using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Edits;

/// <summary>Replace all of a line's arguments at once, their count included: a unit conversion that adds the mode.</summary>
public sealed class ReplaceArgumentsEdit : IHudEdit
{
    private readonly HudBlock _line;
    private readonly HudArgument[] _old;
    private readonly HudArgument[] _new;
    private readonly HudBlock? _affected;

    public ReplaceArgumentsEdit(HudBlock line, IEnumerable<HudArgument> arguments, HudBlock? affected = null)
    {
        _line = line;
        _old = line.Arguments.ToArray();
        _new = arguments.ToArray();
        _affected = affected ?? line;
    }

    public HudBlock? AffectedBlock => _affected;
    public string Description => $"edit {_line.Name}";

    public void Apply() => Set(_new);

    public void Revert() => Set(_old);

    private void Set(HudArgument[] arguments)
    {
        _line.Arguments.Clear();
        _line.Arguments.AddRange(arguments);
        _line.NotifyArgumentsChanged();
    }
}
