using System.Collections.ObjectModel;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Tests.Hud;

/// <summary>Copy, paste, duplicate and move: blocks as .hud text, unique names, placement and undo.</summary>
public class HudClipboardTests
{
    [Fact]
    public void Copied_Blocks_Are_Hud_Text_That_Reads_Back_The_Same()
    {
        var doc = HudParser.Parse("Group(\"g\")\n{\n\t// the bar\n\tBarBitmap(\"bar\")\n\t{\n\t\tValue(0.5) // half\n\t}\n}\n");
        var text = HudClipboard.Write(new[] { doc.TopLevel[0].Children[0] }, "    ", "\n");
        Assert.Equal("// the bar\nBarBitmap(\"bar\")\n{\n    Value(0.5) // half\n}\n", text);
        var back = HudClipboard.Read(text)!;
        Assert.Single(back);
        Assert.Equal("BarBitmap", back[0].Name);
        // The original is untouched.
        Assert.Equal("bar", doc.TopLevel[0].Children[0].ItemName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("just some words (")]
    [InlineData("   ")]
    public void Text_That_Is_Not_Hud_Reads_As_Nothing(string text) => Assert.Null(HudClipboard.Read(text));

    [Fact]
    public void Pasted_Names_And_Transform_Outputs_Are_Made_Unique()
    {
        var blocks = HudClipboard.Read("""
            Group("holder")
            {
                Text("label") { }
                Segment("Segment") { }
            }
            TransformNumberMath("math")
            {
                EventOutput("player1.math")
            }
            ObjectiveList("player1objectives") { }
            """)!;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "holder", "label", "math", "math_1", "player1objectives" };
        var events = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "player1.math" };
        var renamed = HudClipboard.MakeNamesUnique(blocks, names.Contains, events.Contains);

        Assert.Equal("holder_1", blocks[0].ItemName);
        Assert.Equal("label_1", blocks[0].Children[0].ItemName);
        Assert.Equal("Segment", blocks[0].Children[1].ItemName);       // a template keeps its name
        Assert.Equal("math_2", blocks[1].ItemName);
        Assert.Equal("player1.math_1", blocks[1].Children[0].Arguments[0].Raw);
        Assert.Equal("player1objectives", blocks[2].ItemName);          // found by its name
        Assert.Contains(("holder", "holder_1"), renamed);
    }

    [Fact]
    public void A_Numbered_Name_Counts_On_From_Its_Stem()
    {
        var taken = new HashSet<string> { "bar_1", "bar_2" };
        Assert.Equal("bar_3", HudClipboard.Unique("bar_1", taken.Contains));
        Assert.Equal("free", HudClipboard.Unique("free", taken.Contains));
    }

    [Fact]
    public void A_Clone_Is_Deep_And_Keeps_Comments_But_Not_Indentation()
    {
        var doc = HudParser.Parse("Group(\"g\")\n{\n\t\t// note\n\t\tAlpha(0.5)\n}\n");
        var clone = HudClipboard.Clone(doc.TopLevel[0]);
        Assert.NotSame(doc.TopLevel[0].Children[0], clone.Children[0]);
        Assert.Equal(new[] { "\t\t// note" }, clone.Children[0].Trivia!.Before);
        Assert.Null(clone.Children[0].Trivia!.Indent);
    }

    [Fact]
    public void Moves_Within_And_Between_Lists_Undo_In_Place()
    {
        var a = new HudBlock("Group", HudArgument.FromString("a")) { HasBody = true };
        var b = new HudBlock("Group", HudArgument.FromString("b")) { HasBody = true };
        var c = new HudBlock("Group", HudArgument.FromString("c")) { HasBody = true };
        var list = new ObservableCollection<HudBlock> { a, b, c };
        var other = new ObservableCollection<HudBlock>();

        var down = new MoveBlockEdit(list, 0, list, 2, a);
        down.Apply();
        Assert.Equal(new[] { b, c, a }, list);
        down.Revert();
        Assert.Equal(new[] { a, b, c }, list);

        var away = new MoveBlockEdit(list, 1, other, 0, b);
        away.Apply();
        Assert.Equal(new[] { a, c }, list);
        Assert.Equal(new[] { b }, other);
        away.Revert();
        Assert.Equal(new[] { a, b, c }, list);
        Assert.Empty(other);
    }

    [Fact]
    public void A_Moved_Or_Pasted_Block_Goes_Where_A_New_One_Of_Its_Kind_Could()
    {
        var doc = HudParser.Parse("""
            FileInfo("f") { Viewports(1) }
            Group("g") { }
            Text("t") { }
            Map("m") { BackdropSmall("BackdropSmall") { } }
            BarSegmented("ring") { Segment("Segment") { } }
            """);
        HudBlock B(int i) => doc.TopLevel[i];
        var bitmap = HudClipboard.Read("Bitmap(\"b\") { }")![0];
        var math = HudClipboard.Read("TransformNumberMath(\"m\") { }")![0];
        var line = HudClipboard.Read("Alpha(0.5)")![0];
        Assert.Null(HudBlockTemplates.WhyNot(bitmap, B(1), doc));
        Assert.NotNull(HudBlockTemplates.WhyNot(bitmap, B(2), doc));
        Assert.NotNull(HudBlockTemplates.WhyNot(math, B(1), doc));
        Assert.Null(HudBlockTemplates.WhyNot(math, null, doc));
        Assert.NotNull(HudBlockTemplates.WhyNot(line, B(1), doc));

        // Its own FileInfo moves within the file; a second one may not join it.
        Assert.Null(HudBlockTemplates.WhyNot(B(0), null, doc));
        Assert.NotNull(HudBlockTemplates.WhyNot(HudClipboard.Read("FileInfo(\"x\") { }")![0], null, doc));

        // A template goes only into an element that reads it, and stays where it is.
        var backdrop = B(3).Children[0];
        Assert.Null(HudBlockTemplates.WhyNot(backdrop, B(3), doc));
        Assert.NotNull(HudBlockTemplates.WhyNot(backdrop, B(1), doc));
        Assert.Null(HudBlockTemplates.WhyNot(HudClipboard.Read("BackdropLarge(\"BackdropLarge\") { }")![0], B(3), doc));
        Assert.Null(HudBlockTemplates.WhyNot(B(4).Children[0], B(4), doc));
    }

    [Fact]
    public void Arguments_Know_Their_Names_Choices_Flags_Colours_And_Events()
    {
        Assert.Equal("mode", PropertyArguments.NameOf("Position", 3));
        Assert.Equal("vertical alignment", PropertyArguments.NameOf("BitmapRect", 3));
        Assert.Null(PropertyArguments.NameOf("Position", 7));
        Assert.Equal(new[] { "Pixels", "Screen", "Viewport", "Frame" }, PropertyArguments.Choices("Position", 3));
        Assert.Equal(new[] { "Pixels", "Screen", "Viewport" }, PropertyArguments.Choices("Rect", 4));
        Assert.DoesNotContain("Word Reformat", PropertyArguments.Choices("TextBreak", 0)!);   // reads as Word
        Assert.Contains("NotEqual", PropertyArguments.Choices("Operation", 0, "TransformNumberCompare")!);
        Assert.Contains("Divide", PropertyArguments.Choices("Operation", 0, "TransformNumberMath")!);
        Assert.Null(PropertyArguments.Choices("Position", 0));
        Assert.True(PropertyArguments.IsFlag("PropagateAlpha"));
        Assert.False(PropertyArguments.IsFlag("Alpha"));
        Assert.Equal(0, PropertyArguments.ColorStart("Color"));
        Assert.Equal(1, PropertyArguments.ColorStart("NumberColor"));
        Assert.Null(PropertyArguments.ColorStart("Position"));
        Assert.True(PropertyArguments.IsEvent("EventEnable", 0));
        Assert.True(PropertyArguments.IsEvent("InputRange", 1));
        Assert.False(PropertyArguments.IsEvent("Text", 0));
    }
}
