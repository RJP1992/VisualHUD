using System.Numerics;
using SwbfHudEditor.Core.Layout;
using static SwbfHudEditor.Core.Layout.TrueWidescreen;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>
/// The vectors of BF2GameExt's <c>tests/hud_true_widescreen_tests.cpp</c>, run against the C# port, so
/// the preview places pieces where the game does.
/// </summary>
public class TrueWidescreenTests
{
    private static bool Near(float a, float b, float tolerance = 1e-3f) => MathF.Abs(a - b) <= tolerance;

    /// <summary>
    /// The interface camera <c>RedInterfaceScreen::Render</c> sets up, projecting a point the way the
    /// game does (perspective, frustum width t), to check the matrices against.
    /// </summary>
    private static Vector2 Project(Matrix4x4 m, float lx, float ly, float width, float height)
    {
        var t = EngineMath.FrustumWidth;
        var x = lx * m.M11 + ly * m.M21 + m.M41;
        var y = lx * m.M12 + ly * m.M22 + m.M42;
        var z = -(lx * m.M13 + ly * m.M23 + m.M43);
        var halfW = z * t * 0.5f;
        var halfH = z * t * (height / width) * 0.5f;
        return new Vector2((x / halfW + 1f) * 0.5f * width, (1f - y / halfH) * 0.5f * height);
    }

    [Fact]
    public void Layout_And_Anchors()
    {
        Assert.True(new HudScreen(1280, 720).IsWide && new HudScreen(1920, 1080).IsWide);
        Assert.True(new HudScreen(1680, 1050).IsWide && new HudScreen(2560, 1080).IsWide);
        Assert.False(new HudScreen(1024, 768).IsWide || new HudScreen(1280, 1024).IsWide);
        Assert.True(Near(LayoutWidth(720), 960) && Near(LayoutWidth(1080), 1440));

        var lw = LayoutWidth(720);
        Assert.Equal(Anchor.Left, AnchorFor(0.054041f * lw, lw));
        Assert.Equal(Anchor.Center, AnchorFor(0.5f * lw, lw));
        Assert.Equal(Anchor.Right, AnchorFor(0.88f * lw, lw));
        Assert.Equal(Anchor.Center, AnchorFor(lw / 3f, lw));
        Assert.Equal(Anchor.Center, AnchorFor(lw * 2f / 3f, lw));
        Assert.Equal(Anchor.Left, AnchorFor(-50f, lw));
        Assert.Equal(Anchor.Right, AnchorFor(lw + 50f, lw));

        Assert.True(Near(SlideFor(Anchor.Left, 1280, lw), 0));
        Assert.True(Near(SlideFor(Anchor.Center, 1280, lw), 160));
        Assert.True(Near(SlideFor(Anchor.Right, 1280, lw), 320));

        var map = 0.88f * lw;
        Assert.True(Near(1280 - (map + SlideFor(Anchor.Right, 1280, lw)), lw - map));
        Assert.True(Near(0.5f * lw + SlideFor(Anchor.Center, 1280, lw), 640));
        Assert.True(IsOrigin(0, 0) && !IsOrigin(0, 1) && !IsOrigin(0.5f, 0));
    }

    [Fact]
    public void Tracking_Lands_On_The_Real_Point()
    {
        float[] widths = { 1280, 1920, 1680, 2560, 3440 };
        float[] heights = { 720, 1080, 1050, 1080, 1440 };
        for (var s = 0; s < widths.Length; s++)
        {
            float w = widths[s], h = heights[s], lw = LayoutWidth(h);
            foreach (var slide in new[] { 0f, SlideFor(Anchor.Center, w, lw), SlideFor(Anchor.Right, w, lw) })
                for (var f = -0.25f; f <= 1.25f; f += 0.125f)
                    Assert.True(Near(TrackedFraction(f, w, lw, slide) * lw + slide, f * w, 1e-2f));
        }
        var lw720 = LayoutWidth(720);
        var x = TrackedFraction(0.5f, 1280, lw720, SlideFor(Anchor.Center, 1280, lw720)) * lw720;
        Assert.True(Near(x, 480) && Near(x + 160, 640));
    }

    [Fact]
    public void Pixel_Parent_Is_One_To_One()
    {
        float[][] sizes = { new[] { 1280f, 720f }, new[] { 1920f, 1080f }, new[] { 1680f, 1050f }, new[] { 2560f, 1080f }, new[] { 1024f, 768f } };
        foreach (var size in sizes)
        {
            float w = size[0], h = size[1];
            var screen = new HudScreen((int)w, (int)h);
            foreach (var slide in new[] { 0f, 160f, 427.5f })
            {
                var m = EngineMath.PixelParent(screen, slide);
                float[][] points = { new[] { 0f, 0f }, new[] { 0.5f * w, 0.5f * h }, new[] { 100f, 37f }, new[] { w, h }, new[] { -20f, h + 5 } };
                foreach (var p in points)
                {
                    var projected = Project(m, p[0], p[1], w, h);
                    Assert.True(Near(projected.X, p[0] + slide, 1e-2f) && Near(projected.Y, p[1], 1e-2f));
                    // The flat mapping the preview draws with agrees on the plane.
                    var flat = Vector2.Transform(new Vector2(p[0], p[1]), m * EngineMath.UnitsToScreen(screen));
                    Assert.True(Near(flat.X, projected.X, 1e-2f) && Near(flat.Y, projected.Y, 1e-2f));
                }
            }
        }
        var m720 = EngineMath.PixelParent(new HudScreen(1280, 720), 0);
        Assert.True(Near(m720.M43, -640f / EngineMath.FrustumWidth) && Near(m720.M43, -554.256f, 1e-2f));
    }

    [Fact]
    public void Authored_Ratios()
    {
        float r169 = 16f / 9f, r219 = 21f / 9f, r1610 = 16f / 10f;
        Assert.True(Near(LayoutWidth(720, r169), 1280) && Near(LayoutWidth(1080, r169), 1920));
        Assert.True(ValidRatio(1f) && ValidRatio(FourThree) && ValidRatio(r219) && ValidRatio(32f / 9f));
        Assert.False(ValidRatio(0.5f) || ValidRatio(4.5f) || ValidRatio(0f));

        Assert.True(LaidOutOn(1280, 720, FourThree) && LaidOutOn(2560, 1080, FourThree));
        Assert.False(LaidOutOn(1024, 768, FourThree) || LaidOutOn(1280, 1024, FourThree));
        Assert.True(LaidOutOn(1280, 720, r169) && LaidOutOn(1024, 768, r169) && LaidOutOn(1680, 1050, r169));
        Assert.True(LaidOutOn(1280, 1024, r169) && LaidOutOn(2560, 1080, r169));
        Assert.Equal(LaidOutOn(1024, 768, FourThree), LaidOutOn(1024, 768, 4f / 3.0001f));
        Assert.False(LaidOutOn(0, 720, r169) || LaidOutOn(1280, 0, r169) || LaidOutOn(1280, 720, 0.5f));

        var lw = LayoutWidth(720, r169);
        foreach (var a in new[] { Anchor.Left, Anchor.Center, Anchor.Right }) Assert.True(Near(SlideFor(a, 1280, lw), 0));

        float[][] screens = { new[] { 960f, 720f }, new[] { 1024f, 768f }, new[] { 1680f, 1050f }, new[] { 1280f, 720f },
                              new[] { 2560f, 1080f }, new[] { 3440f, 1440f }, new[] { 1280f, 1024f } };
        foreach (var ratio in new[] { FourThree, r1610, r169, r219 })
        {
            foreach (var s in screens)
            {
                float w = s[0], h = s[1], l = LayoutWidth(h, ratio);
                float left = 0.05f * l, middle = 0.5f * l, right = 0.93f * l;
                Assert.Equal(Anchor.Left, AnchorFor(left, l));
                Assert.Equal(Anchor.Center, AnchorFor(middle, l));
                Assert.Equal(Anchor.Right, AnchorFor(right, l));
                Assert.True(Near(left + SlideFor(Anchor.Left, w, l), left));
                Assert.True(Near(middle + SlideFor(Anchor.Center, w, l), 0.5f * w));
                Assert.True(Near(w - (right + SlideFor(Anchor.Right, w, l)), l - right));
            }
        }

        Assert.True(Near(SlideFor(Anchor.Right, 960, lw), -320) && Near(SlideFor(Anchor.Center, 960, lw), -160));
    }

    [Fact]
    public void Screen_Anchors()
    {
        Assert.True(AnchorNamed("Left", out var a) && a == Anchor.Left);
        Assert.True(AnchorNamed("center", out a) && a == Anchor.Center);
        Assert.True(AnchorNamed("RIGHT", out a) && a == Anchor.Right);
        Assert.False(AnchorNamed("Centre", out _) || AnchorNamed("", out _) || AnchorNamed("Lef", out _) ||
                     AnchorNamed("Lefts", out _) || AnchorNamed(null, out _));
        foreach (var e in new[] { Anchor.Left, Anchor.Center, Anchor.Right })
        {
            Assert.True(AnchorNamed(AnchorName(e), out a) && a == e);
            Assert.Equal(e, NamedAnchor(ShareOf(e)));
        }
        Assert.True(ValidShare(0f) && ValidShare(0.25f) && ValidShare(1f));
        Assert.False(ValidShare(-0.01f) || ValidShare(1.01f) || ValidShare(float.NaN));

        // A piece meant for 30% of a 1280 x 720 screen in a 4:3 file: drawn at 384 with
        // ScreenAnchor(0.3), where the thirds would leave it at 288.
        var lw = LayoutWidth(720);
        var x = 0.3f * 1280 - SlideForShare(0.3f, 1280, lw);
        Assert.True(Near(x, 288) && AnchorFor(x, lw) == Anchor.Left);
        Assert.True(Near(x + SlideForShare(0.3f, 1280, lw), 384));
    }

    [Fact]
    public void Placements()
    {
        float w = 1280, lw = LayoutWidth(720);
        var p = PlacementFor(0, 0, true, null, w, lw);
        Assert.True(p.Container && p.Slide == 0f);
        p = PlacementFor(0, 0, true, 1f, w, lw);
        Assert.True(!p.Container && Near(p.Slide, 320));
        p = PlacementFor(0, 0, false, null, w, lw);
        Assert.True(!p.Container && Near(p.Slide, 0));
        Assert.False(PlacementFor(0, 10, true, null, w, lw).Container || PlacementFor(10, 0, true, null, w, lw).Container);

        foreach (var top in new[] { true, false })
        {
            Assert.True(Near(PlacementFor(100, 50, top, null, w, lw).Slide, 0));
            Assert.True(Near(PlacementFor(480, 50, top, null, w, lw).Slide, 160));
            Assert.True(Near(PlacementFor(900, 50, top, null, w, lw).Slide, 320));
            Assert.True(Near(PlacementFor(100, 50, top, 1f, w, lw).Slide, 320));
            Assert.True(Near(PlacementFor(900, 50, top, 0.25f, w, lw).Slide, 80));
        }
        Assert.True(Near(PlacementFor(319, 50, true, null, w, lw).Slide, 0));
        Assert.True(Near(PlacementFor(321, 50, true, null, w, lw).Slide, 160));

        var lw169 = LayoutWidth(720, 16f / 9f);
        foreach (var x in new[] { 0f, 300f, 640f, 1000f, 1280f })
            Assert.Equal(0f, PlacementFor(x, 50, true, null, 1280, lw169).Slide);
    }
}
