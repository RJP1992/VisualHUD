using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Events;

/// <summary>The HUD over time: fades, colours, pulses, change strips, eased lerps and held comparisons.</summary>
public class HudClockTests
{
    private sealed class Scenario
    {
        public required HudDocument Document { get; init; }
        public HudEventState State { get; } = new();

        public void At(double time, string name, HudEventValue value)
        {
            State.SetTime(time);
            State.Send(name, value);
        }

        public HudClock Run(double time, bool allShown = false)
        {
            State.SetTime(time);
            var clock = HudClock.Start(new[] { Document }, HudTransformGraph.From(new[] { Document }), State, allShown: allShown);
            clock.RunTo(time);
            return clock;
        }

        public ElementClock Element(HudClock clock, string name) =>
            clock.For(All(Document.TopLevel).First(b => b.HasBody && b.ItemName == name))!;

        private static IEnumerable<HudBlock> All(IEnumerable<HudBlock> blocks)
        {
            foreach (var b in blocks)
            {
                yield return b;
                foreach (var inner in All(b.Children)) yield return inner;
            }
        }
    }

    private static Scenario Load(string source) => new() { Document = HudParser.Parse(source) };

    private static readonly HudEventValue On = HudEventValue.FromBool(true);

    private const string Fading = """
        Bitmap("b")
        {
            EventEnable("show")
            EventDisable("hide")
            FadeInTime(0.5)
            FadeOutTime(1.0)
        }
        """;

    [Fact]
    public void An_Element_Fades_In_From_Its_Enable_And_Out_From_Its_Disable()
    {
        var s = Load(Fading);
        s.At(0, "show", On);
        var e = s.Element(s.Run(0.25), "b");
        Assert.True(e.Shown);
        // 15 frames of 1/60 s plus the send's own 0.001 s.
        Assert.Equal(0.251f / 0.5f, e.Level, 3);
        Assert.Equal(FadePhase.Hold, s.Element(s.Run(0.6), "b").Phase);

        s.At(1.0, "hide", On);
        Assert.Equal(1f - 0.251f / 1f, s.Element(s.Run(1.25), "b").Level, 2);
        // Hidden the frame after the level reaches 0.
        Assert.False(s.Element(s.Run(2.1), "b").Shown);
    }

    [Fact]
    public void A_Disable_During_The_Fade_In_Jumps_To_One_Minus_The_Level()
    {
        var s = Load(Fading);
        s.At(0, "show", On);
        s.At(0.1, "hide", On);
        // At 0.1 s the level was about 0.2; Stop keeps the share of time, so the fade-out starts at about 0.8.
        var e = s.Element(s.Run(0.1), "b");
        Assert.Equal(FadePhase.Release, e.Phase);
        Assert.InRange(e.Level, 0.78f, 0.81f);
    }

    [Fact]
    public void A_Hold_Time_Fades_The_Element_Out_On_Its_Own()
    {
        var s = Load("""
            Text("t")
            {
                EventEnable("player1.weapon1.change")
                FadeInTime(0.25)
                FadeHoldTime(2.5)
                FadeOutTime(1.0)
            }
            """);
        s.At(0, "player1.weapon1.change", On);
        Assert.Equal(FadePhase.Hold, s.Element(s.Run(2.5), "t").Phase);
        Assert.Equal(FadePhase.Release, s.Element(s.Run(3.0), "t").Phase);
        Assert.False(s.Element(s.Run(3.9), "t").Shown);
    }

    [Fact]
    public void A_Sent_Colour_Eases_In_Per_Frame_And_Stops_A_Few_Units_Short()
    {
        var s = Load("""
            Bitmap("b")
            {
                EventEnable("show")
                EventColor("tint")
                Color(255, 255, 255)
                ColorChangeRate(0.3)
            }
            """);
        s.At(0, "show", On);
        s.At(1, "tint", HudEventValue.FromColor(new HudColor(0, 0, 0)));
        var early = s.Element(s.Run(1.3), "b").DrawColor.R;
        Assert.InRange(early, 80, 110);                 // about e^-1 of the way back: 255 x 0.37
        // Each frame moves k = dt / rate of the way, rounded: it stalls about 0.5 rate / dt = 9 short.
        var late = s.Element(s.Run(6), "b").DrawColor.R;
        Assert.InRange(late, 5, 10);
    }

    [Fact]
    public void A_Pulse_Snaps_To_ColorChange_Every_ColorPulseRate_Seconds_Of_Being_Shown()
    {
        var s = Load("""
            Bitmap("b")
            {
                EventEnable("show")
                ColorChange(255, 0, 0)
                ColorPulseRate(1.0)
                ColorChangeRate(0.3)
            }
            """);
        s.At(0, "show", On);
        var before = s.Element(s.Run(0.95), "b").DrawColor;
        Assert.Equal(255, before.G);
        // The countdown runs out on the frame at 1.0 s (the send's 0.001 s ahead): red, then easing back.
        var pulse = s.Element(s.Run(1.0), "b").DrawColor;
        Assert.Equal((byte)0, pulse.G);
        Assert.True(s.Element(s.Run(1.2), "b").DrawColor.G > 100);
    }

    [Fact]
    public void A_Change_Flashes_To_ColorChange_With_UseChangeColor()
    {
        var s = Load("""
            Text("t")
            {
                EventEnable("show")
                EventNumber("ammo")
                UseChangeColor(1)
                ColorChange(255, 0, 0)
            }
            """);
        s.At(0, "show", On);
        s.At(1, "ammo", HudEventValue.FromInt(5));
        Assert.Equal((byte)0, s.Element(s.Run(1), "t").DrawColor.G);
        Assert.True(s.Element(s.Run(2), "t").DrawColor.G > 230);
    }

    private const string Lerp = """
        TransformNumberLerp("ease")
        {
            EventInput("player1.health")
            EventOutput("eased")
            RiseTime(1.0)
            FallTime(0.5)
        }
        """;

    [Fact]
    public void A_Lerp_Eases_Its_Output_Over_Its_Rise_And_Fall_Times()
    {
        var s = Load(Lerp);
        s.At(0, "player1.health", HudEventValue.FromFloat(0f));
        s.At(1, "player1.health", HudEventValue.FromFloat(1f));
        Assert.Equal(0f, s.Run(0.5).Values["eased"].Value.Float);
        // GameExt ticks after the frame's sends, so the move starts in the send's own frame: 31 frames by 1.5 s.
        Assert.Equal(31f / 60f, s.Run(1.5).Values["eased"].Value.Float, 4);
        Assert.Equal(1f, s.Run(2.1).Values["eased"].Value.Float, 3);

        // Falling takes FallTime for the whole swing: 16 frames of 1/30 by 3.25 s.
        s.At(3, "player1.health", HudEventValue.FromFloat(0f));
        Assert.Equal(1f - 16f / 30f, s.Run(3.25).Values["eased"].Value.Float, 4);
    }

    [Fact]
    public void A_Compare_Holds_By_Its_Hysteresis()
    {
        var s = Load("""
            TransformNumberCompare("low")
            {
                EventInputA("player1.health")
                ConstantB(0.4)
                Operation("Greater")
                Hysteresis(0.1)
                EventOutput("high")
            }
            """);
        s.At(0, "player1.health", HudEventValue.FromFloat(0.5f));
        Assert.Equal(1f, s.Run(0).Values["high"].Value.Float);
        s.At(1, "player1.health", HudEventValue.FromFloat(0.35f));
        Assert.Equal(1f, s.Run(1).Values["high"].Value.Float);    // still above 0.4 - 0.1
        s.At(2, "player1.health", HudEventValue.FromFloat(0.25f));
        Assert.Equal(0f, s.Run(2).Values["high"].Value.Float);
    }

    private const string Bar = """
        BarBitmap("bar")
        {
            EventEnable("show")
            EventValue("value")
            FlashyDecFadeOutTime(2.0)
            BitmapRect(100, 10, "Left", "Top", "Pixels")
        }
        """;

    [Fact]
    public void A_Shrinking_Bar_Leaves_A_Strip_That_Fades_And_Comes_Down()
    {
        var s = Load(Bar);
        s.At(0, "show", On);
        s.At(0.5, "value", HudEventValue.FromFloat(1f));     // arms the bar
        s.At(1, "value", HudEventValue.FromFloat(0.25f));
        var strip = s.Element(s.Run(1), "bar").Strip;
        Assert.NotNull(strip);
        Assert.Equal(0.25f, strip!.Value.From, 3);
        Assert.Equal(1f, strip.Value.To, 3);
        Assert.True(strip.Value.Alpha > 0.99f);
        var later = s.Element(s.Run(2), "bar").Strip!.Value;
        Assert.Equal(0.5f, later.Alpha, 1);
        Assert.True(later.Collapse > 0.95f);
        Assert.Null(s.Element(s.Run(3.1), "bar").Strip);

        // Laid out, the strip covers the uncovered three quarters, in white.
        s.State.SetTime(2);
        var clock = s.Run(2);
        var layout = HudLayout.Lay(s.Document, new LayoutOptions
        {
            Screen = new HudScreen(1280, 720),
            Simulation = HudSimulation.Run(s.Document, HudTransformGraph.From(new[] { s.Document }), s.State, null, clock),
            ShowByEvents = true,
        });
        var piece = layout.All.Single(e => e.Piece is not null);
        Assert.Equal(25f, piece.Quad!.Value.Left, 0);
        Assert.Equal(100f, piece.Quad!.Value.Right, 0);
    }

    [Fact]
    public void With_The_Clock_The_Layout_Draws_An_Element_At_Its_Fade_Level()
    {
        var s = Load(Fading);
        s.At(0, "show", On);
        var clock = s.Run(0.25);
        var layout = HudLayout.Lay(s.Document, new LayoutOptions
        {
            Screen = new HudScreen(1280, 720),
            Simulation = HudSimulation.Run(s.Document, HudTransformGraph.From(new[] { s.Document }), s.State, null, clock),
            ShowByEvents = true,
        });
        Assert.Equal(0.5f, layout.All.Single().DrawAlpha, 1);
    }

    [Fact]
    public void Running_On_Gives_The_Same_As_Starting_Again()
    {
        var s = Load(Fading + Lerp);
        s.At(0, "show", On);
        s.At(0.2, "player1.health", HudEventValue.FromFloat(0f));
        s.At(0.4, "player1.health", HudEventValue.FromFloat(1f));
        var stepped = s.Run(0.1);
        stepped.RunTo(0.7);
        var fresh = s.Run(0.7);
        Assert.Equal(fresh.Values["eased"].Value.Float, stepped.Values["eased"].Value.Float);
        Assert.Equal(s.Element(fresh, "b").Level, s.Element(stepped, "b").Level);
    }
}
