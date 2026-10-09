using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App;

/// <summary>
/// One <c>.hud</c> file of the set the editor has open. The game loads a HUD's files additively into
/// the same screen groups, each laid out by its own <c>FileInfo</c>, so the editor keeps them as one set
/// in load order and saves each on its own.
/// </summary>
public sealed class HudFile : INotifyPropertyChanged
{
    public HudFile(HudDocument document, string? filePath, bool addedAutomatically = false)
    {
        Document = document;
        _filePath = filePath;
        _addedAutomatically = addedAutomatically;
        _savedText = HudWriter.Write(document);
        _note = ReadNote();
    }

    public HudDocument Document { get; }

    private string? _filePath;
    public string? FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            Document.SourcePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string Name => Path.GetFileName(_filePath) ?? "(unsaved)";

    private bool _addedAutomatically;
    /// <summary>A transform file the set loads because its HUD needs it, not one the user opened.</summary>
    public bool AddedAutomatically
    {
        get => _addedAutomatically;
        set
        {
            if (_addedAutomatically == value) return;
            _addedAutomatically = value;
            OnPropertyChanged();
            Refresh();
        }
    }

    // The document as last loaded or saved, written out: a document that writes differently has edits
    // to save, and undoing back to it leaves none.
    private string _savedText;
    private bool _isModified;
    public bool IsModified
    {
        get => _isModified;
        private set
        {
            if (_isModified == value) return;
            _isModified = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>Re-check for unsaved edits and what the file's <c>FileInfo</c> asks for.</summary>
    public void Refresh()
    {
        IsModified = HudWriter.Write(Document) != _savedText;
        var note = ReadNote();
        if (note == _note) return;
        _note = note;
        OnPropertyChanged(nameof(Note));
    }

    public void MarkSaved()
    {
        _savedText = HudWriter.Write(Document);
        IsModified = false;
    }

    private bool _isVisible = true;
    /// <summary>Editor-only: draw this file's elements on the canvas.</summary>
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

    public string DisplayName => Name + (IsModified ? " *" : string.Empty);

    private string? _note;
    /// <summary>The tree's note on the file: how GameExt lays it out, or why the set loads it.</summary>
    public string? Note => _note;

    private string? ReadNote()
    {
        if (_addedAutomatically) return "transforms, loaded with the set";
        return HudFileLayout.For(Document, new LayoutOptions()).Ratio is { } ratio
            ? $"GameExt layout {HudCanvas.RatioName(ratio)}"
            : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => Name;
}

/// <summary>Loading the files of a HUD as the game loads them.</summary>
internal static class HudFileSet
{
    public static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// The transform files a HUD made of <paramref name="hudPaths"/> loads with, in load order: each
    /// folder's own <c>hudtransforms.hud</c> (a mod's replaces the stock one, so the stock one loads
    /// only when no folder has its own), then the folder's other <c>hudtransforms*.hud</c>, such as
    /// GameExt's. The set's own files are not repeated.
    /// </summary>
    public static IReadOnlyList<string> TransformFilesFor(IReadOnlyList<string> hudPaths)
    {
        if (hudPaths.Count == 0) return Array.Empty<string>();
        var own = hudPaths.Select(Path.GetFullPath).ToList();
        var folders = own.Select(Path.GetDirectoryName).OfType<string>().Distinct(PathComparer).ToList();
        var result = new List<string>();
        void Add(string path)
        {
            var full = Path.GetFullPath(path);
            if (!result.Contains(full, PathComparer) && !own.Contains(full, PathComparer)) result.Add(full);
        }

        var stock = Path.Combine(MainWindow.DefaultHudFolder, "hudtransforms.hud");
        if (!folders.Any(f => File.Exists(Path.Combine(f, "hudtransforms.hud"))) && File.Exists(stock)) Add(stock);
        foreach (var folder in folders)
        {
            var main = Path.Combine(folder, "hudtransforms.hud");
            if (File.Exists(main)) Add(main);
            foreach (var extra in Directory.GetFiles(folder, "hudtransforms*.hud")
                         .Where(f => f.EndsWith(".hud", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f, PathComparer))
                Add(extra);
        }
        return result;
    }

    /// <summary>
    /// The documents a HUD made of these files loads, in load order: its transform files, then the
    /// files. A transform file that does not parse is skipped, as it only feeds the preview.
    /// </summary>
    public static IReadOnlyList<HudDocument> Load(IReadOnlyList<string> hudPaths)
    {
        var docs = new List<HudDocument>();
        foreach (var path in TransformFilesFor(hudPaths))
            if (TryParse(path) is { } doc) docs.Add(doc);
        foreach (var path in hudPaths)
            docs.Add(Parse(path));
        return docs;
    }

    public static HudDocument Parse(string path) => HudParser.Parse(File.ReadAllText(path), path);

    public static HudDocument? TryParse(string path)
    {
        try { return Parse(path); }
        catch { return null; }
    }
}
