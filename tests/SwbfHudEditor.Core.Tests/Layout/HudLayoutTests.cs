using System.Numerics;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>Whole files laid out for a screen and target, checked where they land on screen.</summary>
public class HudLayoutTests
{
    private static readonly HudScreen Hd = new(1280, 720);

    private static bool Near(float a, float b, float tolerance = 1e-2f) => MathF.Abs(a - b) <= tolerance;

    private static HudLayoutResult Lay(string source, HudTarget target, HudScreen? screen = null, bool? rounding = false,
        Func<HudDocument, IReadOnlyDictionary<HudBlock, float>>? values = null)
    {
        var doc = HudParser.Parse(source);
        return HudLayout.Lay(doc, new LayoutOptions
        {
            Screen = screen ?? Hd,
            Target = target,
            Rounding = rounding,
            BarValues = values?.Invoke(doc),
        });
    }

    private static LaidOutElement Named(HudLayoutResult r, string name) => r.All.Single(e => e.Spec.Name == name);

    /// <summary>The quad's screen extent: (left, top, right, bottom).</summary>
    private static (float L, float T, float R, float B) OnScreen(LaidOutElement e, QuadRect? quad = null)
    {
        var q = quad ?? e.Quad!.Value;
        var a = Vector2.Transform(new Vector2(q.Left, q.Top), e.ToScreen);
        var b = Vector2.Transform(new Vector2(q.Right, q.Bottom), e.ToScreen);
        return (MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
    }

    private static Vector2 Origin(LaidOutElement e) => Vector2.Transform(Vector2.Zero, e.ToScreen);

    private const string Square16x9 = """
        Bitmap("b")
        {
            Position(0.5, 0.5, 0, "Viewport")
            BitmapRect(0.1, 0.177778, "Center", "Center", "Viewport")
        }
        """;

    [Fact]
    public void Stock_Stretches_A_Square_At_16_9()
    {
        // The same numbers drawn by the stock game: 4/3 taller, then the 8/9 band, 32/27 in all.
        var r = Lay(Square16x9, HudTarget.Vanilla, rounding: true);
        var (l, t, rr, b) = OnScreen(Named(r, "b"));
        Assert.True(Near(rr - l, 128));
        Assert.True(Near(b - t, 128 * 32f / 27f, 0.5f), $"height {b - t}");
        Assert.True(Near((l + rr) / 2, 640) && Near((t + b) / 2, 360));
    }

    [Fact]
    public void Pixels_128_By_108_Look_Square_In_Stock()
    {
        var r = Lay("""Bitmap("b") { Position(640, 360, 0) BitmapRect(128, 108, "Center", "Center", "Pixels") }""",
            HudTarget.Vanilla, rounding: true);
        var (l, t, rr, b) = OnScreen(Named(r, "b"));
        Assert.True(Near(rr - l, 128) && Near(b - t, 128));
    }

    [Fact]
    public void AuthoredRatio_Draws_As_Written_With_GameExt_Only()
    {
        var source = "FileInfo(\"f\") { AuthoredRatio(16, 9) }\n" + Square16x9;
        var ext = Lay(source, HudTarget.GameExt);
        Assert.True(ext.File.Opted && Near(ext.File.LayoutWidth, 1280));
        var (l, t, rr, b) = OnScreen(Named(ext, "b"));
        Assert.True(Near(rr - l, 128) && Near(b - t, 128));
        Assert.True(Near((l + rr) / 2, 640) && Near((t + b) / 2, 360));
        Assert.Equal(PieceRole.Piece, Named(ext, "b").Role);

        // The stock game ignores the line and stretches the file.
        var stock = Lay(source, HudTarget.Vanilla);
        Assert.False(stock.File.Opted);
        var (_, t2, _, b2) = OnScreen(Named(stock, "b"));
        Assert.True(b2 - t2 > 150);
    }

    [Fact]
    public void A_Narrower_Screen_Moves_Right_Hand_Pieces_In()
    {
        var source = """
            FileInfo("f") { AuthoredRatio(16, 9) }
            Bitmap("r") { Position(0.9, 0.1, 0, "Viewport") BitmapRect(10, 10, "Left", "Top", "Pixels") }
            Bitmap("l") { Position(0.1, 0.1, 0, "Viewport") BitmapRect(10, 10, "Left", "Top", "Pixels") }
            """;
        var r = Lay(source, HudTarget.GameExt, new HudScreen(1024, 768));
        var lw = 768f * 16f / 9f;
        var right = Named(r, "r");
        Assert.True(Near(right.Slide, 1024 - lw) && right.Third == TrueWidescreen.Anchor.Right);
        Assert.True(Near(Origin(right).X, 1024 - (lw - 0.9f * lw)));
        Assert.True(Near(Origin(Named(r, "l")).X, 0.1f * lw));
        Assert.True(Near(Origin(right).Y, 0.1f * 768));   // full height, no band
    }

    private const string FourThreeContainer = """
        FileInfo("f") { TrueWidescreen(1) }
        Group("all")
        {
            Bitmap("left") { Position(0.1, 0.5, 0, "Viewport") BitmapRect(10, 10, "Left", "Top", "Pixels") }
            Bitmap("mid") { Position(0.5, 0.5, 0, "Viewport") BitmapRect(10, 10, "Left", "Top", "Pixels") }
            Bitmap("right") { Position(0.9, 0.5, 0, "Viewport") BitmapRect(10, 10, "Left", "Top", "Pixels") }
            Bitmap("pinned") { Position(0.3, 0.5, 0, "Viewport") ScreenAnchor(0.3) BitmapRect(10, 10, "Left", "Top", "Pixels") }
        }
        """;

    [Fact]
    public void A_Plain_Container_Places_Its_Children()
    {
        var r = Lay(FourThreeContainer, HudTarget.GameExt);
        Assert.Equal(PieceRole.Container, Named(r, "all").Role);
        Assert.True(Near(Origin(Named(r, "left")).X, 96));
        Assert.True(Near(Origin(Named(r, "mid")).X, 640));
        Assert.True(Near(Origin(Named(r, "right")).X, 864 + 320));
        // ScreenAnchor(0.3): 0.3 of the layout plus 0.3 of the spare width, 30% of the screen.
        Assert.True(Near(Origin(Named(r, "pinned")).X, 384));
        Assert.True(Near(Origin(Named(r, "mid")).Y, 360));

        // On a 4:3 screen a 4:3 file is not laid out: the stock layout is its own.
        var square = Lay(FourThreeContainer, HudTarget.GameExt, new HudScreen(1024, 768));
        Assert.False(square.File.Opted);
        Assert.True(Near(Origin(Named(square, "mid")).X, 512));

        // The stock game spreads the same numbers across the real width.
        var stock = Lay(FourThreeContainer, HudTarget.Vanilla);
        Assert.True(Near(Origin(Named(stock, "left")).X, 128) && Near(Origin(Named(stock, "right")).X, 1152));
        Assert.True(Near(Origin(Named(stock, "mid")).Y, 360));
    }

    [Fact]
    public void An_Anchored_Corner_Group_Is_One_Piece_And_Targets_Stay()
    {
        var r = Lay("""
            FileInfo("f") { TrueWidescreen(1) }
            Group("anchored") { ScreenAnchor("Right") Bitmap("in") { Position(10, 10, 0) BitmapRect(4, 4, "Left", "Top", "Pixels") } }
            Target("markers") { }
            """, HudTarget.GameExt);
        var g = Named(r, "anchored");
        Assert.Equal(PieceRole.Piece, g.Role);
        Assert.True(Near(g.Slide, 320));
        Assert.True(Near(Origin(Named(r, "in")).X, 330));
        Assert.Equal(PieceRole.Target, Named(r, "markers").Role);
        Assert.Equal(0f, Named(r, "markers").Slide);
    }

    [Fact]
    public void Group_Rect_Alignment_And_Frame_Positions()
    {
        var r = Lay("""
            Group("g")
            {
                Position(0.5, 0.5, 0, "Viewport")
                Rect(0.2, 0.1, "Center", "Center")
                Bitmap("corner") { BitmapRect(4, 4, "Left", "Top", "Pixels") }
                Bitmap("centre") { Position(0.5, 0.5, 0, "Frame") BitmapRect(4, 4, "Left", "Top", "Pixels") }
            }
            """, HudTarget.GameExt);
        var g = Named(r, "g");
        Assert.True(Near(g.RectWidthPixels, 256) && Near(g.RectHeightPixels, 72));
        Assert.True(Near(g.Local.M41, 640 - 128) && Near(g.Local.M42, 360 - 36));
        var centre = Named(r, "centre");
        Assert.True(Near(centre.Local.M41, 128) && Near(centre.Local.M42, 36));
        Assert.True(Near(Origin(centre).X, 640) && Near(Origin(centre).Y, 360));
        Assert.True(Near(Origin(Named(r, "corner")).X, 512));
    }

    private const string Bar = """
        BarBitmap("bar")
        {
            Position(100, 100, 0)
            BitmapRect(200, 20, "Left", "Top", "Pixels")
            Value(0.5)
            FillFrom("Right")
        }
        """;

    [Fact]
    public void Bars_Load_Full_And_Fill_When_Sent_A_New_Value()
    {
        // At load the first fill finds the value unchanged: full.
        var load = Lay(Bar, HudTarget.Vanilla);
        Assert.Equal(new QuadRect(0, 0, 200, 20), Named(load, "bar").Quad);

        IReadOnlyDictionary<HudBlock, float> Send(HudDocument d, float v) => new Dictionary<HudBlock, float> { [d.TopLevel[0]] = v };
        var stock = Lay(Bar, HudTarget.Vanilla, values: d => Send(d, 0.25f));
        Assert.Equal(new QuadRect(0, 0, 50, 20), Named(stock, "bar").Quad);
        Assert.Equal(0.25f, Named(stock, "bar").Uv.U1);

        var right = Lay(Bar, HudTarget.GameExt, values: d => Send(d, 0.25f));
        Assert.Equal(new QuadRect(200, 0, 150, 20), Named(right, "bar").Quad);

        // Sent the value it already holds, the stock fill does nothing: still full.
        var same = Lay(Bar, HudTarget.Vanilla, values: d => Send(d, 0.5f));
        Assert.Equal(new QuadRect(0, 0, 200, 20), Named(same, "bar").Quad);

        var bottom = Lay(Bar.Replace("\"Right\"", "\"Bottom\""), HudTarget.GameExt, values: d => Send(d, 0.25f));
        Assert.Equal(new QuadRect(0, 15, 200, 20), Named(bottom, "bar").Quad);
    }

    [Fact]
    public void A_Value_Outside_The_Stock_Range_Collapses_The_Bar_At_Load()
    {
        // The first fill runs against the constructor's width of 1 before setup stores the real one.
        var r = Lay("""BarBitmap("bar") { BitmapRect(200, 20, "Left", "Top", "Pixels") MinValue(0) MaxValue(100) Value(50) }""",
            HudTarget.Vanilla);
        Assert.Equal(new QuadRect(0, 0, 1, 20), Named(r, "bar").Quad);
    }

    [Fact]
    public void Segmented_Ring_And_Row()
    {
        const string ring = """
            BarSegmented("ring")
            {
                Rect(100, 100, "Left", "Top", "Pixels")
                NumSegments(4)
                AngleStart(0)
                AngleEnd(360)
                Segment("Segment") { BitmapRect(4, 8, "Center", "Center", "Pixels") }
            }
            """;
        var stock = Named(Lay(ring, HudTarget.Vanilla), "ring");
        Assert.Equal(4, stock.Segments.Count(s => s.Lit));
        float[][] want = { new[] { 50f, 50f - 4f / 3f * 50f }, new[] { 100f, 50f }, new[] { 50f, 50f + 4f / 3f * 50f }, new[] { 0f, 50f } };
        for (var i = 0; i < 4; i++)
            Assert.True(Near(stock.Segments[i].Local.M41, want[i][0], 1e-3f) && Near(stock.Segments[i].Local.M42, want[i][1], 1e-3f),
                $"segment {i}: {stock.Segments[i].Local.M41}, {stock.Segments[i].Local.M42}");
        Assert.Equal(new QuadRect(-2, -4, 2, 4), stock.SegmentQuad);

        // Laid out by GameExt, the ring stays round.
        var opted = Named(Lay("FileInfo(\"f\") { AuthoredRatio(16, 9) }\n" + ring, HudTarget.GameExt), "ring");
        Assert.True(Near(opted.Segments[0].Local.M42, 0, 1e-3f) && Near(opted.Segments[2].Local.M42, 100, 1e-3f));

        var row = Named(Lay("""
            BarSegmented("row") { Circular(0) Rect(100, 10, "Left", "Top", "Pixels") NumSegments(3) Value(0.5) }
            """, HudTarget.Vanilla), "row");
        Assert.Equal(2, row.Segments.Count(s => s.Lit));
        Assert.True(Near(row.Segments[1].Local.M41, 100f / 3f, 1e-3f));
    }

    [Fact]
    public void Stock_Rounds_To_Whole_Units_And_GameExt_Does_Not()
    {
        const string source = """Bitmap("b") { Position(1, 1, 0) BitmapRect(4, 4, "Left", "Top", "Pixels") }""";
        var stock = Named(Lay(source, HudTarget.Vanilla, rounding: null), "b");
        Assert.True(Near(Origin(stock).X, 2));   // -319.5 units rounds to -319: two pixels at 1280 wide
        var ext = Named(Lay(source, HudTarget.GameExt, rounding: null), "b");
        Assert.True(Near(Origin(ext).X, 1));
    }

    [Fact]
    public void Only_The_Top_Elements_Viewport_Counts()
    {
        var r = Lay("""Group("g") { Viewport(2) Bitmap("b") { Viewport(1) } }""", HudTarget.GameExt);
        var b = Named(r, "b");
        Assert.Equal(2, b.Viewport);
        Assert.False(b.InActiveViewport);
    }

    [Fact]
    public void Text_Box_Is_Whole_Pixels_Of_The_View()
    {
        const string text = """Text("t") { TextBox(0.3, 0.05) }""";
        var stock = Named(Lay(text, HudTarget.GameExt), "t");
        Assert.Equal((384, 36), (stock.TextBoxWidth, stock.TextBoxHeight));
        var opted = Named(Lay("FileInfo(\"f\") { TrueWidescreen(1) }\n" + text, HudTarget.GameExt), "t");
        Assert.Equal((288, 36), (opted.TextBoxWidth, opted.TextBoxHeight));
    }

    [Fact]
    public void Colour_Always_Tints_And_Alpha_Needs_PropagateAlpha()
    {
        var r = Lay("""
            Group("g") { Color(255, 0, 255) Alpha(0.5) PropagateAlpha(1)
                Bitmap("b") { Color(255, 255, 0) Alpha(0.5) }
                Group("h") { Alpha(0.5) Bitmap("c") { Alpha(0.5) } }
            }
            """, HudTarget.GameExt);
        var b = Named(r, "b");
        Assert.Equal(new Vector3(1, 0, 0), b.DrawColor);
        Assert.Equal(0.25f, b.DrawAlpha);
        Assert.Equal(0.5f, Named(r, "c").DrawAlpha);   // h does not propagate
    }

    [Theory]
    [InlineData(HudTarget.Vanilla, "")]
    [InlineData(HudTarget.GameExt, "FileInfo(\"f\") { AuthoredRatio(16, 9) }")]
    public void Before_Events_Groups_On_One_Position_Event_Share_The_First_Ones_Point(HudTarget target, string fileInfo)
    {
        // The game sends every group on the event the same point, so before any event the editor puts
        // them all where the first one is written.
        var r = Lay(fileInfo + """
            Group("bar") { EventPosition("player1.weapon1.target.position") Position(0.5, 0.5, 0, "Viewport") }
            Group("names") { EventPosition("player1.weapon1.target.position") Position(0.5, 0.425, 0, "Viewport") }
            Group("box") { EventPosition("player1.weapon1.target.position") Position(0.1, 0.1, 0, "Viewport") Rect(64, 32, "Center", "Center", "Pixels") }
            Group("other") { Position(0.25, 0.25, 0, "Viewport") }
            """, target);
        var point = Origin(Named(r, "bar"));
        Assert.True(Vector2.Distance(point, Origin(Named(r, "names"))) < 0.01f);
        // A Pixels Rect reads the point as pixels, as it would read a sent screen fraction: 0.5 px, then its anchor.
        Assert.False(Vector2.Distance(point, Origin(Named(r, "box"))) < 1f);
        // A group on no event stays where it is written.
        var other = Origin(Named(r, "other"));
        Assert.True(MathF.Abs(other.X - 320f) < 0.5f, $"other at {other}");
    }

    /// <summary>The events sent, laid out with or without the game's showing and hiding.</summary>
    private static HudLayoutResult LaySent(HudDocument doc, HudEventState state, bool showByEvents, HudTarget target = HudTarget.Vanilla) =>
        HudLayout.Lay(doc, new LayoutOptions
        {
            Screen = Hd,
            Target = target,
            Simulation = HudSimulation.Run(doc, HudTransformGraph.From(new[] { doc }), state),
            ShowByEvents = showByEvents,
        });

    [Fact]
    public void Without_The_Games_Showing_And_Hiding_Every_Element_Shows_And_Takes_What_Is_Sent()
    {
        var doc = HudParser.Parse("""
            Group("g")
            {
                EventEnable("player1.spawn")
                BarBitmap("bar")
                {
                    EventValue("player1.healthFraction")
                    EventEnable("player1.weaponsEnable")
                    BitmapRect(100, 10, "Left", "Top", "Pixels")
                }
                Text("label")
                {
                    EventNumber("player1.weapon1.totalAmmoBullets")
                    EventEnable("player1.weapon1.totalAmmoBullets")
                }
            }
            """);
        var state = new HudEventState();
        state.Send("player1.healthFraction", HudEventValue.FromFloat(0.25f));

        // As the game: the group was never enabled, so nothing shows.
        Assert.All(LaySent(doc, state, showByEvents: true).All, e => Assert.False(e.Shown));

        var r = LaySent(doc, state, showByEvents: false);
        Assert.All(r.All, e => Assert.True(e.Shown));
        Assert.Equal(0.25f, Named(r, "bar").BarFraction, 3);
        Assert.Null(Named(r, "label").Text);   // nothing sent to it yet: the canvas shows its example
    }

    [Fact]
    public void Without_The_Games_Showing_And_Hiding_A_Sent_Position_Moves_Its_Groups_And_The_Rest_Stay_Shared()
    {
        var doc = HudParser.Parse("""
            Group("a") { EventPosition("player1.example.marker") Position(0.5, 0.5, 0, "Viewport") }
            Group("b") { EventPosition("player1.example.marker") Position(0.2, 0.3, 0, "Viewport") }
            Group("c") { EventPosition("player1.example.other") Position(0.1, 0.1, 0, "Viewport") }
            Group("d") { EventPosition("player1.example.other") Position(0.9, 0.9, 0, "Viewport") }
            """);
        var state = new HudEventState();
        var before = LaySent(doc, state, showByEvents: false);
        state.Send("player1.example.marker", HudEventValue.FromVector(new Vector3(0.25f, 0.75f, 0f)));
        var r = LaySent(doc, state, showByEvents: false);

        // Both groups on the sent event are at the sent point, away from where the first was written.
        Assert.True(Vector2.Distance(Origin(Named(r, "a")), Origin(Named(r, "b"))) < 0.01f);
        Assert.True(Vector2.Distance(Origin(Named(r, "a")), Origin(Named(before, "a"))) > 50f);
        Assert.True(MathF.Abs(Origin(Named(r, "a")).X - 0.25f * 1280f) < 1f, $"a at {Origin(Named(r, "a"))}");
        // The groups on the other event still share the first one's written point, as before any event.
        Assert.True(Vector2.Distance(Origin(Named(r, "c")), Origin(Named(r, "d"))) < 0.01f);
        Assert.True(Vector2.Distance(Origin(Named(r, "c")), Origin(Named(before, "c"))) < 0.01f);

        // As the game, a group whose event has not come is where it is written.
        var game = LaySent(doc, state, showByEvents: true);
        Assert.False(Vector2.Distance(Origin(Named(game, "c")), Origin(Named(game, "d"))) < 1f);
    }

    [Fact]
    public void A_Centred_Rect_Hangs_From_The_Shared_Point()
    {
        var r = Lay("""
            FileInfo("f") { AuthoredRatio(16, 9) }
            Group("bar") { EventPosition("player1.weapon1.target.position") Position(0.5, 0.5, 0, "Viewport") }
            Group("marker") { EventPosition("player1.weapon1.target.position") Position(0.2, 0.2, 0, "Viewport") Rect(0.1, 0.1, "Center", "Center", "Viewport") }
            """, HudTarget.GameExt);
        // The box's top-left, where the marker's children start, is half the box up and left of the point.
        Assert.Equal(new Vector2(640f - 64f, 360f - 36f), Origin(Named(r, "marker")));
    }

    [Fact]
    public void Draw_Order_Follows_The_Child_List()
    {
        // Each child joins at z-order 128 behind its equals, then its ZOrder line moves it in front of
        // its equals; the list runs high to low and the last drawn is on top.
        var r = Lay("""
            Group("g")
            {
                Bitmap("a") { ZOrder(5) }
                Bitmap("b") { ZOrder(5) }
                Bitmap("c") { }
                Bitmap("d") { ZOrder(200) }
                Bitmap("e") { ZOrder(128) }
            }
            """, HudTarget.GameExt);
        var order = r.All.OrderBy(e => e.DrawIndex).Select(e => e.Spec.Name).ToArray();
        Assert.Equal(new[] { "g", "d", "e", "c", "b", "a" }, order);
    }

    [Fact]
    public void A_Models_MeshInfo_Replaces_Its_Matrix_For_A_Mesh_Its_Event_Sends()
    {
        const string source = """
            Group("g")
            {
                Position(100, 100, 0)
                Model3D("m")
                {
                    Position(10, 10, 0)
                    Scale(2, 2, 2)
                    EventMesh("player1.weapon1.mesh")
                    MeshInfo("gun") { Position(0.5, 0.25, 0, "Viewport") }
                    MeshInfo("other") { Position(1, 1, 0) }
                }
            }
            """;
        var doc = HudParser.Parse(source);
        LaidOutElement Showing(string? mesh) =>
            HudLayout.Lay(doc, new LayoutOptions { Screen = Hd, ModelMesh = _ => mesh }).All.Single(e => e.Spec.Name == "m");

        // Before its event, the first MeshInfo's mesh stands for what it would send.
        var m = Showing(null);
        Assert.Equal("gun", m.Mesh);
        Assert.True(Near(m.Local.M41, 640) && Near(m.Local.M42, 180) && Near(m.Local.M11, 1));
        Assert.True(Near(m.LoadLocal.M41, 10) && Near(m.LoadLocal.M11, 2));
        Assert.Same(m.MeshInfo!.Block, m.PositionOwner);

        var other = Showing("OTHER");
        Assert.True(Near(other.Local.M41, 1) && Near(other.Local.M42, 1));

        var none = Showing("missing");
        Assert.Null(none.MeshInfo);
        Assert.True(Near(none.Local.M41, 10) && Near(none.Local.M11, 2));

        // A Mesh line runs SetMesh before the table is read: its mesh keeps the element's own matrix.
        var line = HudLayout.Lay(HudParser.Parse(source.Replace("EventMesh(\"player1.weapon1.mesh\")", "Mesh(\"gun\")")),
            new LayoutOptions { Screen = Hd }).All.Single(e => e.Spec.Name == "m");
        Assert.Equal("gun", line.Mesh);
        Assert.Null(line.MeshInfo);
        Assert.True(Near(line.Local.M41, 10) && Near(line.Local.M11, 2));
    }

    [Fact]
    public void Drag_Delta_Inverts_The_Draw()
    {
        var r = Lay("""
            Group("g") { Position(0.2, 0.3, 0, "Viewport") Scale(2, 2, 1)
                Bitmap("b") { Position(0.1, 0.1, 0, "Viewport") BitmapRect(4, 4, "Left", "Top", "Pixels") }
            }
            """, HudTarget.Vanilla);
        var b = Named(r, "b");
        var d = b.PositionDeltaFor(64f, 32f);
        // 64 screen pixels across under a scale of 2 are 32 group pixels, 0.025 of the width; down,
        // 32 screen pixels are 36 group pixels in the 8/9 band, then halved, 18 of 720.
        Assert.True(Near(d.X, 0.025f, 1e-5f), $"dx {d.X}");
        Assert.True(Near(d.Y, 0.025f, 1e-5f), $"dy {d.Y}");
    }
}
