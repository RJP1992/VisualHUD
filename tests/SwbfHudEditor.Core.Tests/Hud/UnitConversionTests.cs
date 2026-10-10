using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Hud;

/// <summary>A line's numbers rewritten in another unit, the same in pixels.</summary>
public class UnitConversionTests
{
    // 1920 x 1080 with one viewport; the line is in a group whose Rect is 400 x 200 pixels.
    private static readonly UnitBasis FullHd = new(400, 200, 1920, 1080, 1920, 1080);

    private static HudBlock Line(string source) => HudParser.Parse($"Bitmap(\"b\") {{ {source} }}").TopLevel[0].Children[0];

    private static string Text(IReadOnlyList<HudArgument> args) =>
        string.Join(", ", args.Select(a => a.Kind == HudArgKind.String ? $"\"{a.Raw}\"" : a.Raw));

    [Fact]
    public void A_Position_In_Pixels_Becomes_A_Share_Of_The_View()
    {
        var args = UnitConversion.Convert(Line("Position(960, 540, 0)"), RelativeMode.Viewport, FullHd)!;
        Assert.Equal("0.500000, 0.500000, 0, \"Viewport\"", Text(args));
    }

    [Fact]
    public void A_Position_Goes_Back_To_Pixels_And_Into_Its_Groups_Frame()
    {
        Assert.Equal("960.000000, 540.000000, 0.0, \"Pixels\"",
            Text(UnitConversion.Convert(Line("Position(0.5, 0.5, 0.0, \"Viewport\")"), RelativeMode.Pixels, FullHd)!));
        Assert.Equal("0.500000, 0.250000, 0.0, \"Frame\"",
            Text(UnitConversion.Convert(Line("Position(200, 50, 0.0, \"Pixels\")"), RelativeMode.Frame, FullHd)!));
    }

    [Fact]
    public void A_Rect_Without_Its_Mode_Is_Read_As_Viewport_And_Gets_Its_Alignments()
    {
        var line = Line("Rect(0.25, 0.5)");
        Assert.Equal(RelativeMode.Viewport, UnitConversion.ModeOf(line));
        Assert.Equal("480.000000, 540.000000, \"Left\", \"Top\", \"Pixels\"", Text(UnitConversion.Convert(line, RelativeMode.Pixels, FullHd)!));
        // Only a Position takes Frame.
        Assert.Null(UnitConversion.Convert(line, RelativeMode.Frame, FullHd));
    }

    [Fact]
    public void A_BitmapRect_Reads_Its_Mode_Only_With_Five_Arguments()
    {
        var five = Line("BitmapRect(0.1, 0.1, \"Center\", \"Center\", \"Viewport\")");
        Assert.Equal("0.100000, 0.100000, \"Center\", \"Center\", \"Screen\"", Text(UnitConversion.Convert(five, RelativeMode.Screen, FullHd)!));
        Assert.Equal("192.000000, 108.000000, \"Center\", \"Center\", \"Pixels\"", Text(UnitConversion.Convert(five, RelativeMode.Pixels, FullHd)!));
        Assert.False(UnitConversion.CanConvert(Line("BitmapRect(1, 1, \"Left\", \"Top\", \"Pixels\", 2)"), RelativeMode.Viewport));
    }

    [Fact]
    public void Pixels_Typed_On_A_Number_Are_Put_In_Its_Lines_Unit()
    {
        Assert.Equal(0.5f, UnitConversion.FromPixels(Line("Position(0, 0, 0, \"Viewport\")"), 0, 960, FullHd));
        Assert.Equal(0.25f, UnitConversion.FromPixels(Line("Position(0, 0, 0, \"Frame\")"), 1, 50, FullHd));
        Assert.Equal(270f, UnitConversion.FromPixels(Line("Position(0, 0, 0)"), 1, 270, FullHd));
        Assert.Equal(0.25f, UnitConversion.FromPixels(Line("TextBox(0.5, 0.5)"), 0, 480, FullHd));
        Assert.Null(UnitConversion.FromPixels(Line("Alpha(1)"), 0, 4, FullHd));
    }

    [Fact]
    public void The_Edit_Puts_The_Arguments_Back_On_Undo()
    {
        var line = Line("Position(960, 540, 0)");
        var edit = new ReplaceArgumentsEdit(line, UnitConversion.Convert(line, RelativeMode.Viewport, FullHd)!);
        edit.Apply();
        Assert.Equal(4, line.Arguments.Count);
        edit.Revert();
        Assert.Equal("960, 540, 0", Text(line.Arguments));
    }
}
