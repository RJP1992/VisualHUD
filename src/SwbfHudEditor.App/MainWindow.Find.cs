using System.Windows.Controls;
using System.Windows.Input;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App;

/// <summary>Find and replace across the open files' arguments, in the Find tab.</summary>
public partial class MainWindow
{
    private bool _findListStale = true;

    // The files the user opened: the transform files loaded with them, the stock one among them, are left
    // alone unless opened too.
    private IReadOnlyList<HudDocument> SearchedDocuments => UserFiles.Select(f => f.Document).ToList();

    private void SetUpFind()
    {
        FindList.QueryChanged += (_, _) => RefreshFindList();
        FindList.MatchPicked += (_, match) => ShowMatch(match, edit: false);
        FindList.MatchOpened += (_, match) => ShowMatch(match, edit: true);
        FindList.ReplaceRequested += (_, _) => ReplaceAll();
    }

    /// <summary>Ctrl+F and Ctrl+H: the Find tab, with any text picked in another box to find.</summary>
    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var picked = !FindList.IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox { SelectedText: { Length: > 0 } text } &&
                     !text.Contains('\n') ? text : null;
        LeftTabs.SelectedItem = FindTab;
        LeftTabs.UpdateLayout();
        if (picked is not null) FindList.SetFind(picked);
        if (e.Command == ApplicationCommands.Replace) FindList.FocusReplace();
        else FindList.FocusFind();
    }

    /// <summary>The Find tab, finding this text, for the off-screen snapshot.</summary>
    internal void ShowFind(string text, string? replacement)
    {
        LeftTabs.SelectedItem = FindTab;
        FindList.SetFind(text);
        if (replacement is not null) FindList.SetReplacement(replacement);
    }

    /// <summary>Fill the Find tab's list again, now if it shows, else when it next does.</summary>
    private void RefreshFindList()
    {
        _findListStale = true;
        if (!FindTab.IsSelected) return;
        _findListStale = false;
        if (FindList.Query is not { } query)
        {
            FindList.Show(Array.Empty<FindRow>(), "Type text to find it in the open files: element names, events, textures, text keys.");
            return;
        }
        var matches = HudFind.Find(SearchedDocuments, query);
        var replacement = FindList.Replacement;
        var rows = matches.Select(m => RowFor(m, query, replacement)).ToList();
        FindList.Show(rows, FindSummary(matches, rows));
    }

    private FindRow RowFor(FindMatch match, FindQuery query, string replacement)
    {
        var replaced = HudFind.Replaced(match, query, replacement);
        var element = match.Element;
        return new FindRow
        {
            Match = match,
            Group = new FindGroup(element,
                element is null ? "(the top of the file)" : element.ItemName.Length > 0 ? $"{element.Name} \"{element.ItemName}\"" : element.Name,
                FileOf(match.Document)?.Name ?? string.Empty),
            Segments = FindRow.Marked(match.Line, match.Index, query),
            After = replaced is null ? $"stays: {WhyNot(match.Value.Kind)}" : replaced == match.Value ? "(the same)" : $"→ {Written(replaced)}",
            Changes = replaced is not null && replaced != match.Value,
            Refused = replaced is null,
            DetailsOf = () => MatchDetails(match, replaced),
        };
    }

    private static string Written(HudArgument arg) => arg.Kind == HudArgKind.String ? $"\"{arg.Raw}\"" : arg.Raw;

    private static string WhyNot(HudArgKind kind) => kind switch
    {
        HudArgKind.String => "a string cannot hold a quote or a line break",
        HudArgKind.Identifier => "not a bare word",
        _ => "not a number",
    };

    private string MatchDetails(FindMatch match, HudArgument? replaced)
    {
        var path = match.Element is { } element && Locate(element) is { } at
            ? string.Join(" › ", at.Path.Select(b => b.ItemName.Length > 0 ? $"{b.Name} \"{b.ItemName}\"" : b.Name))
            : "the top of the file";
        var after = replaced is null ? $"\nIt stays: {WhyNot(match.Value.Kind)}."
            : replaced != match.Value ? $"\nIt becomes {Written(replaced)}."
            : string.Empty;
        return $"{FileOf(match.Document)?.Name}: {path}\n{HudWriter.Header(match.Line)}, argument {match.Index + 1}{after}";
    }

    private static string Counted(int count, string word) => $"{count} {word}{(count == 1 ? "" : "s")}";

    private static string FindSummary(IReadOnlyList<FindMatch> matches, IReadOnlyList<FindRow> rows)
    {
        if (matches.Count == 0) return "Not found in the files opened.";
        var times = matches.Sum(m => m.Count);
        var files = matches.Select(m => m.Document).Distinct().Count();
        var text = times == matches.Count
            ? $"Found in {Counted(matches.Count, "argument")}, in {Counted(files, "file")}."
            : $"Found {Counted(times, "time")} in {Counted(matches.Count, "argument")}, in {Counted(files, "file")}.";
        var refused = rows.Count(r => r.Refused);
        if (refused > 0) text += $" {refused} would stay as {(refused == 1 ? "it is" : "they are")}.";
        return text;
    }

    /// <summary>Show a match's element, and to edit, its argument's box in the Properties panel.</summary>
    private void ShowMatch(FindMatch match, bool edit)
    {
        var target = match.Element ?? match.Line;
        if (Locate(target) is not { } at) return;
        ClearPicks();
        SelectPath(at.File, at.Path);
        if (!edit) return;
        if (ReferenceEquals(match.Line, target) && match.Index == 0)
        {
            Properties.FocusName();
            return;
        }
        Properties.FlushRows();
        if (Properties.BoxFor(match.Line, match.Index) is not { } box) return;
        box.Focus();
        box.SelectAll();
    }

    /// <summary>Replace the text in every ticked match, in one undo step: how many arguments changed.</summary>
    internal int ReplaceAll()
    {
        if (FindList.Query is not { } query) return 0;
        var matches = HudFind.Find(SearchedDocuments, query).Where(FindList.Includes).ToList();
        var (changes, refused) = HudFind.Changes(matches, query, FindList.Replacement);
        var left = refused.Count == 0 ? string.Empty
            : $" {Counted(refused.Count, "argument")} left as {(refused.Count == 1 ? "it was" : "they were")}: the replacement would not read back there.";
        if (changes.Count == 0)
        {
            StatusText.Text = "Nothing to replace." + left;
            return 0;
        }
        var edit = new ArgumentsEdit(changes, $"replace \"{query.Text}\" in {Counted(changes.Count, "argument")}");
        edit.Apply();
        _knownEvents = null;
        _undo.Push(edit);
        AfterBlocksChanged(Tree.SelectedItem as HudBlock);
        var documentOf = matches.ToDictionary(m => (m.Line, m.Index), m => m.Document);
        var files = changes.Select(c => documentOf[(c.Line, c.Index)]).Distinct().Count();
        StatusText.Text = $"Replaced \"{query.Text}\" in {Counted(changes.Count, "argument")} of {Counted(files, "file")}, in one undo step." + left;
        return changes.Count;
    }
}
