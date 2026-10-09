namespace SwbfHudEditor.Core.Hud;

public sealed class HudParseException(string message, int line, int column)
    : Exception($"{message} at line {line}, column {column}")
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

public static class HudParser
{
    public static HudDocument Parse(string source, string? sourcePath = null)
    {
        var tokens = HudLexer.Tokenize(source);
        var pos = 0;
        var doc = new HudDocument { SourcePath = sourcePath };

        while (Peek(tokens, pos).Kind != HudTokenKind.EndOfFile)
        {
            doc.TopLevel.Add(ParseBlock(tokens, ref pos));
        }

        return doc;
    }

    private static HudBlock ParseBlock(IReadOnlyList<HudToken> tokens, ref int pos)
    {
        var name = Expect(tokens, ref pos, HudTokenKind.Identifier);
        var block = new HudBlock { Name = name.Value };

        Expect(tokens, ref pos, HudTokenKind.LParen);
        if (Peek(tokens, pos).Kind != HudTokenKind.RParen)
        {
            block.Arguments.Add(ParseArgument(tokens, ref pos));
            while (Peek(tokens, pos).Kind == HudTokenKind.Comma)
            {
                pos++;
                block.Arguments.Add(ParseArgument(tokens, ref pos));
            }
        }
        Expect(tokens, ref pos, HudTokenKind.RParen);

        if (Peek(tokens, pos).Kind == HudTokenKind.LBrace)
        {
            pos++;
            block.HasBody = true;
            // Tolerant at EOF: stock files (notably the localize cfgs) ship with unbalanced braces.
            // The original engine's parser is lenient, so we stop the current block silently if we
            // run off the end rather than throwing.
            while (Peek(tokens, pos).Kind != HudTokenKind.RBrace && Peek(tokens, pos).Kind != HudTokenKind.EndOfFile)
            {
                block.Children.Add(ParseBlock(tokens, ref pos));
            }
            if (Peek(tokens, pos).Kind == HudTokenKind.RBrace) pos++;
        }

        return block;
    }

    private static HudArgument ParseArgument(IReadOnlyList<HudToken> tokens, ref int pos)
    {
        var t = tokens[pos];
        switch (t.Kind)
        {
            case HudTokenKind.String:
                pos++;
                return new HudArgument(HudArgKind.String, t.Value);
            case HudTokenKind.Identifier:
                pos++;
                return new HudArgument(HudArgKind.Identifier, t.Value);
            case HudTokenKind.Number:
                pos++;
                var kind = t.Value.Contains('.') || t.Value.Contains('e') || t.Value.Contains('E')
                    ? HudArgKind.Float
                    : HudArgKind.Integer;
                return new HudArgument(kind, t.Value);
            default:
                throw new HudParseException($"Expected argument, got {t.Kind} '{t.Value}'", t.Line, t.Column);
        }
    }

    private static HudToken Peek(IReadOnlyList<HudToken> tokens, int pos) => tokens[pos];

    private static HudToken Expect(IReadOnlyList<HudToken> tokens, ref int pos, HudTokenKind kind)
    {
        var t = tokens[pos];
        if (t.Kind != kind)
            throw new HudParseException($"Expected {kind}, got {t.Kind} '{t.Value}'", t.Line, t.Column);
        pos++;
        return t;
    }
}
