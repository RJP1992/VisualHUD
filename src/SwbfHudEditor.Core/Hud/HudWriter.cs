using System.Text;

namespace SwbfHudEditor.Core.Hud;

/// <summary>
/// Writes a document as <c>.hud</c> text: one line per statement, a body's braces on lines of their
/// own, and the comments, blank lines, indentation and line endings its file was read with.
/// </summary>
public static class HudWriter
{
    /// <summary>
    /// The document as text, indented with <paramref name="indent"/> or the file's own; lines
    /// <paramref name="omit"/> picks are left out, with their comments. The document is not changed.
    /// </summary>
    public static string Write(HudDocument doc, string? indent = null, Func<HudBlock, bool>? omit = null)
    {
        var writer = new Writer(indent ?? doc.Indent, doc.LineEnding, omit);
        foreach (var block in doc.TopLevel) writer.Block(block, depth: 0);
        writer.Lines(doc.Closing);
        return writer.Finish(doc.FinalLineEnding);
    }

    private sealed class Writer(string indent, string newline, Func<HudBlock, bool>? omit)
    {
        private readonly StringBuilder _sb = new();

        public void Block(HudBlock block, int depth)
        {
            var trivia = block.Trivia;
            var reindent = trivia?.Reindent == true;
            Lines(trivia?.Before, reindent ? Depth(depth) : null);
            var header = Header(block);
            // As written, while the line is as it was read.
            if (trivia?.RawHeader is { } raw && trivia.HeaderAsRead == header) header = raw;
            Line(trivia?.Indent ?? Depth(depth), header + trivia?.After);
            if (!block.HasBody) return;
            Line(trivia?.OpenIndent ?? Depth(depth), "{" + trivia?.AfterOpen);
            foreach (var child in block.Children)
                if (omit?.Invoke(child) != true) Block(child, depth + 1);
            Lines(trivia?.BeforeClose, reindent ? Depth(depth + 1) : null);
            Line(trivia?.CloseIndent ?? Depth(depth), "}" + trivia?.AfterClose);
        }

        /// <summary>
        /// Whole lines kept from the file, written as they were, or at <paramref name="indent"/> for a
        /// block that moved.
        /// </summary>
        public void Lines(List<string>? lines, string? indent = null)
        {
            if (lines is null) return;
            foreach (var line in lines)
            {
                if (indent is not null && line.Trim().Length > 0) Line(indent, line.TrimStart());
                else Line(string.Empty, indent is not null ? string.Empty : line);
            }
        }

        private string Depth(int depth) => depth == 0 ? string.Empty : string.Concat(Enumerable.Repeat(indent, depth));

        private void Line(string lead, string text)
        {
            // A block comment's own line breaks follow the file's.
            _sb.Append(lead).Append(newline == "\n" ? text : text.Replace("\n", newline)).Append(newline);
        }

        public string Finish(string finalLineEnding)
        {
            if (_sb.Length >= newline.Length) _sb.Length -= newline.Length;
            return _sb.Append(finalLineEnding).ToString();
        }
    }

    /// <summary>A block's name and arguments as the writer puts them: <c>Name("text", 1, 2.5)</c>.</summary>
    public static string Header(HudBlock block)
    {
        var sb = new StringBuilder(block.Name).Append('(');
        for (var i = 0; i < block.Arguments.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var arg = block.Arguments[i];
            if (arg.Kind == HudArgKind.String) sb.Append('"').Append(arg.Raw).Append('"');
            else sb.Append(arg.Raw);
        }
        return sb.Append(')').ToString();
    }
}
