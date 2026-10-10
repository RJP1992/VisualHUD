using System.Numerics;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Hud;

/// <summary>The Insert menus' blocks: every kind the game makes, each a working start, and where each may go.</summary>
public class HudBlockTemplatesTests
{
    private static BlockTemplate T(string keyword) => HudBlockTemplates.Find(keyword)!;

    private static readonly string[] Transforms =
    {
        "TransformNumberColor", "TransformNumberVector3", "TransformNumberColorBlend", "TransformNameMesh",
        "TransformNumberMath", "TransformNumberLerp", "TransformNumberCompare",
    };

    public static IEnumerable<object[]> Elements => Enum.GetValues<ElementKind>().Select(k => new object[] { k.ToString() });
    public static IEnumerable<object[]> TransformKeywords => Transforms.Select(t => new object[] { t });

    [Fact]
    public void Every_Element_Kind_And_Every_Transform_Can_Be_Inserted()
    {
        foreach (var kind in Enum.GetValues<ElementKind>())
            Assert.Equal(TemplateFamily.Element, HudBlockTemplates.Find(kind.ToString())?.Family);
        foreach (var name in Transforms)
            Assert.Equal(TemplateFamily.Transform, T(name).Family);
        Assert.Equal(TemplateFamily.File, T("FileInfo").Family);
        Assert.Equal(TemplateFamily.File, T("ViewPort").Family);
        Assert.All(HudBlockTemplates.All, t => Assert.Contains(t.Category, HudBlockTemplates.Categories));
        Assert.Equal(new[] { "TransformNumberCompare", "TransformNumberLerp", "TransformNumberMath" },
            HudBlockTemplates.All.Where(t => t.GameExt).Select(t => t.Keyword).OrderBy(k => k));
    }

    [Theory]
    [MemberData(nameof(Elements))]
    public void A_New_Element_Is_Named_And_Shows_From_The_Start(string keyword)
    {
        var block = T(keyword).Create("e", nested: false);
        Assert.True(ElementKinds.TryGet(block, out var kind));
        Assert.Equal(keyword, kind.ToString());
        Assert.Equal("e", HudRename.NameOf(block));
        var spec = ElementReader.Read(block, kind);
        Assert.NotNull(spec.EventEnable);
        Assert.Equal(1, spec.Viewport);
        // Written in the file's own indentation.
        Assert.Null(block.Trivia);
        Assert.All(block.Children, c => Assert.Null(c.Trivia));
    }

    [Theory]
    [MemberData(nameof(Elements))]
    public void Inside_A_Group_A_New_Element_Sits_At_The_Group_And_Follows_Its_Screen(string keyword)
    {
        var block = T(keyword).Create("e", nested: true);
        Assert.DoesNotContain(block.Children, c => !c.HasBody && c.Name == "Viewport");
        if (block.Children.FirstOrDefault(c => !c.HasBody && c.Name == "Position") is { } position)
        {
            Assert.Equal(0, position.Arguments[0].AsDouble());
            Assert.Equal(0, position.Arguments[1].AsDouble());
        }
        // A template keeps its own offsets.
        if (keyword == "VehicleSeating")
            Assert.Contains(block.Children.Single(c => c.Name == "Empty").Children, c => c.Name == "Position" && c.Arguments[0].Raw == "-0.017477");
    }

    [Fact]
    public void A_New_Bitmap_And_Bar_Sit_Mid_Screen()
    {
        var doc = new HudDocument();
        doc.TopLevel.Add(T("Bitmap").Create("b", nested: false));
        doc.TopLevel.Add(T("BarBitmap").Create("bar", nested: false));
        foreach (var target in new[] { HudTarget.Vanilla, HudTarget.GameExt })
        {
            var r = HudLayout.Lay(doc, new LayoutOptions { Screen = new HudScreen(1920, 1080), Target = target });
            foreach (var name in new[] { "b", "bar" })
            {
                var e = r.All.Single(x => x.Spec.Name == name);
                var q = e.Quad!.Value;
                var middle = Vector2.Transform(new Vector2((q.Left + q.Right) / 2, (q.Top + q.Bottom) / 2), e.ToScreen);
                Assert.True(MathF.Abs(middle.X - 960) < 1 && MathF.Abs(middle.Y - 540) < 1, $"{name} {target}: {middle}");
            }
        }
    }

    [Fact]
    public void Every_New_Element_Lays_Out_At_The_Top_Of_A_File_And_In_A_Group()
    {
        var doc = new HudDocument();
        doc.TopLevel.Add(T("FileInfo").Create("hud", nested: false));
        var group = T("Group").Create("holder", nested: false);
        doc.TopLevel.Add(group);
        foreach (var kind in Enum.GetValues<ElementKind>())
        {
            doc.TopLevel.Add(T(kind.ToString()).Create($"top_{kind}", nested: false));
            group.Children.Add(T(kind.ToString()).Create($"in_{kind}", nested: true));
        }
        // Written and read back, it is the same file.
        var text = HudWriter.Write(doc);
        Assert.Equal(text, HudWriter.Write(HudParser.Parse(text)));
        foreach (var target in new[] { HudTarget.Vanilla, HudTarget.GameExt })
        {
            var r = HudLayout.Lay(HudParser.Parse(text), new LayoutOptions { Screen = new HudScreen(1920, 1080), Target = target });
            foreach (var kind in Enum.GetValues<ElementKind>())
            {
                Assert.Contains(r.All, e => e.Spec.Name == $"top_{kind}");
                Assert.Contains(r.All, e => e.Spec.Name == $"in_{kind}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(TransformKeywords))]
    public void A_New_Transform_Is_Valid_And_Publishes_From_What_The_Game_Sends(string keyword)
    {
        // hudtransforms.hud declares the weapon table and the health colour the new ones read.
        var stock = HudParser.Parse("""
            ViewPort("Transforms")
            {
                EventNameFilter("player%")
                TransformNameMesh("player1weapon1")
                {
                    NameMesh("cis_weap_inf_wrist_trishot", "hud_cis_trishot")
                    EventInput("player1.weapon1.change")
                    EventOutput("player1.weapon1.mesh")
                }
                TransformNumberColor("player1healthcolor")
                {
                    NumberColor(0.0, 255, 0, 0)
                    NumberColor(1.0, 255, 255, 255)
                    EventInput("player1.healthFraction")
                    EventOutput("player1.healthColor")
                }
            }
            """);
        var doc = new HudDocument();
        doc.TopLevel.Add(T(keyword).Create("t", nested: false));
        var node = HudTransformGraph.From(new[] { stock, doc }).Nodes.Single(n => ReferenceEquals(n.Block, doc.TopLevel[0]));
        Assert.Null(node.Problem);
        Assert.Contains(node.OutputTypes, o => o.Name == "player1.t");

        HudEventValue? Latest(string name) => name switch
        {
            "player1.healthFraction" => HudEventValue.FromFloat(0.1f),
            "player1.weapon1.change" => HudEventValue.FromName("cis_weap_inf_wrist_trishot"),
            "player1.healthColor" => HudEventValue.FromColor(new HudColor(255, 255, 255)),
            _ => null,
        };
        var sent = node.Run(Latest).ToList();
        Assert.Contains(sent, s => s.Name == "player1.t");
        if (keyword == "TransformNameMesh") Assert.Equal("hud_cis_trishot", sent.Single().Value.Text);
        if (keyword == "TransformNumberMath") Assert.Equal(10f, sent.Single().Value.Float, 3);
        if (keyword == "TransformNumberCompare") Assert.Contains(sent, s => s.Value.Float == 1f);
    }

    [Fact]
    public void Elements_Go_Into_Groups_ViewPorts_And_Files_But_Not_Into_Other_Elements()
    {
        var doc = HudParser.Parse("""
            Group("g") { }
            Text("t") { }
            ViewPort("v") { }
            TransformNumberColor("c") { }
            Model3D("m") { MeshInfo("mesh") { } }
            """);
        HudBlock B(string name) => doc.TopLevel.Single(b => HudRename.NameOf(b) == name);
        var bitmap = T("Bitmap");
        Assert.Null(HudBlockTemplates.WhyNot(bitmap, null, doc));
        Assert.Null(HudBlockTemplates.WhyNot(bitmap, B("g"), doc));
        Assert.Null(HudBlockTemplates.WhyNot(bitmap, B("v"), doc));
        Assert.Contains("Text holds no elements", HudBlockTemplates.WhyNot(bitmap, B("t"), doc));
        Assert.NotNull(HudBlockTemplates.WhyNot(bitmap, B("c"), doc));
        Assert.NotNull(HudBlockTemplates.WhyNot(bitmap, B("m").Children[0], doc));
    }

    [Fact]
    public void Every_Group_Based_Element_Holds_Elements_And_No_Other_Element_Does()
    {
        // Map, Target, MultilineText and VehicleSeating read their own templates, then hand any other
        // block to ElementGroupBase::ReadChild like a group.
        foreach (var kind in Enum.GetValues<ElementKind>())
        {
            var why = HudBlockTemplates.WhyNothingFits(T(kind.ToString()).Create("x", nested: false));
            Assert.True(ElementKinds.IsGroupBased(kind) ? why is null : why is not null, $"{kind}: {why}");
        }
    }

    [Fact]
    public void Transforms_Go_At_The_Top_Of_A_File_Or_In_A_ViewPort()
    {
        var doc = HudParser.Parse("""
            Group("g") { }
            ViewPort("v") { }
            """);
        foreach (var keyword in Transforms)
        {
            Assert.Null(HudBlockTemplates.WhyNot(T(keyword), null, doc));
            Assert.Null(HudBlockTemplates.WhyNot(T(keyword), doc.TopLevel[1], doc));
            Assert.NotNull(HudBlockTemplates.WhyNot(T(keyword), doc.TopLevel[0], doc));
        }
    }

    [Fact]
    public void A_File_Has_One_FileInfo_At_Its_Top_And_A_ViewPort_Goes_At_The_Top()
    {
        var doc = HudParser.Parse("Group(\"g\") { }");
        Assert.Null(HudBlockTemplates.WhyNot(T("FileInfo"), null, doc));
        Assert.NotNull(HudBlockTemplates.WhyNot(T("FileInfo"), doc.TopLevel[0], doc));
        Assert.Null(HudBlockTemplates.WhyNot(T("ViewPort"), null, doc));
        Assert.NotNull(HudBlockTemplates.WhyNot(T("ViewPort"), doc.TopLevel[0], doc));

        doc.TopLevel.Insert(0, T("FileInfo").Create("hud", nested: false));
        Assert.Contains("FileInfo already", HudBlockTemplates.WhyNot(T("FileInfo"), null, doc));
    }

    [Fact]
    public void New_Names_Do_Not_Repeat_And_An_ObjectiveList_Is_Named_For_A_Player()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "new_group", "New_Group_1", "player1objectives" };
        Assert.Equal("new_group_2", T("Group").NewName(taken.Contains));
        Assert.Equal("new_text", T("Text").NewName(taken.Contains));
        Assert.Equal("player2objectives", T("ObjectiveList").NewName(taken.Contains));
        Assert.Null(T("ObjectiveList").NewName(_ => true));

        // Its events follow its player.
        var list = T("ObjectiveList").Create("player2objectives", nested: false);
        Assert.Contains(list.Children, c => c.Name == "EventEnable" && c.Arguments[0].Raw == "player2.objectivelist.enable");
        Assert.Contains(list.Children, c => c.Name == "Viewport" && c.Arguments[0].Raw == "2");
    }
}
