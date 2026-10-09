using System.Numerics;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>
/// The stock screen mapping against what was measured in game: at 1280×720 x is exact and
/// y = 40 + 8/9 y, and a <c>"Pixels"</c> 128 × 108 bitmap is square (the user's measurement, and
/// GameExt's HUD diagnostic, 2026-10-05).
/// </summary>
public class EngineMathTests
{
    private static bool Near(float a, float b, float tolerance = 1e-3f) => MathF.Abs(a - b) <= tolerance;

    private static Matrix4x4 StockGroupToScreen(HudScreen s) =>
        EngineMath.LetterboxMatrix(s) * EngineMath.ScreenGroupParent(s) * EngineMath.UnitsToScreen(s);

    [Fact]
    public void Relative_Modes()
    {
        Assert.Equal(12f, EngineMath.ToPixels(RelativeMode.Pixels, 12f, 100f, 200f, 300f));
        Assert.Equal(150f, EngineMath.ToPixels(RelativeMode.Screen, 0.5f, 100f, 200f, 300f));
        Assert.Equal(100f, EngineMath.ToPixels(RelativeMode.Viewport, 0.5f, 100f, 200f, 300f));
        Assert.Equal(50f, EngineMath.ToPixels(RelativeMode.Frame, 0.5f, 100f, 200f, 300f));
        foreach (var mode in Enum.GetValues<RelativeMode>())
            Assert.True(Near(EngineMath.ToRelative(mode, EngineMath.ToPixels(mode, 0.37f, 100f, 200f, 300f), 100f, 200f, 300f), 0.37f));
    }

    [Fact]
    public void Letterbox_Only_On_Screens_Wider_Than_Four_Three()
    {
        var (sy, oy) = EngineMath.Letterbox(new HudScreen(1280, 720));
        Assert.True(Near(sy, 2f / 3f) && Near(oy, 120f));
        Assert.Equal((1f, 0f), EngineMath.Letterbox(new HudScreen(1024, 768)));
        Assert.Equal((1f, 0f), EngineMath.Letterbox(new HudScreen(1280, 1024)));
        // Wider than 4:3 by less than 2 pixels of height: left alone.
        Assert.Equal((1f, 0f), EngineMath.Letterbox(new HudScreen(961, 720)));
        Assert.NotEqual((1f, 0f), EngineMath.Letterbox(new HudScreen(964, 720)));
    }

    [Theory]
    [InlineData(1280, 720, 40f, 8f / 9f)]
    [InlineData(1920, 1080, 60f, 8f / 9f)]
    [InlineData(1024, 768, 0f, 1f)]
    [InlineData(1280, 1024, 32f, 0.9375f)]     // taller than 4:3: the 640 x 480 band is squeezed
    [InlineData(1680, 1050, 21f, 0.96f)]       // 16:10: a = 1.2, a(2 - a) = 0.96
    public void Stock_Band(int w, int h, float top, float scale)
    {
        var s = new HudScreen(w, h);
        var m = StockGroupToScreen(s);
        foreach (var y in new[] { 0f, 100f, 360f, h })
        {
            var p = Vector2.Transform(new Vector2(123f, y), m);
            Assert.True(Near(p.X, 123f, 1e-2f), $"x {p.X}");
            Assert.True(Near(p.Y, top + scale * y, 2e-2f), $"y {p.Y} want {top + scale * y}");
        }
        Assert.True(Near(EngineMath.StockVerticalScale(s), scale, 1e-4f));
    }

    [Fact]
    public void Pixels_128_By_108_Is_Square_At_16_9()
    {
        var s = new HudScreen(1280, 720);
        var stretch = EngineMath.BitmapStretch(s);
        Assert.True(Near(stretch, 4f / 3f));
        var m = EngineMath.ScaleOwnAxes(Matrix4x4.Identity, 1f, stretch) * StockGroupToScreen(s);
        var a = Vector2.Transform(new Vector2(0, 0), m);
        var b = Vector2.Transform(new Vector2(128, 108), m);
        Assert.True(Near(b.X - a.X, 128f, 1e-2f) && Near(b.Y - a.Y, 128f, 1e-2f));
    }

    [Fact]
    public void Rotation_Is_X_Then_Y_Then_Z_And_Scale_Follows()
    {
        // A quarter turn about Z takes +x to +y: clockwise on a y-down screen.
        var r = EngineMath.Rotation(0, 0, 90);
        var p = Vector3.Transform(Vector3.UnitX, r);
        Assert.True(Near(p.X, 0) && Near(p.Y, 1));
        // Angles wrap into [0, 360).
        Assert.True(Near(Vector3.Transform(Vector3.UnitX, EngineMath.Rotation(0, 0, 450)).Y, 1));
        Assert.True(Near(Vector3.Transform(Vector3.UnitX, EngineMath.Rotation(0, 0, -270)).Y, 1));
        // R × S: the scale works along the parent's axes, after the rotation.
        var m = EngineMath.LocalMatrix(r, new Vector3(2, 1, 1), new Vector3(10, 20, 0));
        var q = Vector3.Transform(Vector3.UnitX, m);
        Assert.True(Near(q.X, 10) && Near(q.Y, 21));
        // Mirroring through Y = 180.
        Assert.True(Near(Vector3.Transform(Vector3.UnitX, EngineMath.Rotation(0, 180, 0)).X, -1));
    }

    [Fact]
    public void Rounding_Is_To_Whole_Units()
    {
        var m = Matrix4x4.CreateTranslation(10.5f, -3.5f, 7.25f);
        var r = EngineMath.RoundTranslation(m);
        Assert.Equal(11f, r.M41);
        Assert.Equal(-3f, r.M42);
        Assert.Equal(7.25f, r.M43);
    }
}
