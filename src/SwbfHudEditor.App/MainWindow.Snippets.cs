using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App;

/// <summary>
/// Blocks saved under a name, to insert into any HUD later: each a <c>.hud</c> file in the Snippets folder
/// beside the settings, inserted as a paste is, inside the selection where it fits, renamed where its names
/// are taken.
/// </summary>
public partial class MainWindow
{
    public static readonly RoutedCommand SaveSnippetCommand = new("SaveSnippet", typeof(MainWindow));

    /// <summary>Where the saved blocks are kept: beside the settings, so the tests keep theirs apart.</summary>
    internal static string SnippetFolder => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(AppSettings.FilePath))!, "Snippets");

    private void SetUpSnippets()
    {
        CommandBindings.Add(new CommandBinding(SaveSnippetCommand, (_, _) => AskSaveSnippet(), (_, e) =>
            e.CanExecute = !TypingInBox && TopSelectedBlocks().Any(b => b.HasBody)));
        // In the Edit menu, the tree's menu and the toolbar's Insert: save the selection, insert a saved one.
        foreach (var (anchor, items) in new[]
                 {
                     (EditInsertAfter, ((ItemsControl)EditInsertAfter.Parent).Items),
                     (TreeInsertAfter, ((ItemsControl)TreeInsertAfter.Parent).Items),
                 })
        {
            var at = items.IndexOf(anchor) + 1;
            items.Insert(at, SnippetMenu());
            items.Insert(at + 1, new MenuItem
            {
                Header = "Save as snippet...",
                Command = SaveSnippetCommand,
                ToolTip = "Keep the selected blocks under a name, to insert into any HUD later from Insert saved.",
            });
        }
        ToolbarInsertMenu.Items.Add(new Separator());
        ToolbarInsertMenu.Items.Add(SnippetMenu());
    }

    /// <summary>A menu of the saved blocks, read each time it opens.</summary>
    private MenuItem SnippetMenu()
    {
        var menu = new MenuItem { Header = "Insert saved", ToolTip = "Blocks saved with Save as snippet, inserted as a paste is." };
        menu.Items.Add(new MenuItem { Header = "(none saved yet)", IsEnabled = false });
        menu.SubmenuOpened += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, menu)) return;
            menu.Items.Clear();
            var names = SnippetNames();
            if (names.Count == 0) menu.Items.Add(new MenuItem { Header = "(none saved yet)", IsEnabled = false });
            foreach (var name in names)
            {
                var item = new MenuItem { Header = name.Replace("_", "__"), ToolTip = SnippetPreview(name) };
                item.Click += (_, _) => InsertSnippet(name);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var open = new MenuItem { Header = "Open the folder", ToolTip = SnippetFolder };
            open.Click += (_, _) =>
            {
                Directory.CreateDirectory(SnippetFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SnippetFolder}\"") { UseShellExecute = true });
            };
            menu.Items.Add(open);
        };
        return menu;
    }

    /// <summary>The names of the saved blocks, in order.</summary>
    internal static List<string> SnippetNames() =>
        Directory.Exists(SnippetFolder)
            ? Directory.GetFiles(SnippetFolder, "*.hud").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();

    private static string SnippetPreview(string name)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(SnippetFolder, name + ".hud"));
            var lines = text.Split('\n').Take(12).Select(l => l.TrimEnd('\r'));
            return string.Join("\n", lines) + (text.Count(c => c == '\n') > 12 ? "\n..." : string.Empty);
        }
        catch (IOException) { return name; }
    }

    private void AskSaveSnippet()
    {
        var blocks = TopSelectedBlocks().Where(b => b.HasBody).ToList();
        if (blocks.Count == 0) return;
        var dialog = new TextPromptDialog("Save as snippet", "A name for the selected blocks, to insert them into any HUD later:",
            blocks[0].ItemName.Length > 0 ? blocks[0].ItemName : blocks[0].Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var name = dialog.Text.Trim();
        if (File.Exists(Path.Combine(SnippetFolder, name + ".hud")) &&
            MessageBox.Show(this, $"A snippet named {name} is saved already. Replace it?", "Save as snippet",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SaveSnippet(name);
    }

    /// <summary>Save the selected blocks under a name; the file, or null when the name cannot be a file's.</summary>
    internal string? SaveSnippet(string name)
    {
        var blocks = TopSelectedBlocks().Where(b => b.HasBody).ToList();
        if (blocks.Count == 0) return null;
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusText.Text = "A snippet's name must be one a file can have.";
            return null;
        }
        var path = Path.Combine(SnippetFolder, name + ".hud");
        try
        {
            Directory.CreateDirectory(SnippetFolder);
            File.WriteAllText(path, HudClipboard.Write(blocks));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not save the snippet: {ex.Message}";
            return null;
        }
        StatusText.Text = $"Saved {Describe(blocks.Cast<object>().ToList())} as the snippet {name}: Insert saved puts it into any HUD.";
        return path;
    }

    /// <summary>Insert a saved snippet as a paste is: inside the selection where it fits, else after it.</summary>
    internal bool InsertSnippet(string name)
    {
        string text;
        try { text = File.ReadAllText(Path.Combine(SnippetFolder, name + ".hud")); }
        catch (IOException)
        {
            StatusText.Text = $"The snippet {name} is gone.";
            return false;
        }
        return Paste(text);
    }
}
