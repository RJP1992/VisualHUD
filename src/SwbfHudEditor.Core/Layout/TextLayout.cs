using SwbfHudEditor.Core.Assets;

namespace SwbfHudEditor.Core.Layout;

/// <summary><c>sTextBreak</c> (P <c>00A91A00</c>), set into bits 4 and 5 by <c>RedTextElement::SetTextBreak</c>.</summary>
public enum TextBreak : byte
{
    /// <summary>Lines end only at a line break.</summary>
    None = 0,
    /// <summary>A line that would pass the box's right edge breaks at its last space, else mid-word.</summary>
    Word = 1,
    /// <summary>Despite the name: no wrapping, but each run of spaces, tabs and line breaks counts as one space.</summary>
    NoReformat = 2,
    /// <summary>Both: wrapping, and runs of white space as one space.</summary>
    WordReformat = 3,
}

/// <summary>How a text element is set up: the <c>RedTextElement</c> fields its layout reads.</summary>
public sealed record TextLayoutOptions
{
    /// <summary><c>TextBox</c> in pixels; 0 for none.</summary>
    public int BoxWidth { get; init; }
    public int BoxHeight { get; init; }
    /// <summary><c>TextScale</c>.</summary>
    public float ScaleX { get; init; } = 1f;
    public float ScaleY { get; init; } = 1f;
    /// <summary><c>TextCharacterSpacing</c>, added after every character.</summary>
    public int Spacing { get; init; } = 1;
    /// <summary><c>TextTabSpacing</c>; 0 for eight spaces.</summary>
    public int TabSpacing { get; init; }
    public TextHAlign HAlign { get; init; }
    public TextVAlign VAlign { get; init; }
    public TextBreak Break { get; init; }
    /// <summary>Extra pixels between lines; not set from a <c>.hud</c> file.</summary>
    public int Leading { get; init; }
}

/// <summary>A character as drawn: its glyph's cell at (X, Y) in the element's own pixels, scaled.</summary>
public readonly record struct PlacedGlyph(char Char, float X, float Y, float ScaleX, float ScaleY, FontGlyph Glyph);

/// <summary>A line as the layout broke it: the text from <c>Start</c> to <c>End</c>, its width, and where it is drawn.</summary>
public readonly record struct TextLine(int Start, int End, int Width, int X, int Y);

public sealed record TextLayoutResult(IReadOnlyList<PlacedGlyph> Glyphs, IReadOnlyList<TextLine> Lines, int LineHeight);

/// <summary>
/// Lays text out as the game does: <c>RedTextElement::ClipString</c> (P <c>008C55B0</c>) breaks it into
/// lines, then <c>RedTextElement::WriteCache</c> (P <c>008C6A20</c>) places each line in the box by its
/// alignment and each character after the last, clipping at the box's right edge. Widths come from
/// the font, in whole pixels, as the game rounds them.
/// </summary>
public static class TextLayout
{
    /// <summary>The line table's size when the text has no box height.</summary>
    public const int MaxLines = 80;

    public static TextLayoutResult Lay(string text, ITextFont font, TextLayoutOptions o)
    {
        // GetTextHeight (P 008C62F0): the font's height scaled and rounded, plus the leading.
        var lineHeight = (int)(font.Height * o.ScaleY + 0.5f) + o.Leading;
        var space = font.Find(' ');
        var tab = TabSpacing(o, space);
        // GetMaxLines (P 008C5F90): as many lines as the box is tall, at least one.
        var maxLines = o.BoxHeight == 0 ? MaxLines
            : lineHeight > 0 && lineHeight < o.BoxHeight ? o.BoxHeight / lineHeight : 1;

        var (lines, count, first) = ClipString(text, font, o, lineHeight, tab, space, maxLines);
        return WriteCache(text, font, o, lineHeight, tab, lines, count, first, maxLines);
    }

    /// <summary><c>GetTabSpacing</c> (P <c>008C6210</c>): <c>TextTabSpacing</c>, else eight spaces with their spacing.</summary>
    private static int TabSpacing(TextLayoutOptions o, FontGlyph? space) =>
        o.TabSpacing != 0 ? o.TabSpacing
        : space is { } s ? ((int)(s.Width * o.ScaleX + 0.5f) + o.Spacing) * 8
        : 0;

    private sealed class Line
    {
        public int Start = -1;   // -1: not started (a null pcStart)
        public int End;
        public int Width;
    }

    /// <summary>
    /// The line breaking, as <c>ClipString</c> walks the text: a line ends at a line break, or, when
    /// wrapping, before the character that would pass the box's right edge, at the last run of white
    /// space if the line has one. Past the last line the box holds, a bottom-aligned text keeps going
    /// round the table so its last lines stay; any other stops.
    /// </summary>
    private static (Line[] Lines, int Count, int First) ClipString(string text, ITextFont font, TextLayoutOptions o,
        int lineHeight, int tab, FontGlyph? space, int maxLines)
    {
        var wrap = o.Break is TextBreak.Word or TextBreak.WordReformat;
        var collapse = o.Break is TextBreak.NoReformat or TextBreak.WordReformat;
        var keepsLast = o.VAlign == TextVAlign.Bottom;

        var lines = new Line[maxLines];
        for (var i = 0; i < maxLines; i++) lines[i] = new Line();
        var cur = 0;
        var count = 0;
        lines[0].Start = 0;
        var lineWidth = 0;
        var breakAt = -1;          // where the last run of white space began
        var breakWidth = 0;
        var wordStart = 0;         // where the word after it began, and the next line begins
        var wordStartWidth = 0;
        var done = false;
        var prevWhite = collapse;  // with Reformat, white space before the first word is dropped

        for (var pos = 0; ; pos++)
        {
            if (pos == text.Length)
            {
                if (lines[cur].Start >= 0 && !done)
                {
                    lines[cur].End = pos;
                    lines[cur].Width = lineWidth;
                    count++;
                    return (lines, count, count % maxLines);
                }
                return (lines, count, cur);
            }

            var c = text[pos];
            int glyphWidth = 0, advance = 0;
            bool newline = false, white = false;
            if (c == '\n')
            {
                newline = true;
                white = true;
            }
            else if (c == '\t')
            {
                white = true;
                advance = tab == 0 ? 0 : tab - lineWidth % tab;
                glyphWidth = advance;
            }
            else if (font.Find(c) is { } g)
            {
                glyphWidth = (int)(g.Width * o.ScaleX + 0.5f);
                advance = o.Spacing + glyphWidth;
                white = !g.Printing;
            }

            if (collapse && white)
            {
                // Only the first of a run of white space counts, and as a space.
                if (prevWhite) continue;
                glyphWidth = space is { } s ? (int)(s.Width * o.ScaleX + 0.5f) : lineHeight >> 1;
                advance = o.Spacing + glyphWidth;
            }
            if (done) continue;

            if (white)
            {
                if (!prevWhite)
                {
                    breakAt = pos;
                    breakWidth = lineWidth;
                }
            }
            else if (prevWhite)
            {
                if (lines[cur].Start < 0)
                {
                    lines[cur].Start = pos;
                    lineWidth = 0;
                }
                wordStart = pos;
                wordStartWidth = lineWidth;
            }
            var lastBreak = breakAt;
            prevWhite = white;

            if (newline)
            {
                var line = lines[cur];
                line.End = pos;
                if (line.Start < 0)
                {
                    line.Start = pos;
                    line.Width = 0;
                }
                else line.Width = lineWidth;
                wordStart = pos + 1;
                lineWidth = 0;
                count++;
                cur = count % maxLines;
                if (count == maxLines && !keepsLast) done = true;
                else lines[cur] = new Line { Start = wordStart };
                continue;
            }

            if (wrap && o.BoxWidth != 0 && o.BoxWidth < lineWidth + glyphWidth)
            {
                var line = lines[cur];
                if (breakAt < 0)
                {
                    // No white space on the line: it breaks before this character.
                    line.End = pos;
                    line.Width = lineWidth;
                    wordStart = pos;
                    lineWidth = 0;
                }
                else
                {
                    line.End = breakAt;
                    line.Width = breakWidth;
                    breakAt = -1;
                    if (wordStart <= lastBreak)
                    {
                        // Still in the white space: the next line starts at the next word.
                        wordStart = -1;
                        lineWidth = 0;
                    }
                    else lineWidth -= wordStartWidth;
                }
                count++;
                cur = count % maxLines;
                if (count == maxLines && !keepsLast)
                {
                    done = true;
                    continue;
                }
                lines[cur] = new Line { Start = wordStart };
            }
            lineWidth += advance;
        }
    }

    /// <summary>
    /// The placing, as <c>WriteCache</c> does it: the first line by the vertical alignment, each line
    /// by the horizontal one, characters from there, a line stopping at the first printing character
    /// that would pass the box's right edge.
    /// </summary>
    private static TextLayoutResult WriteCache(string text, ITextFont font, TextLayoutOptions o, int lineHeight, int tab,
        Line[] all, int count, int first, int maxLines)
    {
        var numLines = Math.Min(count, maxLines);
        if (count < maxLines) first = 0;
        var y = o.VAlign switch
        {
            TextVAlign.Bottom => o.BoxHeight - numLines * lineHeight,
            TextVAlign.Center => (o.BoxHeight - numLines * lineHeight) / 2,
            TextVAlign.Baseline => -(int)(font.Ascent * o.ScaleY + 0.5f),
            _ => 0,
        };

        var glyphs = new List<PlacedGlyph>();
        var lines = new List<TextLine>(numLines);
        for (var i = 0; i < numLines; i++)
        {
            var line = all[(i + first) % maxLines];
            var x0 = o.HAlign switch
            {
                TextHAlign.Center => (o.BoxWidth - line.Width) / 2,
                TextHAlign.Right => o.BoxWidth - line.Width,
                _ => 0,
            };
            var start = line.Start < 0 ? line.End : line.Start;
            lines.Add(new TextLine(start, line.End, line.Width, x0, y));
            var x = x0;
            for (var p = start; p < line.End; p++)
            {
                var c = text[p];
                if (c == '\t')
                {
                    x += tab == 0 ? 0 : tab - (x - x0) % tab;
                    continue;
                }
                if (font.Find(c) is not { } g) continue;
                // A glyph much taller than the line is shrunk to fit.
                var fit = Math.Min(lineHeight / (float)(g.Bottom - g.Top) * 1.2f, 1f);
                var sx = fit * o.ScaleX;
                var sy = fit * o.ScaleY;
                var width = (int)(g.Width * sx + 0.5f);
                if (g.Printing)
                {
                    if (o.BoxWidth != 0 && o.BoxWidth < width + x) break;
                    glyphs.Add(new PlacedGlyph(c, x, y, sx, sy, g));
                }
                x += o.Spacing + width;
            }
            y += lineHeight;
        }
        return new TextLayoutResult(glyphs, lines, lineHeight);
    }
}
