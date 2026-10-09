using System.Text;

namespace SwbfHudEditor.Core.Hud;

public enum HudTokenKind
{
    Identifier,
    String,
    Number,
    LParen,
    RParen,
    LBrace,
    RBrace,
    Comma,
    EndOfFile,
}

public readonly record struct HudToken(HudTokenKind Kind, string Value, int Line, int Column);

public sealed class HudLexException(string message, int line, int column)
    : Exception($"{message} at line {line}, column {column}")
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

public static class HudLexer
{
    public static IReadOnlyList<HudToken> Tokenize(string source)
    {
        var tokens = new List<HudToken>(capacity: 1024);
        var line = 1;
        var col = 1;
        var i = 0;
        var len = source.Length;

        while (i < len)
        {
            var c = source[i];

            if (c == '\n') { i++; line++; col = 1; continue; }
            if (c == '\r' || c == ' ' || c == '\t' || c == ';') { i++; col++; continue; }

            if (c == '/' && i + 1 < len && source[i + 1] == '/')
            {
                while (i < len && source[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < len && source[i + 1] == '*')
            {
                var startLine = line; var startCol = col;
                i += 2; col += 2;
                while (i + 1 < len && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    if (source[i] == '\n') { line++; col = 1; } else col++;
                    i++;
                }
                if (i + 1 >= len) throw new HudLexException("Unterminated block comment", startLine, startCol);
                i += 2; col += 2;
                continue;
            }

            switch (c)
            {
                case '(': tokens.Add(new HudToken(HudTokenKind.LParen, "(", line, col)); i++; col++; continue;
                case ')': tokens.Add(new HudToken(HudTokenKind.RParen, ")", line, col)); i++; col++; continue;
                case '{': tokens.Add(new HudToken(HudTokenKind.LBrace, "{", line, col)); i++; col++; continue;
                case '}': tokens.Add(new HudToken(HudTokenKind.RBrace, "}", line, col)); i++; col++; continue;
                case ',': tokens.Add(new HudToken(HudTokenKind.Comma, ",", line, col)); i++; col++; continue;
            }

            if (c == '"')
            {
                var startLine = line; var startCol = col;
                i++; col++;
                var sb = new StringBuilder();
                while (i < len && source[i] != '"')
                {
                    if (source[i] == '\n') throw new HudLexException("Newline in string literal", startLine, startCol);
                    sb.Append(source[i]);
                    i++; col++;
                }
                if (i >= len) throw new HudLexException("Unterminated string literal", startLine, startCol);
                i++; col++;
                tokens.Add(new HudToken(HudTokenKind.String, sb.ToString(), startLine, startCol));
                continue;
            }

            if (c == '-' || c == '+' || (c >= '0' && c <= '9') || (c == '.' && i + 1 < len && IsDigit(source[i + 1])))
            {
                var startLine = line; var startCol = col;
                var start = i;
                if (c == '-' || c == '+') { i++; col++; }
                while (i < len && IsDigit(source[i])) { i++; col++; }
                if (i < len && source[i] == '.')
                {
                    i++; col++;
                    while (i < len && IsDigit(source[i])) { i++; col++; }
                }
                if (i < len && (source[i] == 'e' || source[i] == 'E'))
                {
                    i++; col++;
                    if (i < len && (source[i] == '+' || source[i] == '-')) { i++; col++; }
                    while (i < len && IsDigit(source[i])) { i++; col++; }
                }
                tokens.Add(new HudToken(HudTokenKind.Number, source[start..i], startLine, startCol));
                continue;
            }

            if (IsIdentStart(c))
            {
                var startLine = line; var startCol = col;
                var start = i;
                while (i < len && IsIdentPart(source[i])) { i++; col++; }
                tokens.Add(new HudToken(HudTokenKind.Identifier, source[start..i], startLine, startCol));
                continue;
            }

            throw new HudLexException($"Unexpected character '{c}'", line, col);
        }

        tokens.Add(new HudToken(HudTokenKind.EndOfFile, string.Empty, line, col));
        return tokens;
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';
    private static bool IsIdentStart(char c) => c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    private static bool IsIdentPart(char c) => IsIdentStart(c) || IsDigit(c);
}
