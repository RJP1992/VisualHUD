using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>Text laid out as RedTextElement::ClipString and WriteCache lay it out.</summary>
public class TextLayoutTests
{
    /// <summary>Every character 5 wide with pixels, a space 3 wide without; cells 10 tall.</summary>
    private sealed class TestFont : ITextFont
    {
        public int Height => 10;
        public int Ascent => 8;
        public FontGlyph? Find(char c) => c == ' ' ? new FontGlyph(c, 3, 0, 0, 0, 0) : new FontGlyph(c, 5, 0, 1, 5, 9);
    }

    private static TextLayoutResult Lay(string text, TextLayoutOptions? options = null) =>
        TextLayout.Lay(text, new TestFont(), options ?? new TextLayoutOptions());

    private static string[] Lines(TextLayoutResult r, string text) => r.Lines.Select(l => text[l.Start..l.End]).ToArray();

    private static float[] Xs(TextLayoutResult r) => r.Glyphs.Select(g => g.X).ToArray();

    [Fact]
    public void Characters_Follow_Each_Other_With_Their_Spacing()
    {
        // A letter moves 5 and the default spacing of 1.
        var r = Lay("abc");
        Assert.Equal(new[] { 0f, 6f, 12f }, Xs(r));
        Assert.Equal(18, r.Lines.Single().Width);
        Assert.Equal(10, r.LineHeight);
    }

    [Fact]
    public void Without_A_Box_Right_Ends_At_The_Origin_And_Center_Straddles_It()
    {
        Assert.Equal(-18f, Xs(Lay("abc", new TextLayoutOptions { HAlign = TextHAlign.Right }))[0]);
        Assert.Equal(-9f, Xs(Lay("abc", new TextLayoutOptions { HAlign = TextHAlign.Center }))[0]);
    }

    [Fact]
    public void Word_Breaks_At_The_Last_Space_That_Fits()
    {
        const string text = "aaa bbb ccc";
        var r = Lay(text, new TextLayoutOptions { BoxWidth = 30, Break = TextBreak.Word });
        Assert.Equal(new[] { "aaa", "bbb", "ccc" }, Lines(r, text));
        Assert.Equal(new[] { 0, 10, 20 }, r.Lines.Select(l => l.Y).ToArray());
    }

    [Fact]
    public void A_Word_Longer_Than_The_Box_Breaks_Inside()
    {
        const string text = "aaaaaaaaaa";
        var r = Lay(text, new TextLayoutOptions { BoxWidth = 30, Break = TextBreak.Word });
        Assert.Equal(new[] { "aaaaa", "aaaaa" }, Lines(r, text));
    }

    [Fact]
    public void Without_Wrapping_A_Line_Is_Cut_At_The_Box_Edge()
    {
        // Each character is drawn only if it fits whole: x + 5 at most 30.
        var r = Lay("aaaaaaaaaa", new TextLayoutOptions { BoxWidth = 30 });
        Assert.Single(r.Lines);
        Assert.Equal(new[] { 0f, 6f, 12f, 18f, 24f }, Xs(r));
    }

    [Fact]
    public void The_Box_Height_Limits_The_Lines_And_Bottom_Keeps_The_Last()
    {
        const string text = "aaa bbb ccc";
        // 25 pixels tall with 10-pixel lines: two lines.
        var top = Lay(text, new TextLayoutOptions { BoxWidth = 30, BoxHeight = 25, Break = TextBreak.Word });
        Assert.Equal(new[] { "aaa", "bbb" }, Lines(top, text));
        var bottom = Lay(text, new TextLayoutOptions { BoxWidth = 30, BoxHeight = 25, Break = TextBreak.Word, VAlign = TextVAlign.Bottom });
        Assert.Equal(new[] { "bbb", "ccc" }, Lines(bottom, text));
        Assert.Equal(new[] { 5, 15 }, bottom.Lines.Select(l => l.Y).ToArray());
    }

    [Fact]
    public void Center_Sits_The_Lines_In_The_Middle_Of_The_Box()
    {
        var r = Lay("a", new TextLayoutOptions { BoxWidth = 30, BoxHeight = 30, VAlign = TextVAlign.Center, HAlign = TextHAlign.Center });
        Assert.Equal(10, r.Lines.Single().Y);
        Assert.Equal(12, r.Lines.Single().X);   // (30 - 6) / 2
    }

    [Fact]
    public void Reformat_Counts_A_Run_Of_Spaces_As_One_Only_To_Break_And_Align()
    {
        // ClipString measures the run as one space; WriteCache still moves past every space.
        var reformat = Lay("a    b", new TextLayoutOptions { Break = TextBreak.NoReformat });
        Assert.Equal(16, reformat.Lines.Single().Width);
        Assert.Equal(22f, Xs(reformat)[1]);
        var plain = Lay("a    b");
        Assert.Equal(28, plain.Lines.Single().Width);
        Assert.Equal(22f, Xs(plain)[1]);
    }

    [Fact]
    public void Line_Breaks_And_Tabs()
    {
        const string text = "ab\ncd";
        var r = Lay(text);
        Assert.Equal(new[] { "ab", "cd" }, Lines(r, text));
        Assert.Equal(new[] { 0, 10 }, r.Lines.Select(l => l.Y).ToArray());
        // A tab stop every eight spaces with their spacing: 8 × (3 + 1).
        Assert.Equal(32f, Xs(Lay("a\tb"))[1]);
    }

    [Fact]
    public void Text_Scale_Widens_Characters_And_Lines()
    {
        var r = Lay("ab", new TextLayoutOptions { ScaleX = 2f, ScaleY = 1.5f });
        Assert.Equal(new[] { 0f, 11f }, Xs(r));   // round(5 × 2) + 1
        Assert.Equal(15, r.LineHeight);
    }

    [Fact]
    public void A_Stock_Font_Has_The_Games_Metrics()
    {
        var path = @"C:\BF2_ModTools\data\Common\fonts\gamefont_small.fff";
        if (!File.Exists(path)) return; // hermetic skip when modtools aren't on this machine
        var font = FffFont.Read(path);
        Assert.Equal(17, font.Height);
        Assert.Equal(new FontGlyph('A', 10, 0, 5, 10, 14), font.Find('A'));
        Assert.False(font.Find(' ')!.Value.Printing);
        Assert.Equal(14, font.Ascent);
    }

    private static ElementSpec Text(string body) =>
        ElementReader.Read(HudParser.Parse($"Text(\"t\") {{ {body} }}").TopLevel[0], ElementKind.Text);

    [Fact]
    public void Examples_Are_What_The_Events_Would_Send()
    {
        Assert.Equal("120", HudTextExamples.For(Text("EventNumber(\"player1.weapon1.totalAmmoBullets\")"), TextPreview.Examples, null));
        Assert.Equal("2:05", HudTextExamples.For(Text("EventNumber(\"objectivetimer\") NumberToTime(\"%m:%02s\")"), TextPreview.Examples, null));
        var name = Text("EventText(\"player1.weapon1.name\")");
        Assert.Equal("DC-15A Blaster Rifle", HudTextExamples.For(name, TextPreview.Examples, null));
        Assert.Equal("Wookiee Bowcaster Heavy Repeater Rifle", HudTextExamples.For(name, TextPreview.Long, null));
        Assert.Equal("{name}", HudTextExamples.For(name, TextPreview.Keys, null));
    }

    [Fact]
    public void Keys_Show_Their_String_With_Example_Buttons()
    {
        var hint = Text("Text(\"game.hint\")");
        string? Localize(string key) => key == "game.hint" ? "Press {GuiAccept} to continue" : null;
        Assert.Equal("Press [Accept] to continue", HudTextExamples.For(hint, TextPreview.Examples, Localize));
        Assert.Equal("game.hint", HudTextExamples.For(hint, TextPreview.Keys, Localize));
        // A key the localization lacks shows as itself, as the game shows it.
        Assert.Equal("game.missing", HudTextExamples.For(Text("Text(\"game.missing\")"), TextPreview.Examples, Localize));
        Assert.Equal("[Lock Target] [Select] [Use] [Map]", HudTextExamples.WithButtons("{lockTargetButton} {btn_select} {usebutton} {Map}"));
    }

    [Fact]
    public void The_Reader_Takes_The_Break_And_The_Spacing()
    {
        var spec = Text("TextBreak(\"No Reformat\") TextCharacterSpacing(3) TextTabSpacing(40)");
        Assert.Equal(TextBreak.NoReformat, spec.TextBreak);
        Assert.Equal(3, spec.TextSpacing);
        Assert.Equal(40, spec.TextTabSpacing);
        // TranslateString takes the first name the text starts with: "Word Reformat" reads as Word,
        // so a file cannot ask for it.
        Assert.Equal(TextBreak.Word, Text("TextBreak(\"Word Reformat\")").TextBreak);
        // A name that is none of the four leaves it as it was.
        Assert.Equal(TextBreak.Word, Text("TextBreak(\"Word\") TextBreak(\"Sideways\")").TextBreak);
    }
}
