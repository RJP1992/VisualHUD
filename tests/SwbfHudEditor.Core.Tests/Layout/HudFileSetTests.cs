using System.Numerics;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>
/// Several files loaded together, as the game loads a HUD's files additively: one set of screen
/// groups, events and transforms shared by all, and each file laid out by its own <c>FileInfo</c>.
/// </summary>
public class HudFileSetTests
{
    private static readonly HudScreen Hd = new(1280, 720);

    private static bool Near(float a, float b, float tolerance = 1e-2f) => MathF.Abs(a - b) <= tolerance;

    private static LaidOutElement Named(HudLayoutResult r, string name) => r.All.Single(e => e.Spec.Name == name);

    private static float Height(LaidOutElement e)
    {
        var q = e.Quad!.Value;
        var a = Vector2.Transform(new Vector2(q.Left, q.Top), e.ToScreen);
        var b = Vector2.Transform(new Vector2(q.Right, q.Bottom), e.ToScreen);
        return MathF.Abs(b.Y - a.Y);
    }

    private static string Square(string name) => $$"""
        Bitmap("{{name}}")
        {
            Position(0.5, 0.5, 0, "Viewport")
            BitmapRect(0.1, 0.177778, "Center", "Center", "Viewport")
        }
        """;

    [Fact]
    public void Each_File_Keeps_Its_Own_FileInfo()
    {
        // GameExt lays out the AuthoredRatio file on its ratio while the stock file loaded with it keeps
        // the stock layout: the same numbers draw square in one and stretched in the other.
        var opted = HudParser.Parse("FileInfo(\"a\") { AuthoredRatio(16, 9) }\n" + Square("a"));
        var stock = HudParser.Parse("FileInfo(\"b\") { Viewports(1) }\n" + Square("b"));
        var docs = new[] { opted, stock };
        var r = HudLayout.Lay(docs, new LayoutOptions { Screen = Hd, Target = HudTarget.GameExt, Rounding = false });

        Assert.True(r.LayoutOf(opted)!.Opted);
        Assert.False(r.LayoutOf(stock)!.Opted);
        Assert.Same(opted, Named(r, "a").Document);
        Assert.Same(stock, Named(r, "b").Document);
        Assert.Equal(PieceRole.Piece, Named(r, "a").Role);
        Assert.Equal(PieceRole.None, Named(r, "b").Role);
        Assert.True(Near(Height(Named(r, "a")), 128), $"a {Height(Named(r, "a"))}");
        Assert.True(Near(Height(Named(r, "b")), 128 * 32f / 27f, 0.5f), $"b {Height(Named(r, "b"))}");

        // The stock game ignores the ratio: both stretch alike.
        var vanilla = HudLayout.Lay(docs, new LayoutOptions { Screen = Hd, Target = HudTarget.Vanilla, Rounding = false });
        Assert.True(Near(Height(Named(vanilla, "a")), Height(Named(vanilla, "b"))));
    }

    [Fact]
    public void Top_Level_Elements_Draw_In_Load_Order_Or_The_Order_Their_Events_Enabled_Them()
    {
        var first = HudParser.Parse("""Bitmap("a") { EventEnable("ev.a") }""");
        var second = HudParser.Parse("""
            Bitmap("b") { EventEnable("ev.b") }
            Bitmap("c") { EventEnable("ev.a") ZOrder(200) }
            """);
        var docs = new[] { first, second };
        string[] Order(HudSimulation? sim) => HudLayout.Lay(docs, new LayoutOptions { Screen = Hd, Simulation = sim })
            .All.OrderBy(e => e.DrawIndex).Select(e => e.Spec.Name!).ToArray();

        // As loaded: a higher z-order goes behind; equals in load order, file by file, the later on top.
        Assert.Equal(new[] { "c", "a", "b" }, Order(null));

        // b enabled before a: a joins the screen group after b, so it draws over b.
        var state = new HudEventState();
        state.Send("ev.b", HudEventValue.FromBool(true));
        state.Send("ev.a", HudEventValue.FromBool(true));
        var sim = HudSimulation.Run(docs, HudTransformGraph.From(docs), state);
        Assert.Equal(new[] { "c", "b", "a" }, Order(sim));
    }

    [Fact]
    public void Events_And_Transforms_Reach_Every_File()
    {
        // A transform file, then two files that read what it publishes and what is sent.
        var transforms = HudParser.Parse("""
            TransformNumberColor("tint") { NumberColor(0, 255, 0, 0) NumberColor(1, 0, 255, 0) EventInput("player1.healthFraction") EventOutput("player1.example.healthTint") }
            """);
        var bars = HudParser.Parse("""
            BarBitmap("health")
            {
                BitmapRect(100, 10, "Left", "Top", "Pixels")
                EventEnable("player1.healthFraction")
                EventValue("player1.healthFraction")
                EventColor("player1.example.healthTint")
            }
            """);
        var texts = HudParser.Parse("""
            Text("ammo")
            {
                EventEnable("initialize")
                EventNumber("player1.weapon1.totalClipBullets")
            }
            """);
        var docs = new[] { transforms, bars, texts };
        var graph = HudTransformGraph.From(docs);
        var state = new HudEventState();
        state.SendAll(new[]
        {
            ("initialize", HudEventValue.FromBool(true)),
            ("player1.healthFraction", HudEventValue.FromFloat(0f)),
            ("player1.weapon1.totalClipBullets", HudEventValue.FromUint(12)),
        });
        var sim = HudSimulation.Run(docs, graph, state);
        var r = HudLayout.Lay(docs, new LayoutOptions { Screen = Hd, Simulation = sim });

        var health = Named(r, "health");
        Assert.True(health.Shown);
        Assert.Equal(new Vector3(1, 0, 0), health.DrawColor);   // the other file's transform at 0
        Assert.Equal("12", Named(r, "ammo").Text);

        var uses = HudEventUsage.Of(docs, graph).Select(u => u.Name).ToList();
        Assert.Contains("player1.healthFraction", uses);
        Assert.Contains("player1.weapon1.totalClipBullets", uses);
        Assert.Contains("player1.example.healthTint", uses);
    }
}
