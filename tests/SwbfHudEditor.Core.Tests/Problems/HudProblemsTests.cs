using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Problems;

namespace SwbfHudEditor.Core.Tests.Problems;

/// <summary>The Problems tab's checks: each finds what it is for, and the stock way of writing a thing is no problem.</summary>
public class HudProblemsTests
{
    private static List<HudProblem> Check(string text, Func<HudAssetKind, string, bool>? missing = null)
    {
        var document = HudParser.Parse(text);
        return HudProblems.Check(new[] { document }, new[] { document }, missing);
    }

    /// <summary>The problems of the line (or block, without one) named so, in the element named so.</summary>
    private static List<HudProblem> At(List<HudProblem> problems, string element, string? line = null) =>
        problems.Where(p => p.Block.ItemName == element || p.Block.Name == element)
            .Where(p => line is null ? p.Line is null : p.Line?.Name == line)
            .ToList();

    [Fact]
    public void Units_The_Game_Reads_Otherwise_Are_Pointed_Out()
    {
        var problems = Check("""
            Bitmap("a")
            {
                Position(10, 10, 0, "Viewpor")
                BitmapRect(10, 10, "Left", "Top", "Pixels", 1)
            }
            Bitmap("b")
            {
                Position(0.5, 0.5, 0, "Viewport")
                BitmapRect(10, 10, "Left", "Top", "Pixel")
            }
            Group("c")
            {
                Rect(1, 1, "Left", "Top", "Frame")
            }
            Bitmap("fine")
            {
                Position(0.5, 0.5, 0, "ViewportX")
                BitmapRect(10, 10, "Left", "Top", "Pixels")
            }
            """);
        Assert.Contains("as Pixels", Assert.Single(At(problems, "a", "Position")).Message);
        Assert.Contains("exactly five", Assert.Single(At(problems, "a", "BitmapRect")).Message);
        Assert.Contains("\"Pixel\"", Assert.Single(At(problems, "b", "BitmapRect")).Message);
        Assert.Contains("keeps Viewport", Assert.Single(At(problems, "c", "Rect")).Message);
        // A name the unit starts with is read as that unit.
        Assert.Empty(problems.Where(p => p.Block.ItemName == "fine"));
    }

    [Fact]
    public void A_Line_Or_Block_The_Game_Reads_Nothing_Of_Is_Pointed_Out()
    {
        var problems = Check("""
            MeshInfo("loose")
            {
                Position(0, 0, 0)
            }
            Text("label")
            {
                Colour(255, 0, 0)
                Bitmap("inside")
                {
                    BitmapRect(1, 1)
                }
            }
            Group("g")
            {
                TransformNumberColor("inGroup")
                {
                }
            }
            """);
        Assert.Equal(ProblemLevel.Warning, Assert.Single(At(problems, "MeshInfo")).Level);
        Assert.Contains("reads no Colour line", Assert.Single(At(problems, "label", "Colour")).Message);
        Assert.Equal(ProblemLevel.Error, Assert.Single(At(problems, "inside")).Level);
        Assert.Equal(ProblemLevel.Note, Assert.Single(At(problems, "inGroup")).Level);
    }

    [Fact]
    public void Models_Inherited_Tables_And_Their_Units()
    {
        var problems = Check("""
            Model3D("first")
            {
                Position(0, 0, 0)
                Mesh("hud_a")
                BlendMode("Additive")
                MeshInfo("hud_a")
                {
                    Position(0.5, 0.5, 0, "Viewport")
                }
            }
            Model3D("heir")
            {
                Mesh("hud_a")
                InheritMeshInfo("first")
            }
            Model3D("orphan")
            {
                Mesh("hud_a")
                InheritMeshInfo("later")
            }
            Model3D("later")
            {
                Mesh("hud_a")
            }
            """);
        Assert.Contains("before the Mesh", Assert.Single(At(problems, "first", "Position")).Message);
        Assert.Equal(ProblemLevel.Note, Assert.Single(At(problems, "first", "BlendMode")).Level);
        Assert.Contains("twice", Assert.Single(At(problems, "heir", "InheritMeshInfo")).Message);
        Assert.Contains("made before", Assert.Single(At(problems, "orphan", "InheritMeshInfo")).Message);
    }

    [Fact]
    public void Elements_That_Read_Their_Own_Way()
    {
        var problems = Check("""
            VehicleSeating("seats")
            {
                Position(0.5, 0.5, 0, "Viewport")
            }
            Target("targets")
            {
                ObjectiveOnScreen("ObjectiveOnScreen")
                {
                    Position(0, 0, 0)
                    Bitmap("hud_target")
                }
            }
            MultilineText("messages")
            {
                Format("Format")
                {
                    TextBox(0.5, 0.1)
                }
            }
            ObjectiveList("objectives")
            {
            }
            ObjectiveList("player1objectives")
            {
            }
            """);
        Assert.Contains("as pixels", Assert.Single(At(problems, "seats", "Position")).Message);
        Assert.Equal(ProblemLevel.Note, Assert.Single(At(problems, "ObjectiveOnScreen", "Position")).Level);
        Assert.Contains("nothing shows", Assert.Single(At(problems, "messages")).Message);
        Assert.Single(At(problems, "Format", "TextBox"));
        Assert.Single(At(problems, "objectives"));
        Assert.Empty(At(problems, "player1objectives"));
    }

    [Fact]
    public void A_Groups_BlendMode_Reaches_Only_The_Children_Before_It()
    {
        var problems = Check("""
            Group("before")
            {
                BlendMode("Additive")
                Bitmap("a") { }
            }
            Group("between")
            {
                Bitmap("b") { }
                BlendMode("Additive")
                Bitmap("c") { }
            }
            Group("after")
            {
                Bitmap("d") { }
                BlendMode("Additive")
            }
            """);
        Assert.Equal(ProblemLevel.Warning, Assert.Single(At(problems, "before", "BlendMode")).Level);
        Assert.Contains("the 1 before this line, not the 1 after", Assert.Single(At(problems, "between", "BlendMode")).Message);
        Assert.Empty(At(problems, "after", "BlendMode"));
    }

    [Fact]
    public void Events_Nothing_Sends_But_Each_Players_Own_Are_Sent()
    {
        var problems = Check("""
            Bitmap("custom")
            {
                EventEnable("my.custom.show")
                EventColor("my.custom.colour")
            }
            Bitmap("player2")
            {
                EventEnable("player2.spawn")
                EventDisable("player3.die")
            }
            TransformNumberColor("made")
            {
                EventInput("player1.healthFraction")
                EventOutput("my.made.colour")
            }
            Bitmap("fromTransform")
            {
                EventEnable("initialize")
                EventColor("my.made.colour")
            }
            """);
        Assert.Equal(ProblemLevel.Warning, Assert.Single(At(problems, "custom", "EventEnable")).Level);
        Assert.Equal(ProblemLevel.Note, Assert.Single(At(problems, "custom", "EventColor")).Level);
        Assert.Empty(problems.Where(p => p.Block.ItemName is "player2" or "fromTransform"));
    }

    [Fact]
    public void Names_Viewports_Assets_And_FileInfo()
    {
        var problems = Check("""
            FileInfo("file")
            {
                SplitMode("Horizontal")
                Widescreen(0)
            }
            Group("one")
            {
                Viewport(1)
                Bitmap("same")
                {
                    Viewport(2)
                    Bitmap("hud_missing")
                }
            }
            Group("two")
            {
                Viewport(1)
                Bitmap("same") { }
            }
            Group("three")
            {
                Viewport(2)
                Bitmap("same") { }
            }
            """, (kind, name) => kind == HudAssetKind.Texture && name == "hud_missing");
        Assert.Equal(ProblemLevel.Error, Assert.Single(At(problems, "FileInfo", "SplitMode")).Level);
        Assert.Contains("off", Assert.Single(At(problems, "FileInfo", "Widescreen")).Message);
        Assert.Contains("viewport 1", Assert.Single(At(problems, "same", "Viewport")).Message);
        Assert.Contains("not found", Assert.Single(At(problems, "same", "Bitmap")).Message);
        // The second "same" in viewport 1 clashes with the first; the one in viewport 2 is another player's.
        var clash = Assert.Single(problems.Where(p => p.Line is null && p.Block.ItemName == "same"));
        Assert.Same(clash.Document.TopLevel.First(b => b.ItemName == "two").Children.First(c => c.HasBody), clash.Block);
    }
}
