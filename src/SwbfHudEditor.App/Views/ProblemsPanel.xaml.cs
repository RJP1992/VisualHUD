using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Problems;

namespace SwbfHudEditor.App.Views;

/// <summary>The block a problem is in, as the Problems tab heads its problems: its name and its file's.</summary>
public sealed record ProblemGroup(HudBlock Block, string Title, string File);

/// <summary>One problem in the Problems tab.</summary>
public sealed class ProblemRow
{
    public required HudProblem Problem { get; init; }
    public required ProblemGroup Group { get; init; }

    public string Message => Problem.Message;
    public string? Line => Problem.Line is { } line ? HudWriter.Header(line) : null;
    public Visibility LineVisibility => Problem.Line is null ? Visibility.Collapsed : Visibility.Visible;

    public string Icon => Problem.Level switch
    {
        ProblemLevel.Error => "",
        ProblemLevel.Warning => "",
        _ => "",
    };

    public Brush IconBrush => (Brush)Application.Current.FindResource(Problem.Level switch
    {
        ProblemLevel.Error => "ErrorBrush",
        ProblemLevel.Warning => "WarningBrush",
        _ => "TextMutedBrush",
    });

    public string LevelName => Problem.Level switch
    {
        ProblemLevel.Error => "Error: the game makes nothing of it, or drops the file",
        ProblemLevel.Warning => "Warning: likely not what was meant",
        _ => "Note: the game's own way, worth knowing",
    };
}

/// <summary>
/// What the game reads otherwise than the files seem to say, under the elements they are in: errors and
/// warnings, and the notes when asked for. A click shows where; a double-click or Enter, the line.
/// </summary>
public partial class ProblemsPanel : UserControl
{
    private IReadOnlyList<ProblemRow>? _rows;
    private bool _showing;

    public event EventHandler<HudProblem>? ProblemPicked;
    public event EventHandler<HudProblem>? ProblemOpened;

    public ProblemsPanel()
    {
        InitializeComponent();
    }

    /// <summary>The rows shown now, notes left out unless asked for.</summary>
    internal IReadOnlyList<ProblemRow> Shown { get; private set; } = Array.Empty<ProblemRow>();

    /// <summary>Show the problems of the files opened, or none when no HUD is open (null).</summary>
    public void Show(IReadOnlyList<ProblemRow>? rows)
    {
        _rows = rows;
        Fill();
    }

    private void Fill()
    {
        var picked = (ProblemList.SelectedItem as ProblemRow)?.Problem;
        if (_rows is null)
        {
            SummaryText.Text = "Open a HUD to check it.";
            Shown = Array.Empty<ProblemRow>();
            ProblemList.ItemsSource = null;
            return;
        }
        var notes = _rows.Count(r => r.Problem.Level == ProblemLevel.Note);
        Shown = NotesBox.IsChecked == true ? _rows : _rows.Where(r => r.Problem.Level != ProblemLevel.Note).ToList();
        SummaryText.Text = Summary(_rows.Count(r => r.Problem.Level == ProblemLevel.Error),
            _rows.Count(r => r.Problem.Level == ProblemLevel.Warning), notes, NotesBox.IsChecked == true);

        _showing = true;
        try
        {
            var view = new ListCollectionView(Shown.ToList());
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProblemRow.Group)));
            ProblemList.ItemsSource = view;
            if (picked is not null)
                ProblemList.SelectedItem = Shown.FirstOrDefault(r => ReferenceEquals(r.Problem.Block, picked.Block) &&
                                                                     ReferenceEquals(r.Problem.Line, picked.Line) && r.Problem.Message == picked.Message);
        }
        finally { _showing = false; }
    }

    private static string Counted(int count, string word) => $"{count} {word}{(count == 1 ? "" : "s")}";

    private static string Summary(int errors, int warnings, int notes, bool showingNotes)
    {
        if (errors + warnings + notes == 0) return "Nothing to point out in the files opened.";
        var text = errors + warnings == 0 ? "No errors or warnings."
            : $"{Counted(errors, "error")}, {Counted(warnings, "warning")}.";
        if (notes > 0) text += showingNotes ? $" {Counted(notes, "note")}." : $" {Counted(notes, "note")} hidden.";
        return text;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_rows is not null) Fill();
    }

    private void ProblemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && ProblemList.SelectedItem is ProblemRow row) ProblemPicked?.Invoke(this, row.Problem);
    }

    private void ProblemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ProblemList, source) is ListBoxItem { DataContext: ProblemRow row })
            ProblemOpened?.Invoke(this, row.Problem);
    }

    private void ProblemList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ProblemList.SelectedItem is not ProblemRow row) return;
        ProblemOpened?.Invoke(this, row.Problem);
        e.Handled = true;
    }
}
