using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Tests.Hud;

public class HudParserTests
{
    [Fact]
    public void Parses_Empty_Source_To_Empty_Document()
    {
        var doc = HudParser.Parse("");
        Assert.Empty(doc.TopLevel);
    }

    [Fact]
    public void Parses_Leaf_Block_With_Mixed_Args()
    {
        var doc = HudParser.Parse("Position(0.082181, 0.427927, 0.000000, \"Viewport\")");
        Assert.Single(doc.TopLevel);
        var b = doc.TopLevel[0];
        Assert.Equal("Position", b.Name);
        Assert.Equal(4, b.Arguments.Count);
        Assert.Equal(HudArgKind.Float, b.Arguments[0].Kind);
        Assert.Equal("0.082181", b.Arguments[0].Raw);
        Assert.Equal(HudArgKind.String, b.Arguments[3].Kind);
        Assert.Equal("Viewport", b.Arguments[3].Raw);
        Assert.False(b.HasBody);
    }

    [Fact]
    public void Parses_Block_With_Body_And_Children()
    {
        const string src = """
            Group("outer")
            {
                Position(0.0, 0.0, 0.0, "Viewport")
                Group("inner")
                {
                    ZOrder(255)
                }
            }
            """;
        var doc = HudParser.Parse(src);
        Assert.Single(doc.TopLevel);
        var outer = doc.TopLevel[0];
        Assert.Equal("Group", outer.Name);
        Assert.True(outer.HasBody);
        Assert.Equal(2, outer.Children.Count);

        var inner = outer.Children[1];
        Assert.Equal("Group", inner.Name);
        Assert.True(inner.HasBody);
        Assert.Single(inner.Children);
        Assert.Equal("ZOrder", inner.Children[0].Name);
        Assert.Equal(HudArgKind.Integer, inner.Children[0].Arguments[0].Kind);
        Assert.Equal("255", inner.Children[0].Arguments[0].Raw);
    }

    [Fact]
    public void Parses_Empty_Body_As_HasBody_True()
    {
        var doc = HudParser.Parse("Group(\"empty\") { }");
        Assert.True(doc.TopLevel[0].HasBody);
        Assert.Empty(doc.TopLevel[0].Children);
    }

    [Fact]
    public void Distinguishes_Leaf_From_Empty_Body()
    {
        var doc1 = HudParser.Parse("Foo()");
        var doc2 = HudParser.Parse("Foo() { }");
        Assert.False(doc1.TopLevel[0].HasBody);
        Assert.True(doc2.TopLevel[0].HasBody);
    }

    [Fact]
    public void Parses_Identifier_Argument_As_Identifier_Kind()
    {
        // Some HUD args may be bare identifiers (e.g. enum-like flags); ensure we capture them.
        var doc = HudParser.Parse("TextBreak(Word)");
        Assert.Equal(HudArgKind.Identifier, doc.TopLevel[0].Arguments[0].Kind);
        Assert.Equal("Word", doc.TopLevel[0].Arguments[0].Raw);
    }

    [Fact]
    public void Tolerates_Missing_Closing_Brace_At_Eof()
    {
        // The parser is intentionally lenient about EOF inside a block body — stock localize.cfg
        // files ship with unbalanced braces and Pandemic's parser accepts them. We stop the block
        // silently instead of throwing. Partial content is preserved.
        var doc = HudParser.Parse("Group(\"x\") { Foo() ");
        Assert.Single(doc.TopLevel);
        var outer = doc.TopLevel[0];
        Assert.Equal("Group", outer.Name);
        Assert.True(outer.HasBody);
        Assert.Single(outer.Children);
        Assert.Equal("Foo", outer.Children[0].Name);
    }

    [Fact]
    public void Throws_On_Garbage_Argument()
    {
        Assert.Throws<HudParseException>(() => HudParser.Parse("Foo({)"));
    }
}
