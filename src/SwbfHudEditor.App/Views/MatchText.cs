using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SwbfHudEditor.App.Views;

/// <summary>A piece of a line of text, marked when it is what was found.</summary>
public sealed record TextSegment(string Text, bool Marked);

/// <summary>A line of text with what was found in it marked, as a find highlights it.</summary>
public sealed class MatchText : TextBlock
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<TextSegment>), typeof(MatchText),
        new PropertyMetadata(null, (d, _) => ((MatchText)d).Build()));

    private static Brush? _mark;

    public IReadOnlyList<TextSegment>? Segments
    {
        get => (IReadOnlyList<TextSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    // The theme's warning colour, faint, behind the text found: it shows on a picked row too.
    private static Brush Mark()
    {
        if (_mark is not null) return _mark;
        var colour = Application.Current.TryFindResource("WarningBrush") is SolidColorBrush { Color: var c } ? c : Colors.Orange;
        var brush = new SolidColorBrush(Color.FromArgb(0x60, colour.R, colour.G, colour.B));
        brush.Freeze();
        return _mark = brush;
    }

    private void Build()
    {
        Inlines.Clear();
        if (Segments is not { } segments) return;
        foreach (var segment in segments)
            Inlines.Add(segment.Marked ? new Run(segment.Text) { Background = Mark() } : new Run(segment.Text));
    }
}
