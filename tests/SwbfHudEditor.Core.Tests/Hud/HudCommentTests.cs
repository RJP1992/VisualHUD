using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Hud;

/// <summary>Saving keeps a file's comments, blank lines, indentation and line endings.</summary>
public class HudCommentTests
{
    private const string Sample =
        "// HUD for the test\r\n" +
        "FileInfo(\"f\")\r\n" +
        "{\r\n" +
        "\tViewports(1)\r\n" +
        "}\r\n" +
        "\r\n" +
        "//Marker opacity\r\n" +
        "Group(\"g\")\r\n" +
        "{\r\n" +
        "\tAlpha(1.000000)\t\t// knob\r\n" +
        "\r\n" +
        "\t/* a block\r\n" +
        "\t   comment */\r\n" +
        "\tBitmap(\"b\") // the bitmap\r\n" +
        "\t{\r\n" +
        "\t\tBitmapRect(4, 4)\r\n" +
        "\t\t// last in b\r\n" +
        "\t}\r\n" +
        "}\r\n" +
        "// end of file\r\n";

    [Fact]
    public void A_File_Comes_Back_As_It_Was()
    {
        var doc = HudParser.Parse(Sample);
        Assert.Equal("\t", doc.Indent);
        Assert.Equal("\r\n", doc.LineEnding);
        Assert.Equal(Sample, HudWriter.Write(doc));
    }

    [Fact]
    public void Comments_Sit_With_What_They_Are_About()
    {
        var doc = HudParser.Parse(Sample);
        var group = doc.TopLevel[1];
        Assert.Equal(new[] { "", "//Marker opacity" }, group.Trivia!.Before);
        var alpha = group.Children[0];
        Assert.Equal("\t\t// knob", alpha.Trivia!.After);
        var bitmap = group.Children[1];
        // Whole lines are kept as written, indentation included.
        Assert.Equal(new[] { "", "\t/* a block\n\t   comment */" }, bitmap.Trivia!.Before);
        Assert.Equal(" // the bitmap", bitmap.Trivia.After);
        Assert.Equal(new[] { "\t\t// last in b" }, bitmap.Trivia.BeforeClose);
        Assert.Equal(new[] { "// end of file" }, doc.Closing);
    }

    [Fact]
    public void An_Edit_Keeps_The_Comments_Around_It()
    {
        var doc = HudParser.Parse(Sample);
        var alpha = doc.TopLevel[1].Children[0];
        alpha.Arguments[0] = alpha.Arguments[0] with { Raw = "0.5" };
        doc.TopLevel[1].Children.Add(new HudBlock("PropagateAlpha", HudArgument.FromInt(1)));
        var text = HudWriter.Write(doc);
        Assert.Contains("\tAlpha(0.5)\t\t// knob\r\n", text);
        Assert.Contains("\t\t// last in b\r\n\t}\r\n\tPropagateAlpha(1)\r\n}\r\n", text);
    }

    [Fact]
    public void Spacing_As_Written_Is_Kept_Until_The_Line_Is_Edited()
    {
        var doc = HudParser.Parse("Group(\"g\")  \n{\n\tColor(234, 1 ,1)\n\tAlpha( 1 )\n}");
        Assert.Equal("Group(\"g\")  \n{\n\tColor(234, 1 ,1)\n\tAlpha( 1 )\n}", HudWriter.Write(doc));
        var color = doc.TopLevel[0].Children[0];
        color.Arguments[0] = color.Arguments[0] with { Raw = "200" };
        Assert.Contains("\tColor(200, 1, 1)\n", HudWriter.Write(doc));
    }

    [Fact]
    public void A_Default_Line_With_A_Comment_Is_Kept()
    {
        var doc = HudParser.Parse("Group(\"g\")\n{\n\t//Marker opacity\n\tAlpha(1.000000)\n\tRotation(0, 0, 0)\n\tScale(1, 1, 1) // as authored\n}\n");
        var text = ElementDefaults.WriteWithoutDefaults(doc);
        Assert.Contains("//Marker opacity\n\tAlpha(1.000000)\n", text);
        Assert.Contains("Scale(1, 1, 1) // as authored", text);
        Assert.DoesNotContain("Rotation", text);
    }

    [Fact]
    public void Odd_Places_Keep_Their_Comments()
    {
        // Inside the parentheses, and on a line of its own before the brace: after the header.
        var doc = HudParser.Parse("Group(\"g\", /* why */ 1)\n// before the brace\n{\n}\n");
        var text = HudWriter.Write(doc);
        Assert.Contains("/* why */", text);
        Assert.Contains("// before the brace", text);
        var again = HudParser.Parse(text);
        Assert.Equal(text, HudWriter.Write(again));
    }

    [Fact]
    public void A_Block_Made_In_The_Editor_Is_Written_Plainly()
    {
        var doc = new HudDocument();
        var group = new HudBlock("Group", HudArgument.FromString("g")) { HasBody = true };
        group.Children.Add(new HudBlock("Alpha", HudArgument.FromInt(1)));
        doc.TopLevel.Add(group);
        Assert.Equal("Group(\"g\")\n{\n    Alpha(1)\n}", HudWriter.Write(doc));
    }
}
