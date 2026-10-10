using System.Numerics;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>The pieces the game builds in code for the six code-driven elements, where their code puts them.</summary>
public class CodePiecesTests
{
    private static readonly HudScreen Hd = new(1280, 720);

    /// <summary>Every character 5 pixels wide; gamefont_small cells 14 tall, every other font 10.</summary>
    private sealed class TestFont(int height) : ITextFont
    {
        public int Height => height;
        public int Ascent => height - 2;
        public FontGlyph? Find(char c) => new FontGlyph(c, 5, 0, 0, c == ' ' ? 0 : 4, height);
    }

    private static HudLayoutResult Lay(string source, HudEventState? sent = null, (int, int)? texture = null)
    {
        var doc = HudParser.Parse(source);
        return HudLayout.Lay(doc, new LayoutOptions
        {
            Screen = Hd,
            Target = HudTarget.Vanilla,
            Rounding = false,
            Fonts = name => new TestFont(name == "gamefont_small" ? 14 : 10),
            TextureSize = _ => texture ?? (128, 128),
            Simulation = sent is null ? null : HudSimulation.Run(doc, HudTransformGraph.From(new[] { doc }), sent),
            ShowByEvents = false,
        });
    }

    private static LaidOutElement Named(HudLayoutResult r, string name) => r.All.Single(e => e.Piece is null && e.Spec.Name == name);

    private static List<LaidOutElement> Pieces(LaidOutElement e) => e.Children.Where(c => c.Piece is not null).ToList();

    private static Vector2 At(LaidOutElement e) => new(e.Local.M41, e.Local.M42);

    [Fact]
    public void A_BorderedBox_Is_Nine_Cells_With_The_Border_Outside_Its_Rect()
    {
        var r = Lay("""
            BorderedBox("box")
            {
                Background("border_3_pieces")
                Border(2, 2)
                Rect(100, 50, "Left", "Top", "Pixels")
            }
            """, texture: (128, 64));
        var cells = Pieces(Named(r, "box"));
        Assert.Equal(9, cells.Count);
        Assert.All(cells, c => Assert.Equal("border_3_pieces", c.Texture));
        var centre = cells[0];
        Assert.Equal(new QuadRect(0, 0, 100, 50), centre.Quad);
        Assert.Equal(2f / 128f, centre.Uv.U0, 5);
        Assert.Equal(2f / 64f, centre.Uv.V0, 5);
        // The top-left corner sits outside the Rect, a Border wide.
        Assert.Contains(cells, c => c.Quad == new QuadRect(-2, -2, 0, 0) && c.Uv == new TexRect(0, 0, 2f / 128f, 2f / 64f));
        Assert.Contains(cells, c => c.Quad == new QuadRect(100, 50, 102, 52));
        // A click on any of them picks the box.
        Assert.All(cells, c => Assert.Same(Named(r, "box").Block, c.PickBlock));
    }

    [Fact]
    public void An_ObjectiveList_Stacks_Its_Objectives_8_In_And_2_Apart_And_Drops_The_Oldest_That_Do_Not_Fit()
    {
        var r = Lay("""ObjectiveList("player1objectives") { Rect(400, 200, "Left", "Top", "Pixels") }""");
        var lines = Pieces(Named(r, "player1objectives"));
        Assert.Equal(3, lines.Count);
        Assert.Equal(new Vector2(8, 8), At(lines[0]));
        Assert.Equal(new Vector2(8, 20), At(lines[1]));    // one 10-pixel line, then 2
        Assert.Equal(CodeSamples.Objectives[0].Text, lines[0].Text);
        Assert.Equal(new Vector3(128, 128, 128) / 255f, lines[0].DrawColor);   // completed
        Assert.Equal(new Vector3(223, 223, 32) / 255f, lines[1].DrawColor);    // active
        Assert.Equal(384, lines[0].TextBoxWidth);

        // Room for one line: only the newest shows.
        var small = Pieces(Named(Lay("""ObjectiveList("player1objectives") { Rect(400, 20, "Left", "Top", "Pixels") }"""), "player1objectives"));
        Assert.Equal(CodeSamples.Objectives[^1].Text, Assert.Single(small).Text);
    }

    private const string Messages = """
        MultilineText("messages")
        {
            EventText("player1.message")
            NumLines(8)
            ADD
            Rect(300, 100, "Left", "Top", "Pixels")
            Format("Format")
            {
                TextFont("gamefont_tiny")
                Color(223, 32, 32)
            }
        }
        """;

    [Fact]
    public void A_MultilineText_Fills_From_The_Top_Or_Keeps_The_Newest_At_The_Bottom()
    {
        var top = Pieces(Named(Lay(Messages.Replace("ADD", "AddToTop(1)")), "messages"));
        Assert.Equal(3, top.Count);
        Assert.Equal(new[] { 0f, 10f, 20f }, top.Select(l => l.Local.M42));
        Assert.Equal(new Vector3(223, 32, 32) / 255f, top[0].DrawColor);
        Assert.Equal(CodeSamples.Friend, top[1].DrawColor);       // sent after an EventColor
        Assert.All(top, l => Assert.Equal((300, 10), (l.TextBoxWidth, l.TextBoxHeight)));

        var bottom = Pieces(Named(Lay(Messages.Replace("ADD", "")), "messages"));
        Assert.Equal(new[] { 70f, 80f, 90f }, bottom.Select(l => l.Local.M42));
        Assert.Equal(CodeSamples.Messages[^1].Text, bottom[^1].Text);
    }

    [Fact]
    public void A_MultilineText_Shows_What_Its_Event_Sent()
    {
        var sent = new HudEventState();
        sent.Send("player1.message", HudEventValue.FromString("Hello there"));
        var lines = Pieces(Named(Lay(Messages.Replace("ADD", "AddToTop(1)"), sent), "messages"));
        Assert.Equal("Hello there", Assert.Single(lines).Text);
    }

    [Fact]
    public void Seats_Stack_Upward_By_Each_Names_Font_And_Their_Icons_Take_0_4_Of_The_Template()
    {
        var r = Lay("""
            VehicleSeating("seats")
            {
                Position(0.5, 0.5, 0, "Viewport")
                Empty("Empty") { Mesh("empty_icon") Scale(0.3, 0.3, 0.3) }
                Self("Self") { Mesh("self_icon") Scale(0.3, 0.3, 0.3) }
                AI("AI") { Mesh("ai_icon") Scale(0.3, 0.3, 0.3) }
                SelfText("SelfText") { TextFont("gamefont_small") Position(10, 0, 0, "Pixels") }
                AIText("AIText") { TextFont("gamefont_tiny") Position(10, 0, 0, "Pixels") }
                EmptyText("EmptyText") { TextFont("gamefont_tiny") Position(10, 0, 0, "Pixels") }
            }
            """);
        var seats = Named(r, "seats");
        // Its Position is read as pixels whatever its mode.
        Assert.Equal(new Vector2(0.5f, 0.5f), At(seats));
        var pieces = Pieces(seats);
        var icons = pieces.Where(p => p.Kind == ElementKind.Model3D).ToList();
        var names = pieces.Where(p => p.Kind == ElementKind.Text).ToList();
        Assert.Equal(new[] { "self_icon", "ai_icon", "empty_icon" }, icons.Select(i => i.Mesh));
        // Seat 0 at the origin; seat 1 one gamefont_small cell (14) up; seat 2 a gamefont_tiny cell (10) more.
        Assert.Equal(new[] { 0f, -14f, -24f }, icons.Select(i => i.Local.M42));
        Assert.Equal(0.12f, icons[0].Local.M11, 4);
        // A name is centred on its seat: half its own cell above it.
        Assert.Equal(new[] { -7f, -19f, -29f }, names.Select(n => n.Local.M42));
        Assert.Equal(10f, names[0].Local.M41);
        Assert.Equal(CodeSamples.Seats[1].Name, names[1].Text);
    }

    private const string Targets = """
        Target("t")
        {
            FourSegmentLockOn(1)
            ObjectiveOnScreen("ObjectiveOnScreen")
            {
                MinScale(1)
                MaxScale(0.5)
                Bitmap("hud_objective_icon")
                BitmapRect(0.05, 0.06, "Center", "Bottom", "Viewport")
                Position(0, -0.05, 0, "Viewport")
            }
            AttackerOffScreen("AttackerOffScreen")
            {
                BitmapRect(0.05, 0.06, "Center", "Center", "Viewport")
            }
            LockedOnScreen("LockedOnScreen")
            {
                MinScale(3)
                MaxScale(0)
                BitmapRect(0.025, 0.03, "Center", "Center", "Viewport")
            }
            HintOnScreen("HintOnScreen")
            {
                ColorFriendly(0, 86, 213)
                BitmapRect(0.05, 0.06, "Center", "Bottom", "Viewport")
            }
        }
        """;

    [Fact]
    public void A_Target_Places_Its_Markers_By_Code_At_A_Scale_For_Their_Distance()
    {
        var target = Named(Lay(Targets), "t");
        var pieces = Pieces(target);
        // ObjectiveOnScreen at ndc (-0.3, -0.1), 80 m: the near scale; its template's Position is ignored.
        var objective = pieces.Single(p => p.PickBlock.Name == "ObjectiveOnScreen");
        Assert.Equal(new Vector2(0.35f * 1280f, 0.45f * 720f), At(objective));
        Assert.Equal(1f, objective.Local.M11, 4);
        Assert.Equal("hud_objective_icon", objective.Texture);

        // Off screen: turned to point out, atan2(x, -y).
        var attacker = pieces.Single(p => p.PickBlock.Name == "AttackerOffScreen");
        var angle = MathF.Atan2(0.9f, -0.3f);
        Assert.Equal(MathF.Atan2(attacker.Local.M12, attacker.Local.M11), angle, 3);

        // Locked with four segments: four brackets, a quarter turn apart, at 1.2.
        var brackets = pieces.Where(p => p.PickBlock.Name == "LockedOnScreen").ToList();
        Assert.Equal(4, brackets.Count);
        Assert.All(brackets, b => Assert.Equal(new QuadRect(-32, -21.6f, 0, 0), b.Quad!.Value, new QuadComparer()));
        Assert.Equal(1.2f, new Vector2(brackets[0].Local.M11, brackets[0].Local.M12).Length(), 4);

        // A friendly target takes ColorFriendly.
        var hint = pieces.Single(p => p.PickBlock.Name == "HintOnScreen");
        Assert.Equal(new Vector3(0, 86, 213) / 255f, hint.DrawColor);
    }

    private sealed class QuadComparer : IEqualityComparer<QuadRect>
    {
        public bool Equals(QuadRect a, QuadRect b) =>
            MathF.Abs(a.Left - b.Left) < 0.6f && MathF.Abs(a.Top - b.Top) < 0.6f && MathF.Abs(a.Right - b.Right) < 0.6f && MathF.Abs(a.Bottom - b.Bottom) < 0.6f;
        public int GetHashCode(QuadRect q) => 0;
    }

    private const string Map = """
        Map("player1map")
        {
            PositionSmall(0.8, 0.2, 0, "Viewport")
            PositionLarge(0.5, 0.5, 0, "Viewport")
            EventChangeMapMode("player1.map.mode")
            Position(0.3, 0.4, 0, "Viewport")
            BackdropSmall("BackdropSmall") { Mask("map_mask") }
            BackdropLarge("BackdropLarge") { Mask("map_mask") }
            NorthSmall("NorthSmall") { Bitmap("hud_map_northpointer") Scale(2, 2, 2) }
            PostSmall("PostSmall") { }
            PostTextSmall("PostTextSmall") { TextAlignment("Center", "Bottom") }
            TargetSmall("TargetSmall") { Color(0, 0, 0) }
        }
        """;

    [Fact]
    public void The_Minimap_Sits_At_PositionSmall_With_Its_Backdrop_North_Pointer_And_Posts()
    {
        var map = Named(Lay(Map), "player1map");
        // At 16:9 modtools stretches the map group 1 + (4/3 - 1)/4 tall.
        Assert.Equal(new Vector2(0.8f * 1280f, 0.2f * 720f), At(map));
        Assert.Equal(1f + (4f / 3f - 1f) / 4f, map.Local.M22, 4);

        var pieces = Pieces(map);
        var side = 0.22f * 720f;
        var backdrop = pieces.Single(p => p.Piece!.Role == CodePieceRole.MapBackdrop);
        Assert.Equal(side, backdrop.Quad!.Value.Width, 3);
        Assert.Equal("map_mask", backdrop.Mask);
        Assert.Equal(0.5f, backdrop.DrawAlpha);
        Assert.Equal(255, backdrop.Piece!.ZOrder);

        var north = pieces.Single(p => p.PickBlock.Name == "NorthSmall");
        Assert.Equal(new Vector2(0, -side / 2f), At(north));
        Assert.Equal(2f, north.Local.M11);

        // The third post lies outside the minimap's 125 m: pinned to the rim, and its number faded out.
        var posts = pieces.Where(p => p.PickBlock.Name == "PostSmall").ToList();
        Assert.Equal(3, posts.Count);
        Assert.Equal(side / 2f, At(posts[2]).Length(), 2);
        Assert.Equal(CodeSamples.Enemy, posts[1].DrawColor);
        var numbers = pieces.Where(p => p.PickBlock.Name == "PostTextSmall").ToList();
        Assert.Equal("1", numbers[0].Text);
        Assert.Equal(At(posts[0]).Y - 5f, At(numbers[0]).Y, 3);
        Assert.Equal(0f, numbers[2].DrawAlpha, 3);

        // Units: the friend always, the enemy while it fires, in white.
        var units = pieces.Where(p => p.PickBlock.Name == "TargetSmall").ToList();
        Assert.Equal(2, units.Count);
        Assert.Equal(Vector3.One, units[1].DrawColor);
    }

    [Fact]
    public void A_Sent_Map_Mode_Shows_The_Large_Map()
    {
        var sent = new HudEventState();
        sent.Send("player1.map.mode", HudEventValue.FromInt(1));
        var map = Named(Lay(Map, sent), "player1map");
        Assert.Equal(new Vector2(640, 360), At(map));
        var backdrop = Pieces(map).Single(p => p.Piece!.Role == CodePieceRole.MapBackdrop);
        Assert.Equal(0.75f * 720f, backdrop.Quad!.Value.Width, 3);
        Assert.Equal(TexRect.Full, backdrop.Uv);
    }

    [Fact]
    public void Pieces_Join_Their_Elements_Children_By_Their_Own_ZOrder()
    {
        var r = Lay("""
            BorderedBox("box")
            {
                Background("b")
                Border(2, 2)
                Rect(100, 50, "Left", "Top", "Pixels")
                Text("over") { ZOrder(0) }
                Text("under") { ZOrder(200) }
            }
            """);
        var order = r.All.OrderBy(e => e.DrawIndex).Select(e => e.Piece is null ? e.Spec.Name : "cell").ToList();
        // A lower ZOrder draws later, on top: the higher child, then the cells (128), then the lower one.
        Assert.True(order.IndexOf("under") < order.IndexOf("cell"));
        Assert.True(order.LastIndexOf("cell") < order.IndexOf("over"));
    }
}
