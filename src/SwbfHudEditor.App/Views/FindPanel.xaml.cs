using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Views;

/// <summary>The element a match is in, as the Find tab heads its matches: its name and its file's.</summary>
public sealed record FindGroup(HudBlock? Element, string Title, string File);

/// <summary>One match in the Find tab: where it is, what it becomes, and whether it is to be replaced.</summary>
public sealed class FindRow : INotifyPropertyChanged
{
    public required FindMatch Match { get; init; }
    public required FindGroup Group { get; init; }

    /// <summary>The line as written, with the text found in its argument marked.</summary>
    public required IReadOnlyList<TextSegment> Segments { get; init; }

    /// <summary>The argument as the replacement would write it, or why it stays.</summary>
    public required string After { get; init; }

    /// <summary>Replacing changes it: it is not refused, nor the same.</summary>
    public required bool Changes { get; init; }

    /// <summary>The replacement could not be written there, as a number that is not one.</summary>
    public bool Refused { get; init; }

    public Brush AfterBrush => (Brush)Application.Current.FindResource(Refused ? "WarningBrush" : Changes ? "TextBrush" : "TextMutedBrush");

    /// <summary>Worked out when the tooltip first shows: the element's path takes a search.</summary>
    public required Func<string> DetailsOf { get; init; }
    private string? _details;
    public string Details => _details ??= DetailsOf();

    private bool _include = true;
    /// <summary>Ticked: replaced with the rest.</summary>
    public bool Include
    {
        get => _include;
        set
        {
            if (_include == value) return;
            _include = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Include)));
            IncludeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? IncludeChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A line as the writer puts it, the found text marked in one of its arguments.</summary>
    public static IReadOnlyList<TextSegment> Marked(HudBlock line, int index, FindQuery query)
    {
        var segments = new List<TextSegment>();
        var text = new StringBuilder(line.Name).Append('(');
        for (var i = 0; i < line.Arguments.Count; i++)
        {
            if (i > 0) text.Append(", ");
            var arg = line.Arguments[i];
            var quote = arg.Kind == HudArgKind.String ? "\"" : string.Empty;
            text.Append(quote);
            if (i == index)
            {
                segments.Add(new TextSegment(text.ToString(), false));
                text.Clear();
                Mark(arg.Raw, query, segments);
            }
            else text.Append(arg.Raw);
            text.Append(quote);
        }
        segments.Add(new TextSegment(text.Append(')').ToString(), false));
        return segments;
    }

    private static void Mark(string value, FindQuery query, List<TextSegment> segments)
    {
        if (query.WholeValue || query.Text.Length == 0)
        {
            segments.Add(new TextSegment(value, query.Text.Length > 0));
            return;
        }
        var at = 0;
        for (var hit = value.IndexOf(query.Text, query.Comparison); hit >= 0; hit = value.IndexOf(query.Text, hit + query.Text.Length, query.Comparison))
        {
            if (hit > at) segments.Add(new TextSegment(value[at..hit], false));
            segments.Add(new TextSegment(value.Substring(hit, query.Text.Length), true));
            at = hit + query.Text.Length;
        }
        if (at < value.Length) segments.Add(new TextSegment(value[at..], false));
    }
}

/// <summary>
/// Find text in the arguments of the open files and replace it: the matches listed with what each becomes,
/// each ticked to be replaced until unticked, and a click on one shows its element.
/// </summary>
public partial class FindPanel : UserControl
{
    // The matches unticked, kept as the list is filled again, until the text to find or how changes.
    private readonly HashSet<(HudBlock Line, int Index)> _excluded = new();
    private IReadOnlyList<FindRow> _rows = Array.Empty<FindRow>();
    private bool _showing;

    /// <summary>The text to find, or how, or the replacement changed: the list wants filling again.</summary>
    public event EventHandler? QueryChanged;

    /// <summary>A match picked in the list.</summary>
    public event EventHandler<FindMatch>? MatchPicked;

    /// <summary>A match opened with a double-click or Enter: its argument to edit.</summary>
    public event EventHandler<FindMatch>? MatchOpened;

    public event EventHandler? ReplaceRequested;

    public FindPanel()
    {
        InitializeComponent();
    }

    /// <summary>What to find, or null with nothing typed.</summary>
    public FindQuery? Query => FindBox.Text.Length == 0
        ? null
        : new FindQuery(FindBox.Text, MatchCaseBox.IsChecked == true, WholeValueBox.IsChecked == true, AllValuesBox.IsChecked != true);

    public string Replacement => ReplaceBox.Text;

    /// <summary>Whether a match is ticked to be replaced.</summary>
    public bool Includes(FindMatch match) => !_excluded.Contains((match.Line, match.Index));

    /// <summary>The rows shown now.</summary>
    internal IReadOnlyList<FindRow> Rows => _rows;

    /// <summary>Show the matches; the one picked stays picked if it is still found.</summary>
    public void Show(IReadOnlyList<FindRow> rows, string summary)
    {
        foreach (var row in _rows) row.IncludeChanged -= Row_IncludeChanged;
        foreach (var row in rows)
        {
            row.Include = Includes(row.Match);
            row.IncludeChanged += Row_IncludeChanged;
        }
        var picked = (MatchList.SelectedItem as FindRow)?.Match;
        _rows = rows;
        _showing = true;
        try
        {
            var view = new System.Windows.Data.ListCollectionView(rows.ToList());
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(FindRow.Group)));
            MatchList.ItemsSource = view;
            if (picked is not null)
                MatchList.SelectedItem = rows.FirstOrDefault(r => ReferenceEquals(r.Match.Line, picked.Line) && r.Match.Index == picked.Index);
        }
        finally { _showing = false; }
        SummaryText.Text = summary;
        UpdateButton();
    }

    public void FocusFind()
    {
        FindBox.Focus();
        FindBox.SelectAll();
    }

    public void FocusReplace()
    {
        ReplaceBox.Focus();
        ReplaceBox.SelectAll();
    }

    /// <summary>Put text in the box to find, as a find started on text picked in a box does.</summary>
    public void SetFind(string text) => FindBox.Text = text;

    /// <summary>Put text in the box to replace with.</summary>
    public void SetReplacement(string text) => ReplaceBox.Text = text;

    private void UpdateButton()
    {
        var count = _rows.Count(r => r.Include && r.Changes);
        ReplaceButton.Content = count == 0 ? "Replace" : $"Replace {count}";
        ReplaceButton.IsEnabled = count > 0;
    }

    private void Row_IncludeChanged(object? sender, EventArgs e)
    {
        if (sender is not FindRow row) return;
        if (row.Include) _excluded.Remove((row.Match.Line, row.Match.Index));
        else _excluded.Add((row.Match.Line, row.Match.Index));
        UpdateButton();
    }

    private void Query_Changed(object sender, RoutedEventArgs e)
    {
        _excluded.Clear();
        FindHint.Visibility = FindBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Replacement_Changed(object sender, TextChangedEventArgs e)
    {
        ReplaceHint.Visibility = ReplaceBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && MatchList.SelectedItem is FindRow row) MatchPicked?.Invoke(this, row.Match);
    }

    private void MatchList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(MatchList, source) is ListBoxItem { DataContext: FindRow row })
            MatchOpened?.Invoke(this, row.Match);
    }

    /// <summary>Enter in the box to find picks the first match; Down goes to the list.</summary>
    private void FindBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Down) || _rows.Count == 0) return;
        if (MatchList.SelectedItem is null) MatchList.SelectedItem = _rows[0];
        if (e.Key == Key.Down && MatchList.ItemContainerGenerator.ContainerFromItem(MatchList.SelectedItem) is ListBoxItem item) item.Focus();
        e.Handled = true;
    }

    private void MatchList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || MatchList.SelectedItem is not FindRow row) return;
        MatchOpened?.Invoke(this, row.Match);
        e.Handled = true;
    }

    private void Replace_Click(object sender, RoutedEventArgs e) => ReplaceRequested?.Invoke(this, EventArgs.Empty);
}
