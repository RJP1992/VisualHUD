using System.Numerics;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>A Model3D's size and place as the game draws it, and which matrix and depth it is drawn with.</summary>
public class ModelProjectionTests
{
    private static readonly HudScreen FullHd = new(1920, 1080);

    private static HudLayoutResult Lay(string source, HudScreen screen, HudTarget target = HudTarget.Vanilla, HudEventState? sent = null)
    {
        var doc = HudParser.Parse(source);
        return HudLayout.Lay(doc, new LayoutOptions
        {
            Screen = screen,
            Target = target,
            Rounding = false,
            Simulation = sent is null ? null : HudSimulation.Run(doc, HudTransformGraph.From(new[] { doc }), sent),
            ShowByEvents = false,
        });
    }

    private static LaidOutElement Named(HudLayoutResult r, string name) => r.All.Single(e => e.Spec.Name == name);

    private static Vector3 Project(LaidOutElement e, HudScreen screen, Vector3 vertex) =>
        ModelProjection.Project(vertex, ModelProjection.MeshToView(e.World, e.MeshDepth, screen), screen)!.Value;

    private const string Centred = """
        Model3D("m")
        {
            Mesh("hud_weapon_backdrop")
            Position(0.5, 0.5, 0, "Viewport")
        }
        """;

    [Fact]
    public void A_Mesh_Unit_Is_W_Over_6_4_Pixels_Across_And_The_Band_Takes_Its_Height()
    {
        // The worked example: 1920x1080, stock, element at the centre of the screen, depth 0.01.
        var e = Named(Lay(Centred, FullHd), "m");
        Assert.Equal(0.01f, e.MeshDepth);
        var origin = Project(e, FullHd, Vector3.Zero);
        Assert.Equal(960f, origin.X, 0.05f);
        Assert.Equal(540f, origin.Y, 0.05f);
        Assert.Equal(1.8475f, origin.Z, 0.001f);
        Assert.Equal(1260f, Project(e, FullHd, Vector3.UnitX).X, 0.1f);           // 300 px: W / 6.4
        Assert.Equal(540f - 266.67f, Project(e, FullHd, Vector3.UnitY).Y, 0.1f);  // 300 x 8/9, the band
    }

    [Fact]
    public void Nearer_Vertices_Are_Drawn_Larger_And_Those_Past_The_Near_Plane_Not_At_All()
    {
        var e = Named(Lay(Centred, FullHd), "m");
        Assert.Equal(1371.3f, Project(e, FullHd, new Vector3(1, 0, 0.5f)).X, 0.2f);
        Assert.Equal(1196.1f, Project(e, FullHd, new Vector3(1, 0, -0.5f)).X, 0.2f);
        var view = ModelProjection.MeshToView(e.World, e.MeshDepth, FullHd);
        Assert.Null(ModelProjection.Project(new Vector3(0, 0, 0.9f), view, FullHd));
    }

    [Fact]
    public void A_Lower_Resolution_Draws_The_Same_Share_Of_The_Screen()
    {
        var hd = new HudScreen(1280, 720);
        var e = Named(Lay(Centred, hd), "m");
        Assert.Equal(640f + 200f, Project(e, hd, Vector3.UnitX).X, 0.1f);
        Assert.Equal(360f - 177.78f, Project(e, hd, Vector3.UnitY).Y, 0.1f);
    }

    [Fact]
    public void A_File_Laid_Out_By_GameExt_Draws_Units_Square()
    {
        var e = Named(Lay("FileInfo(\"f\") { AuthoredRatio(16, 9) }\n" + Centred, FullHd, HudTarget.GameExt), "m");
        Assert.Equal(540f - 300f, Project(e, FullHd, Vector3.UnitY).Y, 0.1f);
        Assert.Equal(1260f, Project(e, FullHd, Vector3.UnitX).X, 0.1f);
    }

    [Fact]
    public void A_Depth_Line_Sets_The_Size_Until_A_Sent_Mesh_Puts_It_Back()
    {
        const string source = """
            Model3D("m")
            {
                Mesh("hud_weapon_backdrop")
                EventMesh("player1.weapon1.mesh")
                Position(0.5, 0.5, 0, "Viewport")
                Depth(0.02)
            }
            """;
        var e = Named(Lay(source, FullHd), "m");
        Assert.Equal(0.02f, e.MeshDepth);
        Assert.Equal(960f + 150f, Project(e, FullHd, Vector3.UnitX).X, 0.1f);

        var sent = new HudEventState();
        sent.Send("player1.weapon1.mesh", HudEventValue.FromModel("hud_weapon2_backdrop"));
        var after = Named(Lay(source, FullHd, sent: sent), "m");
        Assert.Equal("hud_weapon2_backdrop", after.Mesh);
        Assert.Equal(ModelProjection.DefaultDepth, after.MeshDepth);
    }

    private const string WithMeshInfo = """
        Model3D("m")
        {
            MESHLINE
            Position(0.5, 0.5, 0, "Viewport")
            MeshInfo("hud_stealth")
            {
                Position(0.25, 0.25, 0, "Viewport")
                Scale(0.5, 0.5, 0.5)
                Depth(0.05)
            }
        }
        """;

    [Fact]
    public void A_MeshInfo_Places_A_Mesh_An_Event_Sends_But_Not_The_Mesh_Line()
    {
        // The Mesh line runs SetMesh before the table is read: the element keeps its own matrix.
        var line = Named(Lay(WithMeshInfo.Replace("MESHLINE", "Mesh(\"hud_stealth\")"), FullHd), "m");
        Assert.Null(line.MeshInfo);
        Assert.Equal(960f, Vector2.Transform(Vector2.Zero, line.ToScreen).X, 0.1f);

        // Sent by its event, the MeshInfo replaces the matrix and the depth.
        var sent = new HudEventState();
        sent.Send("player1.weapon1.mesh", HudEventValue.FromModel("hud_stealth"));
        var e = Named(Lay(WithMeshInfo.Replace("MESHLINE", "EventMesh(\"player1.weapon1.mesh\")"), FullHd, sent: sent), "m");
        Assert.NotNull(e.MeshInfo);
        Assert.Equal(0.05f, e.MeshDepth);
        Assert.Equal(480f, Vector2.Transform(Vector2.Zero, e.ToScreen).X, 0.1f);
    }

    [Fact]
    public void A_Shared_MeshInfo_Table_Is_Converted_Once_For_Each_Model_That_Reads_It()
    {
        const string source = """
            Model3D("owner")
            {
                EventMesh("player1.weapon1.mesh")
                MeshInfo("hud_stealth") { Position(0.5, 0.25, 0, "Viewport") }
                MeshInfo("hud_vehicle_lasers") { Position(100, 50, 0, "Pixels") }
            }
            Model3D("heir")
            {
                InheritMeshInfo("owner")
                EventMesh("player1.weapon2.mesh")
            }
            """;
        var sent = new HudEventState();
        sent.Send("player1.weapon1.mesh", HudEventValue.FromModel("hud_stealth"));
        sent.Send("player1.weapon2.mesh", HudEventValue.FromModel("hud_vehicle_lasers"));
        var r = Lay(source, FullHd, sent: sent);

        // The heir reads the owner's table.
        Assert.Equal("hud_vehicle_lasers", Named(r, "heir").MeshInfo?.Mesh);
        Assert.Equal(100f, Named(r, "heir").Local.M41, 0.01f);
        // The owner's viewport share was turned into pixels twice: 0.5 x 1920 x 1920.
        Assert.Equal(0.5f * 1920f * 1920f, Named(r, "owner").Local.M41, 1f);
    }
}
