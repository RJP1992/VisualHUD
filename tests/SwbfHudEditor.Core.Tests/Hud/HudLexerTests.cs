using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Tests.Hud;

public class HudLexerTests
{
    [Fact]
    public void Tokenizes_Punctuation()
    {
        var tokens = HudLexer.Tokenize("(){},");
        Assert.Equal(
            new[] { HudTokenKind.LParen, HudTokenKind.RParen, HudTokenKind.LBrace, HudTokenKind.RBrace, HudTokenKind.Comma, HudTokenKind.EndOfFile },
            tokens.Select(t => t.Kind).ToArray());
    }

    [Fact]
    public void Tokenizes_Identifier()
    {
        var tokens = HudLexer.Tokenize("Group ZOrder _foo bar1");
        Assert.Equal(new[] { "Group", "ZOrder", "_foo", "bar1" }, tokens.Take(4).Select(t => t.Value).ToArray());
        Assert.All(tokens.Take(4), t => Assert.Equal(HudTokenKind.Identifier, t.Kind));
    }

    [Fact]
    public void Tokenizes_String_Including_Empty()
    {
        var tokens = HudLexer.Tokenize("\"hello world\" \"\"");
        Assert.Equal("hello world", tokens[0].Value);
        Assert.Equal(HudTokenKind.String, tokens[0].Kind);
        Assert.Equal(string.Empty, tokens[1].Value);
        Assert.Equal(HudTokenKind.String, tokens[1].Kind);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("123")]
    [InlineData("-7")]
    [InlineData("+42")]
    [InlineData("0.5")]
    [InlineData("0.082181")]
    [InlineData("-1.5")]
    [InlineData(".5")]
    [InlineData("1e5")]
    [InlineData("1.5E-3")]
    public void Tokenizes_Number(string input)
    {
        var tokens = HudLexer.Tokenize(input);
        Assert.Equal(HudTokenKind.Number, tokens[0].Kind);
        Assert.Equal(input, tokens[0].Value);
    }

    [Fact]
    public void Tracks_Line_And_Column()
    {
        var tokens = HudLexer.Tokenize("Foo\n  Bar");
        Assert.Equal(1, tokens[0].Line);
        Assert.Equal(1, tokens[0].Column);
        Assert.Equal(2, tokens[1].Line);
        Assert.Equal(3, tokens[1].Column);
    }

    [Fact]
    public void Skips_Line_Comments()
    {
        var tokens = HudLexer.Tokenize("Foo // ignored\nBar");
        Assert.Equal(new[] { "Foo", "Bar" }, tokens.Where(t => t.Kind == HudTokenKind.Identifier).Select(t => t.Value).ToArray());
    }

    [Fact]
    public void Skips_Block_Comments()
    {
        var tokens = HudLexer.Tokenize("Foo /* a\nb\nc */ Bar");
        Assert.Equal(new[] { "Foo", "Bar" }, tokens.Where(t => t.Kind == HudTokenKind.Identifier).Select(t => t.Value).ToArray());
        Assert.Equal(3, tokens.First(t => t.Value == "Bar").Line);
    }

    [Fact]
    public void Throws_On_Unterminated_String()
    {
        Assert.Throws<HudLexException>(() => HudLexer.Tokenize("\"oops"));
    }

    [Fact]
    public void Throws_On_Newline_In_String()
    {
        Assert.Throws<HudLexException>(() => HudLexer.Tokenize("\"line1\nline2\""));
    }
}
