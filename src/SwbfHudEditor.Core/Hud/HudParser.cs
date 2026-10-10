namespace SwbfHudEditor.Core.Hud;

public sealed class HudParseException(string message, int line, int column)
    : Exception($"{message} at line {line}, column {column}")
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

/// <summary>
/// Reads a <c>.hud</c> file into blocks, keeping its comments, blank lines, indentation and line
/// endings so that <see cref="HudWriter"/> writes back what was there.
/// </summary>
public static class HudParser
{
    public static HudDocument Parse(string source, string? sourcePath = null)
    {
        var reader = new Reader(HudLexer.Tokenize(source, keepComments: true), source.Split('\n'));
        var doc = new HudDocument
        {
            SourcePath = sourcePath,
            Indent = IndentOf(source) ?? "    ",
            LineEnding = source.Contains("\r\n") ? "\r\n" : "\n",
            FinalLineEnding = source.EndsWith("\r\n") ? "\r\n" : source.EndsWith('\n') ? "\n" : string.Empty,
        };

        while (true)
        {
            var lines = reader.TakeLines();
            if (reader.Peek().Kind == HudTokenKind.EndOfFile)
            {
                doc.Closing = lines;
                break;
            }
            doc.TopLevel.Add(reader.Block(lines));
        }

        return doc;
    }

    /// <summary>The file's unit of indentation: the leading tab, or spaces, of its first indented line.</summary>
    private static string? IndentOf(string source)
    {
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0) continue;
            if (line[0] == '\t') return "\t";
            if (line[0] == ' ') return new string(' ', line.Length - line.TrimStart(' ').Length);
        }
        return null;
    }

    private sealed class Reader(IReadOnlyList<HudToken> tokens, string[] sourceLines)
    {
        private int _pos;
        // The line the last token read ends on; 0 before the first.
        private int _lastLine;
        // Comments inside a block's header, which go after its line.
        private readonly List<string> _stray = new();

        /// <summary>The next token that is not a comment, without reading it.</summary>
        public HudToken Peek()
        {
            var i = _pos;
            while (tokens[i].Kind == HudTokenKind.Comment) i++;
            return tokens[i];
        }

        /// <summary>
        /// The whole lines before the next token, as written: its own-line comments and the blank
        /// lines between them; null when there are none.
        /// </summary>
        public List<string>? TakeLines()
        {
            List<string>? lines = null;
            while (true)
            {
                var t = tokens[_pos];
                for (var n = _lastLine + 1; n < t.Line; n++)
                    (lines ??= new List<string>()).Add(sourceLines[n - 1].TrimEnd('\r'));
                if (t.Kind != HudTokenKind.Comment) return lines;
                (lines ??= new List<string>()).Add(t.Gap + t.Value);
                Read();
            }
        }

        /// <summary>The comments after the last token on its line, with the gaps before them; null when there are none.</summary>
        public string? TakeSameLine()
        {
            string? text = null;
            while (tokens[_pos] is { Kind: HudTokenKind.Comment } t && t.Line == _lastLine)
            {
                text += t.Gap.Length > 0 || text is null ? t.Gap + t.Value : " " + t.Value;
                Read();
            }
            return text;
        }

        public HudBlock Block(List<string>? before)
        {
            var name = Expect(HudTokenKind.Identifier);
            var block = new HudBlock { Name = name.Value };
            var trivia = new HudTrivia { Before = before, Indent = name.StartsLine ? name.Gap : null };

            Expect(HudTokenKind.LParen);
            if (Peek().Kind != HudTokenKind.RParen)
            {
                block.Arguments.Add(Argument());
                while (Peek().Kind == HudTokenKind.Comma)
                {
                    Expect(HudTokenKind.Comma);
                    block.Arguments.Add(Argument());
                }
            }
            var close = Expect(HudTokenKind.RParen);
            if (_stray.Count == 0 && close.Line == name.Line)
            {
                // Its spelling, kept while the line is not edited.
                var raw = SourceLine(name.Line).Substring(name.Column - 1, close.Column - name.Column + 1);
                var canonical = HudWriter.Header(block);
                if (raw != canonical)
                {
                    trivia.RawHeader = raw;
                    trivia.HeaderAsRead = canonical;
                }
            }
            var after = TakeSameLine() ?? TrailingSpace(close);
            // Comments inside the header, or on lines of their own between it and its brace, go after
            // its line.
            var hasBody = Peek().Kind == HudTokenKind.LBrace;
            HudToken? open = hasBody ? Expect(HudTokenKind.LBrace) : null;
            if (open is { StartsLine: true }) trivia.OpenIndent = open.Value.Gap;
            if (_stray.Count > 0)
            {
                after = (after ?? string.Empty) + " " + string.Join(" ", _stray);
                _stray.Clear();
            }
            trivia.After = after;

            if (hasBody)
            {
                block.HasBody = true;
                trivia.AfterOpen = TakeSameLine() ?? TrailingSpace(open!.Value);
                // Tolerant at EOF: stock files (notably the localize cfgs) ship with unbalanced braces.
                // The original engine's parser is lenient, so we stop the current block silently if we
                // run off the end rather than throwing.
                while (true)
                {
                    var lines = TakeLines();
                    var next = Peek().Kind;
                    if (next is HudTokenKind.RBrace or HudTokenKind.EndOfFile)
                    {
                        trivia.BeforeClose = lines;
                        break;
                    }
                    block.Children.Add(Block(lines));
                }
                if (Peek().Kind == HudTokenKind.RBrace)
                {
                    var end = Expect(HudTokenKind.RBrace);
                    if (end.StartsLine) trivia.CloseIndent = end.Gap;
                    trivia.AfterClose = TakeSameLine() ?? TrailingSpace(end);
                }
            }

            block.Trivia = trivia;
            return block;
        }

        private string SourceLine(int line) => sourceLines[line - 1].TrimEnd('\r');

        /// <summary>The spaces or tabs after a token that ends its line, or null.</summary>
        private string? TrailingSpace(HudToken token)
        {
            var rest = SourceLine(token.Line)[(token.Column - 1 + token.Value.Length)..];
            return rest.Length > 0 && rest.Trim().Length == 0 ? rest : null;
        }

        private HudArgument Argument()
        {
            var t = Next();
            switch (t.Kind)
            {
                case HudTokenKind.String:
                    return new HudArgument(HudArgKind.String, t.Value);
                case HudTokenKind.Identifier:
                    return new HudArgument(HudArgKind.Identifier, t.Value);
                case HudTokenKind.Number:
                    var kind = t.Value.Contains('.') || t.Value.Contains('e') || t.Value.Contains('E')
                        ? HudArgKind.Float
                        : HudArgKind.Integer;
                    return new HudArgument(kind, t.Value);
                default:
                    throw new HudParseException($"Expected argument, got {t.Kind} '{t.Value}'", t.Line, t.Column);
            }
        }

        private HudToken Expect(HudTokenKind kind)
        {
            var t = Next();
            if (t.Kind != kind)
                throw new HudParseException($"Expected {kind}, got {t.Kind} '{t.Value}'", t.Line, t.Column);
            return t;
        }

        /// <summary>The next token that is not a comment; comments passed on the way go after the block's line.</summary>
        private HudToken Next()
        {
            while (tokens[_pos].Kind == HudTokenKind.Comment) _stray.Add(Read().Value);
            return Read();
        }

        private HudToken Read()
        {
            var t = tokens[_pos];
            if (t.Kind != HudTokenKind.EndOfFile) _pos++;
            _lastLine = t.LastLine;
            return t;
        }
    }
}
