using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>Lines read the way the engine's readers read them, quirks included.</summary>
public class ElementReaderTests
{
    private static ElementSpec Read(string source)
    {
        var block = HudParser.Parse(source).TopLevel[0];
        Assert.True(ElementKinds.TryGet(block, out var kind));
        return ElementReader.Read(block, kind);
    }

    [Theory]
    [InlineData("Position(1, 2, 3)", RelativeMode.Pixels)]
    [InlineData("Position(1, 2, 3, \"Viewport\")", RelativeMode.Viewport)]
    [InlineData("Position(1, 2, 3, \"viewport\")", RelativeMode.Viewport)]
    [InlineData("Position(1, 2, 3, \"ScreenSpace\")", RelativeMode.Screen)]   // a prefix match
    [InlineData("Position(1, 2, 3, \"Frame\")", RelativeMode.Frame)]
    [InlineData("Position(1, 2, 3, \"Viewpor\")", RelativeMode.Pixels)]        // too short to be a name: Pixels
    [InlineData("Position(1, 2, 3, \"Centre\")", RelativeMode.Pixels)]
    [InlineData("Position(1, 2, 3, 1)", RelativeMode.Viewport)]
    [InlineData("Position(1, 2, 3, 0)", RelativeMode.Pixels)]
    [InlineData("Position(1, 2, 3, \"Viewport\", 5)", RelativeMode.Pixels)]   // only four arguments read a mode
    public void Position_Modes(string line, RelativeMode mode)
    {
        var s = Read($"Bitmap(\"b\") {{ {line} }}");
        Assert.Equal(mode, s.PositionMode);
        Assert.Equal(new System.Numerics.Vector3(1, 2, 3), s.Position);
    }

    [Theory]
    [InlineData("BitmapRect(0.1, 0.2)", RelativeMode.Viewport, HAlign.Left, VAlign.Top)]
    [InlineData("BitmapRect(0.1, 0.2, \"Center\", \"Bottom\")", RelativeMode.Viewport, HAlign.Center, VAlign.Bottom)]
    [InlineData("BitmapRect(0.1, 0.2, \"Right\", \"Center\", \"Pixels\")", RelativeMode.Pixels, HAlign.Right, VAlign.Center)]
    [InlineData("BitmapRect(0.1, 0.2, \"Right\", \"Center\", \"Screen\")", RelativeMode.Screen, HAlign.Right, VAlign.Center)]
    [InlineData("BitmapRect(0.1, 0.2, \"Left\", \"Top\", 0)", RelativeMode.Screen, HAlign.Left, VAlign.Top)]   // 0 means Screen
    [InlineData("BitmapRect(0.1, 0.2, \"Left\", \"Top\", 1)", RelativeMode.Viewport, HAlign.Left, VAlign.Top)]
    [InlineData("BitmapRect(0.1, 0.2, \"Left\", \"Top\", \"Frame\")", RelativeMode.Viewport, HAlign.Left, VAlign.Top)]   // only three names
    [InlineData("BitmapRect(0.1, 0.2, \"Middle\", \"Nowhere\")", RelativeMode.Viewport, HAlign.Left, VAlign.Top)]
    public void BitmapRect_Reading(string line, RelativeMode mode, HAlign h, VAlign v)
    {
        var s = Read($"Bitmap(\"b\") {{ {line} }}");
        Assert.True(s.HasBitmapRect);
        Assert.Equal(mode, s.BitmapMode);
        Assert.Equal(h, s.BitmapHAlign);
        Assert.Equal(v, s.BitmapVAlign);
    }

    [Fact]
    public void Group_Rect()
    {
        var s = Read("Group(\"g\") { }");
        Assert.Equal((1f, 1f, RelativeMode.Viewport), (s.RectWidth, s.RectHeight, s.RectMode));
        s = Read("Group(\"g\") { Rect(0.2, 0.1, \"Center\", \"Bottom\", \"Pixels\") }");
        Assert.Equal((0.2f, 0.1f, HAlign.Center, VAlign.Bottom, RelativeMode.Pixels),
            (s.RectWidth, s.RectHeight, s.RectHAlign, s.RectVAlign, s.RectMode));
        s = Read("Group(\"g\") { Rect(0.2, 0.1, \"Center\", \"Bottom\", \"Bogus\") }");
        Assert.Equal(RelativeMode.Viewport, s.RectMode);   // an unknown name keeps the mode
        s = Read("Group(\"g\") { Rect(0.2, 0.1, \"Center\", \"Bottom\", 0) }");
        Assert.Equal(RelativeMode.Pixels, s.RectMode);
    }

    [Fact]
    public void Text_Alignment_Has_Right_Before_Center()
    {
        var s = Read("Text(\"t\") { TextAlignment(\"Center\", \"Baseline\") }");
        Assert.Equal((TextHAlign.Center, TextVAlign.Baseline), (s.TextHAlign, s.TextVAlign));
        s = Read("Text(\"t\") { TextAlignment(\"Right\", \"Bottom\") TextAlignment(\"Nope\", \"Center\") }");
        Assert.Equal((TextHAlign.Right, TextVAlign.Center), (s.TextHAlign, s.TextVAlign));   // a bad half keeps its value
    }

    [Fact]
    public void Text_Key_And_Text_Element_Are_Told_Apart()
    {
        var s = Read("Group(\"g\") { Text(\"label\") { Text(\"hud.key\") } }");
        Assert.Single(s.Children);
        var text = ElementReader.Read(s.Children[0], ElementKind.Text);
        Assert.Equal("hud.key", text.TextKey);
        Assert.Equal("label", text.Name);
    }

    [Fact]
    public void FillFrom_Is_GameExts_Reader()
    {
        Assert.Equal(FillFrom.Right, Read("BarBitmap(\"b\") { FillFrom(\"right\") }").FillFrom);
        Assert.Equal(FillFrom.Bottom, Read("BarBitmap(\"b\") { FillFrom(\"Bottom\") }").FillFrom);
        Assert.Equal(FillFrom.Left, Read("BarBitmap(\"b\") { FillFrom(\"Top\") FillFrom(\"Sideways\") }").FillFrom);
        Assert.Equal(FillFrom.Left, Read("BarBitmap(\"b\") { FillFrom(Right) }").FillFrom);   // unquoted: not a string
        Assert.Equal(FillFrom.Top, Read("ProceduralBarBitmap(\"b\") { FillFrom(\"Top\") }").FillFrom);
        Assert.Equal(FillFrom.Left, Read("Bitmap(\"b\") { FillFrom(\"Right\") }").FillFrom);   // bars only
    }

    [Fact]
    public void ScreenAnchor_Names_Shares_And_Bad_Lines()
    {
        var s = Read("Group(\"g\") { ScreenAnchor(\"right\") }");
        Assert.Equal((1f, true), (s.ScreenAnchorShare, s.ScreenAnchorNamed));
        s = Read("Group(\"g\") { ScreenAnchor(Center) }");
        Assert.Equal((0.5f, true), (s.ScreenAnchorShare, s.ScreenAnchorNamed));
        s = Read("Group(\"g\") { ScreenAnchor(0.25) }");
        Assert.Equal((0.25f, false), (s.ScreenAnchorShare, s.ScreenAnchorNamed));
        // A bad line is ignored and an earlier one stands.
        s = Read("Group(\"g\") { ScreenAnchor(0.25) ScreenAnchor(1.5) ScreenAnchor(\"Centre\") ScreenAnchor(0.1, 0.2) }");
        Assert.Equal((0.25f, false), (s.ScreenAnchorShare, s.ScreenAnchorNamed));
        Assert.Null(Read("Group(\"g\") { }").ScreenAnchorShare);
    }

    [Fact]
    public void A_Later_Line_Replaces_An_Earlier_One()
    {
        var s = Read("Bitmap(\"b\") { Position(1, 2, 0, \"Viewport\") Position(5, 6, 0) }");
        Assert.Equal((5f, 6f, RelativeMode.Pixels), (s.Position.X, s.Position.Y, s.PositionMode));
    }

    [Fact]
    public void Bar_Range()
    {
        var s = Read("BarBitmap(\"b\") { MinValue(0) MaxValue(100) Value(25) }");
        Assert.Equal(0.25f, s.BarFraction(25f));
        Assert.Equal(1f, s.BarFraction(250f));
        s = Read("BarBitmap(\"b\") { MinValue(5) MaxValue(2) }");   // the maximum rises to the minimum
        Assert.Equal(0f, s.BarFraction(9f));
    }
}
