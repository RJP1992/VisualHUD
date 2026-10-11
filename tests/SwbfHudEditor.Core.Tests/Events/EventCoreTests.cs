using System.Numerics;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Events;

public class EventCoreTests
{
    private static bool Near(float a, float b, float tolerance = 1e-4f) => MathF.Abs(a - b) <= tolerance;

    [Theory]
    [InlineData("player1.healthFraction", 0x1440D2CFu)]
    [InlineData("Effect", 0x6E6E8D54u)]
    [InlineData("ParticleEmitter", 0xCEBB215Fu)]
    [InlineData("ScreenAnchor", 0x80D0E400u)]
    [InlineData("AuthoredRatio", 0x10FF1782u)]
    [InlineData("TrueWidescreen", 0xCA64BB16u)]
    [InlineData("Position", 0x934F4E0Au)]
    [InlineData("Rotation", 0x21AC415Fu)]
    [InlineData("Scale", 0x82971C71u)]
    [InlineData("Rect", 0xEAE44D07u)]
    [InlineData("BitmapRect", 0x625FA794u)]
    [InlineData("Bitmap", 0x46544626u)]
    [InlineData("TexCoords", 0x1FFAD2E0u)]
    [InlineData("MinValue", 0xF1A5A54Cu)]
    [InlineData("MaxValue", 0x7332C626u)]
    [InlineData("Value", 0x425ED3CAu)]
    [InlineData("EventValue", 0x8500ADD4u)]
    public void PblHash_Matches_The_Game(string text, uint hash)
    {
        Assert.Equal(hash, PblHash.Of(text));
        Assert.Equal(hash, PblHash.Of(text.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("player1.weapon2.change", "player%", "player1.weapon2.change")]
    [InlineData("player3.weapon2.change", "player%", "player1.weapon2.change")]
    [InlineData("player.healthFraction", "player%", "player1.healthFraction")]
    [InlineData("time", "player%", "time")]
    [InlineData("player1.x", null, "player1.x")]
    public void Event_Names_Pass_The_ViewPort_Filter(string name, string? filter, string expected) =>
        Assert.Equal(expected, EventNames.Filter(name, filter));

    [Fact]
    public void Native_Input_Clamp_And_Wrap()
    {
        Assert.Equal(0f, MappedNode.InputValue(-1f, 0f, 1f, false));
        Assert.Equal(1f, MappedNode.InputValue(3f, 0f, 1f, false));
        Assert.Equal(0.4f, MappedNode.InputValue(0.4f, 0f, 1f, false));
        // The wrap: -0.25 becomes +0.25 for a 0..1 table, values past the top wrap, the top stays.
        Assert.True(Near(MappedNode.InputValue(-0.25f, 0f, 1f, true), 0.25f));
        Assert.True(Near(MappedNode.InputValue(1.25f, 0f, 1f, true), 0.25f));
        Assert.True(Near(MappedNode.InputValue(1f, 0f, 1f, true), 1f));
        Assert.True(Near(MappedNode.InputValue(370f, 0f, 360f, true), 10f));
    }

    private static HudTransformGraph Graph(string source) => HudTransformGraph.From(new[] { HudParser.Parse(source) });

    private static Dictionary<string, HudEventValue> Run(HudTransformGraph g, params (string, HudEventValue)[] sends)
    {
        var state = new HudEventState();
        state.SendAll(sends);
        var sim = HudSimulation.Run(HudParser.Parse(""), g, state);
        return sim.Effective.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Number_Color_Interpolates_And_Rounds_Like_The_Game()
    {
        var g = Graph("""
            ViewPort("Transforms")
            {
                EventNameFilter("player%")
                TransformNumberColor("tint")
                {
                    NumberColor(0.00, 255, 30, 20)
                    NumberColor(0.50, 255, 190, 40)
                    NumberColor(1.00, 0, 150, 255)
                    EventInput("player1.healthFraction")
                    EventOutput("player1.example.healthTint")
                }
            }
            """);
        var r = Run(g, ("player1.healthFraction", HudEventValue.FromFloat(0.75f)));
        var c = r["player1.example.healthTint"].Color;
        // Halfway between rows 0.5 and 1.0: each channel (a + b) / 2, rounded half up.
        Assert.Equal(new HudColor(128, 170, 148, 255), c);
        r = Run(g, ("player1.healthFraction", HudEventValue.FromFloat(2f)));
        Assert.Equal(new HudColor(0, 150, 255, 255), r["player1.example.healthTint"].Color);   // held at the last row
    }

    [Fact]
    public void Vector3_With_A_Factor_Event()
    {
        var g = Graph("""
            TransformNumberVector3("spin")
            {
                NumberVector3(0.00, 0.00, 0.00, 0.00)
                NumberVector3(1.00, 0.00, 0.00, 360.00)
                InputFactor(0.25)
                WrapInput(1)
                EventInput("time")
                EventOutput("spinner")
            }
            """);
        var r = Run(g, ("time", HudEventValue.FromFloat(1f)));
        Assert.True(Near(r["spinner"].Vector.Z, 90f, 1e-3f));
        r = Run(g, ("time", HudEventValue.FromFloat(5f)));          // 1.25 wraps to 0.25
        Assert.True(Near(r["spinner"].Vector.Z, 90f, 1e-3f));
    }

    [Fact]
    public void Color_Blend_Uses_The_Integer_Formulas()
    {
        var g = Graph("""
            TransformNumberColor("base") { NumberColor(0.0, 255, 255, 255) NumberColor(1.0, 255, 255, 255) EventInput("h") EventOutput("white") }
            TransformNumberColorBlend("soft")
            {
                NumberColor(0.0, 255, 30, 20)
                NumberColor(1.0, 0, 150, 255)
                EventBlend("white")
                Alpha(0.5)
                EventInput("h")
                EventOutput("tint")
            }
            """);
        var r = Run(g, ("h", HudEventValue.FromFloat(0f)));
        // a = (byte)(0.5 * 255) = 127: (127 * 255 >> 8) + (128 * 255 >> 8) = 126 + 127.
        Assert.Equal(new HudColor(253, (byte)((127 * 30 >> 8) + (128 * 255 >> 8)), (byte)((127 * 20 >> 8) + (128 * 255 >> 8)), 255),
            r["tint"].Color);
    }

    [Fact]
    public void NameMesh_By_Hash_With_Inherited_Rows()
    {
        var g = Graph("""
            TransformNameMesh("base") { NameMesh("rep_weap_inf_rifle", "hud_rifle") EventInput("x") EventOutput("unused") }
            TransformNameMesh("weapons")
            {
                TransformNameMesh("base")
                NameMesh("rep_weap_inf_pistol", "hud_pistol")
                EventInput("player1.weapon1.change")
                EventOutput("player1.weapon1.mesh")
            }
            """);
        Assert.Equal("hud_rifle", Run(g, ("player1.weapon1.change", HudEventValue.FromName("REP_WEAP_INF_RIFLE")))["player1.weapon1.mesh"].Text);
        Assert.Equal("hud_pistol", Run(g, ("player1.weapon1.change", HudEventValue.FromName("rep_weap_inf_pistol")))["player1.weapon1.mesh"].Text);
        // No row: the model of that name.
        Assert.Equal("some_model", Run(g, ("player1.weapon1.change", HudEventValue.FromName("some_model")))["player1.weapon1.mesh"].Text);
    }

    [Fact]
    public void NameMesh_Under_GameExt_A_Table_With_The_Name_Beats_One_Without()
    {
        // Two mods' tables for one event: the first maps its weapon, the second does not know it.
        const string tables = """
            TransformNameMesh("modA") { NameMesh("moda_weap_blaster", "hud_moda_blaster") EventInput("player1.weapon1.change") EventOutput("player1.weapon1.mesh") }
            TransformNameMesh("modB") { NameMesh("modb_weap_cannon", "hud_modb_cannon") EventInput("player1.weapon1.change") EventOutput("player1.weapon1.mesh") }
            """;
        var send = ("player1.weapon1.change", HudEventValue.FromName("moda_weap_blaster"));

        // The stock game: the second table finds no row and sends the weapon's own model last.
        var stock = HudTransformGraph.From(new[] { HudParser.Parse(tables) });
        Assert.Equal("moda_weap_blaster", Run(stock, send)["player1.weapon1.mesh"].Text);

        // GameExt: the table with the row wins; the other stays quiet.
        var gameExt = HudTransformGraph.From(new[] { HudParser.Parse(tables) }, gameExt: true);
        Assert.Equal("hud_moda_blaster", Run(gameExt, send)["player1.weapon1.mesh"].Text);
        Assert.Equal("hud_modb_cannon", Run(gameExt, ("player1.weapon1.change", HudEventValue.FromName("modb_weap_cannon")))["player1.weapon1.mesh"].Text);
        // A name no table has still falls through to the model of that name.
        Assert.Equal("some_model", Run(gameExt, ("player1.weapon1.change", HudEventValue.FromName("some_model")))["player1.weapon1.mesh"].Text);
    }

    [Fact]
    public void Math_Compare_And_Lerp()
    {
        var g = Graph("""
            TransformNumberMath("lead") { Operation("Subtract") EventInputA("a") EventInputB("b") EventOutput("lead") }
            TransformNumberMath("pct") { Operation("multiply") EventInputA("lead") ConstantB(10) Clamp(0, 25) EventOutput("pct") }
            TransformNumberMath("div") { Operation("Divide") EventInputA("a") ConstantB(0) EventOutput("never") }
            TransformNumberMath("bad") { Operation("Add") ConstantA(1) ConstantA(2) ConstantB(3) EventOutput("bad") }
            TransformNumberCompare("low") { Operation("Less") EventInputA("a") ConstantB(5) EventOutput("low") EventOutputTrue("lowOn") EventOutputFalse("lowOff") }
            TransformNumberLerp("ease") { EventInput("a") InputRange(0, 10) ConstantA(1) ConstantB(0) EventOutput("ease") }
            """);
        var r = Run(g, ("a", HudEventValue.FromInt(4)), ("b", HudEventValue.FromFloat(1.5f)));
        Assert.Equal(2.5f, r["lead"].Float);
        Assert.Equal(25f, r["pct"].Float);           // clamped
        Assert.False(r.ContainsKey("never"));        // division by zero sends nothing
        Assert.False(r.ContainsKey("bad"));          // a repeated operand switches it off
        Assert.Equal(1f, r["low"].Float);
        Assert.True(r.ContainsKey("lowOn") && !r.ContainsKey("lowOff"));
        Assert.True(Near(r["ease"].Float, 0.6f));
    }

    [Theory]
    [InlineData("%d", 42, "42")]
    [InlineData("%03d", 7, "007")]
    [InlineData("%-4d|", 7, "7   |")]
    [InlineData("%+d", 7, "+7")]
    [InlineData("%x", 255, "ff")]
    [InlineData("%.0f", 2.4, "2")]
    [InlineData("%.1f", 3.14159, "3.1")]
    [InlineData("%5.1f", 3.14159, "  3.1")]
    [InlineData("%f", 0.5, "0.500000")]
    [InlineData("%d%%", 50, "50%")]
    [InlineData("x %d y", 3, "x 3 y")]
    public void CFormat_Prints_Like_Printf(string format, double value, string expected) =>
        Assert.Equal(expected, CFormat.Format(format, value));

    private static ElementSpec Text(string lines) =>
        ElementReader.Read(HudParser.Parse($"Text(\"t\") {{ {lines} }}").TopLevel[0], ElementKind.Text);

    [Fact]
    public void Numbers_Print_As_The_Text_Element_Prints_Them()
    {
        Assert.Equal("30", HudSimulation.FormatNumber(Text(""), HudEventValue.FromUint(30)));
        Assert.Equal("0.750000", HudSimulation.FormatNumber(Text(""), HudEventValue.FromFloat(0.75f)));
        Assert.Equal("75%", HudSimulation.FormatNumber(Text("Percent(1)"), HudEventValue.FromFloat(0.75f)));
        Assert.Equal("1.5", HudSimulation.FormatNumber(Text("FloatFormat(\"%.1f\")"), HudEventValue.FromFloat(1.5f)));
        Assert.Equal("", HudSimulation.FormatNumber(Text(""), HudEventValue.FromUint(0xFFFFFFFFu)));
        Assert.Equal("--", HudSimulation.FormatNumber(Text("InfiniteDashes(1)"), HudEventValue.FromUint(0xFFFFFFFFu)));
        Assert.Equal("--", HudSimulation.FormatNumber(Text("InfiniteDashes(1)"), HudEventValue.FromFloat(float.MaxValue)));
        Assert.Equal("2:05", HudSimulation.FormatNumber(Text("NumberToTime(\"%m:%02s\")"), HudEventValue.FromFloat(125f)));
        Assert.Equal("1d 01h", HudSimulation.FormatNumber(Text("NumberToTime(\"%dd %02hh\")"), HudEventValue.FromFloat(90000f)));
    }

    private const string Hud = """
        Group("info")
        {
            EventEnable("initialize")
            EventDisable("player1.die")
            Position(0.5, 0.5, 0, "Viewport")
            BarBitmap("health")
            {
                BitmapRect(100, 10, "Left", "Top", "Pixels")
                EventEnable("player1.healthFraction")
                EventDisable("player1.healthDisable")
                EventValue("player1.healthFraction")
                EventColor("player1.example.healthTint")
            }
            Text("ammo")
            {
                EventEnable("initialize")
                EventNumber("player1.weapon1.totalClipBullets")
            }
        }
        Group("marker")
        {
            EventEnable("initialize")
            EventPosition("player1.weapon1.target.position")
            Position(0.1, 0.1, 0, "Viewport")
        }
        """;

    private const string Tint = """
        TransformNumberColor("tint") { NumberColor(0, 255, 0, 0) NumberColor(1, 0, 255, 0) EventInput("player1.healthFraction") EventOutput("player1.example.healthTint") }
        """;

    private static (HudLayoutResult Layout, HudSimulation Sim) Simulate(params (string, HudEventValue)[] sends)
    {
        var doc = HudParser.Parse(Hud);
        var state = new HudEventState();
        state.SendAll(sends);
        var sim = HudSimulation.Run(doc, HudTransformGraph.From(new[] { HudParser.Parse(Tint), doc }), state);
        return (HudLayout.Lay(doc, new LayoutOptions { Screen = new HudScreen(1280, 720), Simulation = sim }), sim);
    }

    private static LaidOutElement Named(HudLayoutResult r, string name) => r.All.Single(e => e.Spec.Name == name);

    [Fact]
    public void Elements_Start_Hidden_And_The_Last_Of_Enable_Or_Disable_Wins()
    {
        var (nothing, _) = Simulate();
        Assert.All(nothing.All, e => Assert.False(e.Shown));

        var (started, _) = Simulate(("initialize", HudEventValue.FromBool(true)));
        Assert.True(Named(started, "info").Shown && Named(started, "ammo").Shown);
        Assert.False(Named(started, "health").Shown);    // its own event has not come yet

        var (healthy, _) = Simulate(("initialize", HudEventValue.FromBool(true)), ("player1.healthFraction", HudEventValue.FromFloat(0.5f)));
        var bar = Named(healthy, "health");
        Assert.True(bar.Shown);
        Assert.Equal(new QuadRect(0, 0, 50, 10), bar.Quad);
        Assert.True(Near(bar.DrawColor.X, 128f / 255f) && Near(bar.DrawColor.Y, 128f / 255f) && Near(bar.DrawColor.Z, 0f),
            $"tint {bar.DrawColor}");   // the transform's tint at 0.5

        var (dead, _) = Simulate(("initialize", HudEventValue.FromBool(true)), ("player1.healthFraction", HudEventValue.FromFloat(0.5f)),
            ("player1.die", HudEventValue.FromBool(true)));
        Assert.False(Named(dead, "info").Shown);
        Assert.False(Named(dead, "health").Shown);       // enabled, but its group is not
        Assert.True(Named(dead, "health").Enabled);

        // Disabled first, then enabled again: shown.
        var (again, _) = Simulate(("player1.healthDisable", HudEventValue.FromBool(true)), ("initialize", HudEventValue.FromBool(true)),
            ("player1.healthFraction", HudEventValue.FromFloat(1f)));
        Assert.True(Named(again, "health").Shown);
    }

    [Fact]
    public void Text_And_Positions_Follow_Their_Events()
    {
        var (r, _) = Simulate(("initialize", HudEventValue.FromBool(true)), ("player1.weapon1.totalClipBullets", HudEventValue.FromUint(30)),
            ("player1.weapon1.target.position", HudEventValue.FromVector(new Vector3(0.25f, 0.75f, 0f))));
        Assert.Equal("30", Named(r, "ammo").Text);
        var marker = Named(r, "marker");
        Assert.True(Near(marker.Local.M41, 320f) && Near(marker.Local.M42, 540f));   // the sent position replaces the authored one
        Assert.True(Near(marker.LoadLocal.M41, 128f));
    }

    [Fact]
    public void A_Tracked_Position_Lands_On_The_Real_Point_In_A_Laid_Out_File()
    {
        var doc = HudParser.Parse("""
            FileInfo("f") { TrueWidescreen(1) }
            Group("bar")
            {
                EventEnable("initialize")
                EventPosition("player1.weapon1.target.position")
                Position(0.5, 0.5, 0, "Viewport")
            }
            """);
        var state = new HudEventState();
        state.SendAll(new[]
        {
            ("initialize", HudEventValue.FromBool(true)),
            ("player1.weapon1.target.position", HudEventValue.FromVector(new Vector3(0.9f, 0.5f, 0f))),
        });
        var sim = HudSimulation.Run(doc, HudTransformGraph.From(new[] { doc }), state);
        var bar = HudLayout.Lay(doc, new LayoutOptions { Screen = new HudScreen(1280, 720), Simulation = sim }).All.Single();
        Assert.Equal(PieceRole.Piece, bar.Role);
        Assert.True(Near(bar.Slide, 160f));      // placed by where it loaded: the middle third
        var x = Vector2.Transform(Vector2.Zero, bar.ToScreen).X;
        Assert.True(Near(x, 0.9f * 1280f, 1e-2f), $"x {x}");
    }

    [Fact]
    public void Usage_Lists_Events_With_Their_Types()
    {
        var doc = HudParser.Parse(Hud);
        var uses = HudEventUsage.Of(doc, HudTransformGraph.From(new[] { HudParser.Parse(Tint), doc }));
        var byName = uses.ToDictionary(u => u.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(HudEventType.Float, byName["player1.healthFraction"].Type);
        Assert.Equal(HudEventSource.Stock, byName["player1.healthFraction"].Source);
        Assert.Equal(HudEventType.Color, byName["player1.example.healthTint"].Type);
        Assert.Equal(HudEventSource.Transform, byName["player1.example.healthTint"].Source);
        Assert.Equal(HudEventType.Vector3, byName["player1.weapon1.target.position"].Type);
        Assert.Equal(HudEventSource.GameExt, byName["player1.weapon1.target.position"].Source);
        Assert.Equal(HudEventType.Uint, byName["player1.weapon1.totalClipBullets"].Type);
    }
}
