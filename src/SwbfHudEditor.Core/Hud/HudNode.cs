using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SwbfHudEditor.Core.Hud;

public sealed class HudBlock : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public List<HudArgument> Arguments { get; } = new();
    public ObservableCollection<HudBlock> Children { get; } = new();
    public bool HasBody { get; set; }

    private bool _isVisible = true;
    /// <summary>
    /// Editor-only visibility flag. Not serialized by HudWriter.
    /// Hiding a block hides its rendered visual and its entire descendant subtree.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public HudBlock() { }

    public HudBlock(string name, params HudArgument[] arguments)
    {
        Name = name;
        Arguments.AddRange(arguments);
    }

    public override string ToString()
    {
        var args = string.Join(", ", Arguments.Select(a => a.Kind == HudArgKind.String ? $"\"{a.Raw}\"" : a.Raw));
        var suffix = HasBody ? " { ... }" : string.Empty;
        return $"{Name}({args}){suffix}";
    }
}

public sealed class HudDocument
{
    public ObservableCollection<HudBlock> TopLevel { get; } = new();
    public string? SourcePath { get; set; }
}
