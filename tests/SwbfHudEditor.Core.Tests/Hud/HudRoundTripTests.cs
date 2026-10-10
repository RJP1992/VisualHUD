using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Tests.Hud;

public class HudRoundTripTests
{
    private const string ModtoolsHudFolder = @"C:\BF2_ModTools\data\Common\hud";

    [Fact]
    public void RoundTrip_Preserves_Semantics_For_Inline_Sample()
    {
        const string src = """
            FileInfo("hudtransforms")
            {
                Viewports(1, 2, 3, 4)
            }

            ViewPort("Transforms")
            {
                EventNameFilter("player%")
                TransformNameMesh("player1weapon1")
                {
                    NameMesh("a", "b")
                    EventInput("player1.weapon1.change")
                }
            }
            """;

        var doc = HudParser.Parse(src);
        var written = HudWriter.Write(doc);
        var doc2 = HudParser.Parse(written);
        AssertSemanticEqual(doc, doc2);
    }

    [Theory]
    [InlineData("1playerhud.hud")]
    [InlineData("2playerhud.hud")]
    [InlineData("3playerhud.hud")]
    [InlineData("4playerhud.hud")]
    [InlineData("hudtransforms.hud")]
    public void RoundTrip_Preserves_Semantics_For_Stock_File(string fileName)
    {
        var path = Path.Combine(ModtoolsHudFolder, fileName);
        if (!File.Exists(path)) return; // hermetic skip when modtools aren't on this machine

        var src = File.ReadAllText(path);
        var doc = HudParser.Parse(src, sourcePath: path);
        Assert.NotEmpty(doc.TopLevel);

        var written = HudWriter.Write(doc);
        var doc2 = HudParser.Parse(written);
        AssertSemanticEqual(doc, doc2);
        // Byte for byte: comments, blank lines, spacing, indentation and line endings as they were.
        Assert.Equal(src, written);
    }

    private static void AssertSemanticEqual(HudDocument a, HudDocument b)
    {
        Assert.Equal(a.TopLevel.Count, b.TopLevel.Count);
        for (var i = 0; i < a.TopLevel.Count; i++)
            AssertBlockEqual(a.TopLevel[i], b.TopLevel[i], path: $"[{i}]");
    }

    private static void AssertBlockEqual(HudBlock a, HudBlock b, string path)
    {
        Assert.True(a.Name == b.Name, $"Name mismatch at {path}: '{a.Name}' vs '{b.Name}'");
        Assert.True(a.HasBody == b.HasBody, $"HasBody mismatch at {path}: {a.HasBody} vs {b.HasBody}");
        Assert.True(a.Arguments.Count == b.Arguments.Count, $"Arg count mismatch at {path}/{a.Name}: {a.Arguments.Count} vs {b.Arguments.Count}");
        for (var i = 0; i < a.Arguments.Count; i++)
        {
            Assert.True(a.Arguments[i].Kind == b.Arguments[i].Kind, $"Arg kind mismatch at {path}/{a.Name}[{i}]");
            Assert.True(a.Arguments[i].Raw == b.Arguments[i].Raw, $"Arg value mismatch at {path}/{a.Name}[{i}]: '{a.Arguments[i].Raw}' vs '{b.Arguments[i].Raw}'");
        }
        Assert.True(a.Children.Count == b.Children.Count, $"Child count mismatch at {path}/{a.Name}: {a.Children.Count} vs {b.Children.Count}");
        for (var i = 0; i < a.Children.Count; i++)
            AssertBlockEqual(a.Children[i], b.Children[i], path: $"{path}/{a.Name}[{i}]");
    }
}
