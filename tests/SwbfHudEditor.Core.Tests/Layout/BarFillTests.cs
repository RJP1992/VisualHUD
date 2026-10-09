using SwbfHudEditor.Core.Layout;
using static SwbfHudEditor.Core.Layout.BarFill;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>The vectors of BF2GameExt's <c>tests/hud_bar_fill_from_tests.cpp</c>, run against the C# port.</summary>
public class BarFillTests
{
    private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-4f;

    private static float UAt(Bar b, float x)
    {
        var t = (x - b.Rect.Left) / (b.Rect.Right - b.Rect.Left);
        return b.Uv.U0 + t * (b.Uv.U1 - b.Uv.U0);
    }

    private static float FullUAt(QuadRect r, TexRect t, float x) =>
        t.U0 + (x - r.Left) / (r.Right - r.Left) * (t.U1 - t.U0);

    private static readonly QuadRect[] Rects = { new(-96, -64, 96, 64), new(0, 0, 192, 128), new(-192, 10, 0, 40) };
    private static readonly TexRect[] Uvs = { new(0, 0, 1, 1), new(0, 0.5f, 1, 1), new(0, 1, 1, 0), new(0.25f, 0, 0.75f, 1), new(1, 0, 0, 1) };

    [Fact]
    public void Right_Anchor_Keeps_The_Right_Edge_And_Each_Pixel()
    {
        foreach (var r in Rects)
            foreach (var t in Uvs)
                for (var step = 0; step <= 20; step++)
                {
                    var value = step / 20f;
                    var b = Fill(AnchorRight(r, t), value);
                    Assert.True(Near(b.Rect.Left, r.Right));
                    Assert.True(Near(b.Rect.Right, r.Right - value * (r.Right - r.Left)));
                    Assert.True(b.Rect.Top == r.Top && b.Rect.Bottom == r.Bottom);
                    Assert.True(b.Uv.V0 == t.V0 && b.Uv.V1 == t.V1);
                    if (value > 0)
                        for (var k = 0; k <= 8; k++)
                        {
                            var x = b.Rect.Left + (b.Rect.Right - b.Rect.Left) * k / 8f;
                            Assert.True(Near(UAt(b, x), FullUAt(r, t, x)));
                        }
                }
    }

    [Fact]
    public void Stock_And_Right_Bars_Meet()
    {
        var r = new QuadRect(-96, -64, 96, 64);
        var t = new TexRect(0, 0, 1, 1);
        for (var step = 0; step <= 20; step++)
        {
            var health = step / 20f;
            var left = Fill(Stock(r, t), health);
            var right = Fill(AnchorRight(r, t), 1f - health);
            Assert.True(Near(left.Rect.Right, right.Rect.Right));
            Assert.True(Near(left.Uv.U1, right.Uv.U1));
        }
        // The stock U bug: TexCoords from 0.25 reach U 1.0 at full instead of 0.75.
        Assert.True(Near(Fill(Stock(r, new TexRect(0.25f, 0, 0.75f, 1)), 1f).Uv.U1, 1f));
    }

    [Fact]
    public void Vertical_Fills()
    {
        TexRect[] vuvs = { new(0, 0, 1, 1), new(0, 0.25f, 1, 0.75f), new(0, 1, 1, 0), new(0.1f, 0.2f, 0.9f, 0.6f) };
        foreach (var full in Rects)
            foreach (var uv in vuvs)
                foreach (var top in new[] { false, true })
                    for (var step = 0; step <= 20; step++)
                    {
                        var value = step / 20f;
                        var b = FillVertical(new Vertical(full, uv, top, true), value);
                        Assert.True(b.Rect.Left == full.Left && b.Rect.Right == full.Right);
                        Assert.True(b.Uv.U0 == uv.U0 && b.Uv.U1 == uv.U1);
                        var height = full.Bottom - full.Top;
                        if (top)
                        {
                            Assert.True(b.Rect.Top == full.Top && Near(b.Rect.Bottom, full.Top + value * height));
                            Assert.True(b.Uv.V0 == uv.V0);
                        }
                        else
                        {
                            Assert.True(b.Rect.Bottom == full.Bottom && Near(b.Rect.Top, full.Bottom - value * height));
                            Assert.True(b.Uv.V1 == uv.V1);
                        }
                        if (value > 0)
                            for (var k = 0; k <= 8; k++)
                            {
                                var y = b.Rect.Top + (b.Rect.Bottom - b.Rect.Top) * k / 8f;
                                var shown = b.Uv.V0 + (y - b.Rect.Top) / (b.Rect.Bottom - b.Rect.Top) * (b.Uv.V1 - b.Uv.V0);
                                var wanted = uv.V0 + (y - full.Top) / height * (uv.V1 - uv.V0);
                                Assert.True(Near(shown, wanted));
                            }
                    }

        var r = new QuadRect(-96, -64, 96, 64);
        var t = new TexRect(0, 0, 1, 1);
        Assert.True(Near(FillVertical(new Vertical(r, t, false, true), 1.5f).Rect.Top, r.Top));
        Assert.True(Near(FillVertical(new Vertical(r, t, false, true), -0.5f).Rect.Top, r.Bottom));
        var half = FillVertical(new Vertical(r, t, false, false), 0.5f);
        Assert.True(Near(half.Rect.Top, 0) && half.Uv.V0 == t.V0 && half.Uv.V1 == t.V1);

        for (var step = 0; step <= 20; step++)
        {
            var health = step / 20f;
            var lower = FillVertical(new Vertical(r, t, false, true), health);
            var upper = FillVertical(new Vertical(r, t, true, true), 1f - health);
            Assert.True(Near(lower.Rect.Top, upper.Rect.Bottom));
            Assert.True(Near(lower.Uv.V0, upper.Uv.V1));
        }
    }

    [Fact]
    public void The_Two_Flags_And_Unfilled()
    {
        var r = new QuadRect(-96, -64, 96, 64);
        foreach (var uv in Uvs)
        {
            var full = AnchorRight(r, uv);
            var both = Fill(full, 0.25f, true, true);
            var edge = Fill(full, 0.25f, false, true);
            var crop = Fill(full, 0.25f, true, false);
            var none = Fill(full, 0.25f, false, false);
            Assert.True(Near(edge.Rect.Right, both.Rect.Right) && edge.Uv.U1 == full.Uv.U1);
            Assert.True(crop.Rect.Right == full.Rect.Right && Near(crop.Uv.U1, both.Uv.U1));
            Assert.True(none.Rect.Right == full.Rect.Right && none.Uv.U1 == full.Uv.U1);
        }

        foreach (var rect in Rects)
            foreach (var uv in Uvs)
                for (var flags = 0; flags < 4; flags++)
                    for (var step = 0; step <= 10; step++)
                    {
                        bool cropTex = (flags & 1) != 0, moveEdge = (flags & 2) != 0;
                        var set = Stock(rect, uv);
                        var drawn = Fill(set, step / 10f, cropTex, moveEdge);
                        var back = Unfilled(drawn);
                        Assert.True(back.Rect.Left == rect.Left && back.Rect.Top == rect.Top && Near(back.Rect.Right, rect.Right) &&
                                    back.Rect.Bottom == rect.Bottom);
                        Assert.True(back.Uv.U0 == uv.U0 && back.Uv.V0 == uv.V0 && Near(back.Uv.U1, uv.U1) && back.Uv.V1 == uv.V1);
                        var again = Fill(back, step / 10f, cropTex, moveEdge);
                        Assert.True(Near(again.Rect.Right, drawn.Rect.Right) && Near(again.Uv.U1, drawn.Uv.U1));
                    }
    }
}
