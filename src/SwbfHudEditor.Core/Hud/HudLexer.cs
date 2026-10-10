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
    /// <summary>A <c>//</c> or <c>/* */</c> comment, its text as written, when the lexer keeps them.</summary>
    Comment,
    EndOfFile,
}

/// <summary>
/// A token: a comment also carries the line it ends on. When the lexer keeps comments, every token
/// carries <paramref name="Gap"/>, the spaces and tabs before it on its line, and whether nothing
/// else comes before it on the line.
/// </summary>
public readonly record struct HudToken(HudTokenKind Kind, string Value, int Line, int Column, int EndLine = 0,
    string Gap = "", bool StartsLine = false)
{
    public int LastLine => EndLine == 0 ? Line : EndLine;
}

public sealed class HudLexException(string message, int line, int column)
    : Exception($"{message} at line {line}, column {column}")
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

public static class HudLexer
{
    /// <summary>The tokens of a file; comments are skipped unless <paramref name="keepComments"/>.</summary>
    public static IReadOnlyList<HudToken> Tokenize(string source, bool keepComments = false)
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
                var start = i;
                while (i < len && source[i] != '\n') i++;
                if (keepComments)
                    tokens.Add(Lead(new HudToken(HudTokenKind.Comment, source[start..i].TrimEnd('\r'), line, col, line), source, start));
                col += i - start;
                continue;
            }
            if (c == '/' && i + 1 < len && source[i + 1] == '*')
            {
                var startLine = line; var startCol = col;
                var start = i;
                i += 2; col += 2;
                while (i + 1 < len && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    if (source[i] == '\n') { line++; col = 1; } else col++;
                    i++;
                }
                if (i + 1 >= len) throw new HudLexException("Unterminated block comment", startLine, startCol);
                i += 2; col += 2;
                if (keepComments)
                    tokens.Add(Lead(new HudToken(HudTokenKind.Comment, source[start..i].Replace("\r\n", "\n"), startLine, startCol, line), source, start));
                continue;
            }

            HudTokenKind? punctuation = c switch
            {
                '(' => HudTokenKind.LParen,
                ')' => HudTokenKind.RParen,
                '{' => HudTokenKind.LBrace,
                '}' => HudTokenKind.RBrace,
                ',' => HudTokenKind.Comma,
                _ => null,
            };
            if (punctuation is { } kind)
            {
                var token = new HudToken(kind, c.ToString(), line, col);
                tokens.Add(keepComments ? Lead(token, source, i) : token);
                i++; col++;
                continue;
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
                var token = new HudToken(HudTokenKind.Identifier, source[start..i], startLine, startCol);
                tokens.Add(keepComments ? Lead(token, source, start) : token);
                continue;
            }

            throw new HudLexException($"Unexpected character '{c}'", line, col);
        }

        tokens.Add(new HudToken(HudTokenKind.EndOfFile, string.Empty, line, col));
        return tokens;
    }

    /// <summary>The token with the spaces and tabs before <paramref name="index"/>, and whether they start the line.</summary>
    private static HudToken Lead(HudToken token, string source, int index)
    {
        var start = index;
        while (start > 0 && source[start - 1] is ' ' or '\t') start--;
        return token with { Gap = source[start..index], StartsLine = start == 0 || source[start - 1] is '\n' or '\r' };
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';
    private static bool IsIdentStart(char c) => c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    private static bool IsIdentPart(char c) => IsIdentStart(c) || IsDigit(c);
}
