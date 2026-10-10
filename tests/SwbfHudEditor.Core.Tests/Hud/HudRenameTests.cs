using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Tests.Hud;

/// <summary>Renaming a block, with the lines that find it by name, as one undoable edit.</summary>
public class HudRenameTests
{
    [Fact]
    public void A_Model_Takes_The_InheritMeshInfo_Lines_That_Name_It_Along()
    {
        var weapons = HudParser.Parse("""
            Model3D("weapon")
            {
                Mesh("hud_rifle")
                MeshInfo("hud_rifle")
                {
                    Position(0.0, 0.0, 0.0, "Viewport")
                }
            }
            """);
        var copies = HudParser.Parse("""
            Group("g")
            {
                Model3D("copy")
                {
                    InheritMeshInfo("WEAPON")
                }
                Model3D("other")
                {
                    InheritMeshInfo("someone_else")
                }
            }
            """);
        var model = weapons.TopLevel[0];
        var inherit = copies.TopLevel[0].Children[0].Children[0];
        var plan = HudRename.Plan(new[] { weapons, copies }, model, "rifle");
        plan.Edit.Apply();
        Assert.Equal("rifle", HudRename.NameOf(model));
        Assert.Equal("rifle", inherit.Arguments[0].Raw);
        Assert.Equal("someone_else", copies.TopLevel[0].Children[1].Children[0].Arguments[0].Raw);
        Assert.Contains(plan.Notes, n => n.Contains("1 InheritMeshInfo line"));

        plan.Edit.Revert();
        Assert.Equal("weapon", HudRename.NameOf(model));
        Assert.Equal("WEAPON", inherit.Arguments[0].Raw);
    }

    [Fact]
    public void A_NameMesh_Transform_Takes_The_Tables_That_Copy_It_Along()
    {
        var doc = HudParser.Parse("""
            TransformNameMesh("player1weapon1")
            {
                NameMesh("a", "b")
            }
            TransformNameMesh("mine")
            {
                TransformNameMesh("player1weapon1")
                EventInput("player1.weapon1.change")
            }
            """);
        HudRename.Plan(new[] { doc }, doc.TopLevel[0], "weapons").Edit.Apply();
        Assert.Equal("weapons", doc.TopLevel[1].Children[0].Arguments[0].Raw);
    }

    [Fact]
    public void A_Name_Another_Element_Has_Is_Pointed_Out()
    {
        var doc = HudParser.Parse("""
            Text("a") { }
            Bitmap("b") { }
            """);
        doc.SourcePath = @"C:\mod\common_hud.hud";
        var notes = HudRename.Plan(new[] { doc }, doc.TopLevel[0], "B").Notes;
        Assert.Contains(notes, n => n.Contains("Bitmap \"b\" in common_hud.hud has this name too"));
        Assert.Empty(HudRename.Plan(new[] { doc }, doc.TopLevel[0], "c").Notes);
    }

    [Fact]
    public void An_ObjectiveList_Not_Named_For_A_Player_Is_Pointed_Out()
    {
        var doc = HudParser.Parse("ObjectiveList(\"player1objectives\") { }");
        Assert.Contains(HudRename.Plan(new[] { doc }, doc.TopLevel[0], "objectives").Notes, n => n.Contains("playerNobjectives"));
        Assert.Empty(HudRename.Plan(new[] { doc }, doc.TopLevel[0], "player2objectives").Notes);
    }

    [Fact]
    public void Templates_The_Game_Finds_By_Keyword_Keep_Their_Names()
    {
        var doc = HudParser.Parse("""
            BarSegmented("ring")
            {
                NumSegments(10)
                Segment("Segment") { }
            }
            Map("map")
            {
                BackdropSmall("BackdropSmall") { }
                Text("label") { }
            }
            Model3D("m")
            {
                MeshInfo("hud_rifle") { }
            }
            """);
        var ring = doc.TopLevel[0];
        var map = doc.TopLevel[1];
        var model = doc.TopLevel[2];
        Assert.Null(HudRename.WhyNot(ring, null));
        Assert.Contains("keyword, Segment", HudRename.WhyNot(ring.Children[1], ring));
        Assert.NotNull(HudRename.WhyNot(map.Children[0], map));
        Assert.Null(HudRename.WhyNot(map.Children[1], map));            // an element in the map is a child like any
        Assert.Null(HudRename.WhyNot(model.Children[0], model));        // a MeshInfo's name is the model it places
        Assert.NotNull(HudRename.WhyNot(ring.Children[0], ring));       // a line
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\"b")]
    [InlineData("a\nb")]
    public void Names_That_Cannot_Be_Written_Are_Refused(string name) => Assert.NotNull(HudRename.Invalid(name));

    [Fact]
    public void A_Rename_Tells_The_Tree_And_Saves_With_The_Comments_Kept()
    {
        var doc = HudParser.Parse("// the bar\nBarBitmap( \"old\" ) // odd spacing\n{\n    Value(0.5)\n}\n");
        var bar = doc.TopLevel[0];
        string? changed = null;
        bar.PropertyChanged += (_, e) => changed = e.PropertyName;

        var edit = HudRename.Plan(new[] { doc }, bar, "new").Edit;
        edit.Apply();
        Assert.Equal(nameof(HudBlock.Header), changed);
        Assert.Equal("BarBitmap(\"new\") { ... }", bar.Header);
        Assert.Equal("// the bar\nBarBitmap(\"new\") // odd spacing\n{\n    Value(0.5)\n}\n", HudWriter.Write(doc));
        Assert.Equal("rename BarBitmap \"old\" to \"new\"", edit.Description);

        // Undone, the line is as it was written.
        edit.Revert();
        Assert.Equal("// the bar\nBarBitmap( \"old\" ) // odd spacing\n{\n    Value(0.5)\n}\n", HudWriter.Write(doc));
    }
}
