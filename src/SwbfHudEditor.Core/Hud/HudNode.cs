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

    /// <summary>The comments and blank lines around the block in its file; null for a block the editor made.</summary>
    public HudTrivia? Trivia { get; set; }

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

    private bool _isMultiSelected;
    /// <summary>Editor-only: one of several rows selected together in the Elements tree. Not serialized.</summary>
    public bool IsMultiSelected
    {
        get => _isMultiSelected;
        set
        {
            if (_isMultiSelected == value) return;
            _isMultiSelected = value;
            OnPropertyChanged();
        }
    }

    private bool _isFilteredOut;
    /// <summary>Editor-only: the Elements tree's filter leaves this row out. Not serialized.</summary>
    public bool IsFilteredOut
    {
        get => _isFilteredOut;
        set
        {
            if (_isFilteredOut == value) return;
            _isFilteredOut = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The block's line as written, <c>Name("text", 1) { ... }</c>.</summary>
    public string Header => ToString();

    /// <summary>The name in the block's first argument, as the Elements tree shows it; empty without one.</summary>
    public string ItemName => Arguments.Count > 0 && Arguments[0].Kind == HudArgKind.String ? Arguments[0].Raw : string.Empty;

    /// <summary>Tell bindings an argument changed, such as a rename: <see cref="Arguments"/> is a plain list.</summary>
    public void NotifyArgumentsChanged()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(ItemName));
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

    /// <summary>The comments and blank lines after the last block, as written.</summary>
    public List<string>? Closing { get; set; }

    /// <summary>One level of indentation, as the file has it: a tab, or its number of spaces.</summary>
    public string Indent { get; set; } = "    ";

    /// <summary>The file's line ending, <c>\r\n</c> or <c>\n</c>.</summary>
    public string LineEnding { get; set; } = "\n";

    /// <summary>What the file's last line ends with: a line ending, or nothing.</summary>
    public string FinalLineEnding { get; set; } = string.Empty;
}

/// <summary>
/// The comments and blank lines around a block, kept so that saving writes them back as they were.
/// A list holds whole lines as written, indentation included, blank or whitespace-only lines too;
/// the strings after a line are its trailing comments with the spaces or tabs before them.
/// </summary>
public sealed class HudTrivia
{
    /// <summary>The indentation of the block's line, of its opening and of its closing brace, as written.</summary>
    public string? Indent { get; set; }
    public string? OpenIndent { get; set; }
    public string? CloseIndent { get; set; }

    /// <summary>
    /// The block's name and arguments as written, such as <c>Color(234, 1 ,1)</c>, when that differs
    /// from how the writer would put them; kept while <see cref="HeaderAsRead"/> is still what the
    /// writer would put, that is until the line is edited.
    /// </summary>
    public string? RawHeader { get; set; }
    public string? HeaderAsRead { get; set; }

    /// <summary>The lines just before the block.</summary>
    public List<string>? Before { get; set; }

    /// <summary>A comment after the block's line, such as <c>Alpha(1) // opacity</c>.</summary>
    public string? After { get; set; }

    /// <summary>A comment after the opening brace.</summary>
    public string? AfterOpen { get; set; }

    /// <summary>The lines just before the closing brace.</summary>
    public List<string>? BeforeClose { get; set; }

    /// <summary>A comment after the closing brace.</summary>
    public string? AfterClose { get; set; }

    /// <summary>
    /// Write the comment lines at the block's depth: the block was moved or pasted somewhere else,
    /// and their own indentation was for where they were.
    /// </summary>
    public bool Reindent { get; set; }

    /// <summary>Whether any comment is attached, not only blank lines.</summary>
    public bool HasComments =>
        Before?.Exists(IsComment) == true || After is not null || AfterOpen is not null ||
        BeforeClose?.Exists(IsComment) == true || AfterClose is not null;

    private static bool IsComment(string line) => line.Trim().Length > 0;
}
