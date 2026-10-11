using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SwbfHudEditor.App.Views;

/// <summary>
/// The Markdown the guide is written in as a FlowDocument in the editor's theme: headings, paragraphs, lists two
/// deep, code blocks, rules, and inline code, bold, italics and links. A web link opens in the browser; a link to
/// a file of the repository shows as its text.
/// </summary>
internal static partial class MarkdownFlow
{
    [GeneratedRegex(@"^(#{1,4})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)([-*]|\d+\.)\s+(.*)$")]
    private static partial Regex ItemStart();

    [GeneratedRegex(@"`[^`]+`|\*\*[^*]+\*\*|\*[^*\s][^*]*\*|\[[^\]]+\]\([^)\s]+\)")]
    private static partial Regex InlineToken();

    [GeneratedRegex(@"^\[([^\]]+)\]\(([^)\s]+)\)$")]
    private static partial Regex Link();

    private sealed record Theme(FontFamily Mono, Brush Text, Brush Code, Brush Accent, Brush Surface, Brush Border);

    public static FlowDocument Build(string markdown, FrameworkElement resources)
    {
        Brush B(string key) => (Brush)resources.FindResource(key);
        var theme = new Theme((FontFamily)resources.FindResource("MonoFont"), B("TextBrush"), B("CodeBrush"), B("AccentBrush"),
            B("SurfaceBrush"), B("BorderBrush"));
        var doc = new FlowDocument
        {
            FontFamily = (FontFamily)resources.FindResource("UiFont"),
            FontSize = 14,
            Foreground = theme.Text,
            Background = B("PanelBrush"),
            PagePadding = new Thickness(32, 24, 32, 48),
            TextAlignment = TextAlignment.Left,
            LineHeight = 21,
        };

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++) code.Add(lines[i]);
                i++;
                doc.Blocks.Add(new Paragraph(new Run(string.Join("\n", code)))
                {
                    FontFamily = theme.Mono,
                    FontSize = 13,
                    LineHeight = 18,
                    Background = theme.Surface,
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 2, 0, 12),
                });
                continue;
            }
            if (line.Trim() is "---" or "***")
            {
                doc.Blocks.Add(new Paragraph { BorderBrush = theme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 6, 0, 18), FontSize = 2 });
                i++;
                continue;
            }
            if (Heading().Match(line) is { Success: true } heading)
            {
                var level = heading.Groups[1].Length;
                var p = new Paragraph
                {
                    FontSize = level switch { 1 => 28, 2 => 21, 3 => 16.5, _ => 14.5 },
                    FontWeight = FontWeights.SemiBold,
                    LineHeight = level switch { 1 => 36, 2 => 28, _ => 22 },
                    Margin = new Thickness(0, level switch { 1 => 0, 2 => 22, _ => 14 }, 0, level <= 2 ? 10 : 6),
                };
                AddInlines(p.Inlines, heading.Groups[2].Value, theme);
                doc.Blocks.Add(p);
                i++;
                continue;
            }
            if (ItemStart().Match(line) is { Success: true } item)
            {
                doc.Blocks.Add(ReadList(lines, ref i, item.Groups[1].Value.Length, theme));
                continue;
            }
            // A paragraph: its lines up to a blank one or another block.
            var text = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !StartsBlock(lines[i])) text.Add(lines[i++].Trim());
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 11) };
            AddInlines(paragraph.Inlines, string.Join(" ", text), theme);
            doc.Blocks.Add(paragraph);
        }
        return doc;
    }

    private static bool StartsBlock(string line) =>
        Heading().IsMatch(line) || ItemStart().IsMatch(line) || line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
        line.Trim() is "---" or "***";

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    /// <summary>A list from its first item at <paramref name="indent"/>, with the lists inside its items.</summary>
    private static List ReadList(string[] lines, ref int i, int indent, Theme theme)
    {
        var ordered = char.IsDigit(ItemStart().Match(lines[i]).Groups[2].Value[0]);
        var list = new List
        {
            MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 11),
            Padding = new Thickness(24, 0, 0, 0),
        };
        bool SameKind(Match item) => char.IsDigit(item.Groups[2].Value[0]) == ordered;
        while (i < lines.Length)
        {
            var start = ItemStart().Match(lines[i]);
            if (!start.Success || start.Groups[1].Value.Length < indent ||
                start.Groups[1].Value.Length == indent && !SameKind(start)) break;
            if (start.Groups[1].Value.Length > indent)
            {
                if (list.ListItems.LastListItem is { } last) last.Blocks.Add(ReadList(lines, ref i, start.Groups[1].Value.Length, theme));
                else i++;
                continue;
            }
            // The item's text: its line and the lines that carry on under it.
            var text = new List<string> { start.Groups[3].Value };
            for (i++; i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !StartsBlock(lines[i]) && Indent(lines[i]) > indent; i++)
                text.Add(lines[i].Trim());
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 5) };
            AddInlines(paragraph.Inlines, string.Join(" ", text), theme);
            list.ListItems.Add(new ListItem(paragraph));
            // A blank line between items of one list keeps it going.
            if (i + 1 < lines.Length && string.IsNullOrWhiteSpace(lines[i]) && ItemStart().Match(lines[i + 1]) is { Success: true } next &&
                (next.Groups[1].Value.Length > indent || next.Groups[1].Value.Length == indent && SameKind(next)))
                i++;
        }
        return list;
    }

    private static void AddInlines(InlineCollection inlines, string text, Theme theme)
    {
        var at = 0;
        foreach (Match m in InlineToken().Matches(text))
        {
            if (m.Index > at) inlines.Add(new Run(text[at..m.Index]));
            var token = m.Value;
            if (token.StartsWith('`'))
                inlines.Add(new Run(token[1..^1]) { FontFamily = theme.Mono, Foreground = theme.Code });
            else if (token.StartsWith("**", StringComparison.Ordinal))
            {
                var bold = new Bold();
                AddInlines(bold.Inlines, token[2..^2], theme);
                inlines.Add(bold);
            }
            else if (token.StartsWith('*'))
            {
                var italic = new Italic();
                AddInlines(italic.Inlines, token[1..^1], theme);
                inlines.Add(italic);
            }
            else if (Link().Match(token) is { Success: true } link)
            {
                var url = link.Groups[2].Value;
                if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    var hyperlink = new Hyperlink { NavigateUri = new Uri(url), Foreground = theme.Accent, ToolTip = url };
                    AddInlines(hyperlink.Inlines, link.Groups[1].Value, theme);
                    hyperlink.RequestNavigate += (_, e) =>
                    {
                        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                        e.Handled = true;
                    };
                    inlines.Add(hyperlink);
                }
                else AddInlines(inlines, link.Groups[1].Value, theme);
            }
            at = m.Index + m.Length;
        }
        if (at < text.Length) inlines.Add(new Run(text[at..]));
    }
}
