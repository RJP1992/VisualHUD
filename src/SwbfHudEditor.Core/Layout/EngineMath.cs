using System.Numerics;

namespace SwbfHudEditor.Core.Layout;

/// <summary>
/// The HUD's coordinate maths as the game does it, read in Ghidra from the modtools executable
/// (<c>BF2_modtools_MemExt.exe</c>, "M") and Phantom ("P", the build with names). Matrices are
/// row-vector, as <c>PblMatrix</c> and D3DX: a point is <c>p × M</c>, and <c>A × B</c> applies A
/// first. Floats are single precision, as in the game.
/// </summary>
/// <remarks>
/// Three spaces matter:
/// <list type="bullet">
/// <item><b>HUD pixels</b>: what <c>.hud</c> numbers become once read, x right and y down, one unit a
/// device pixel of the screen (<c>HUD::Element::SetupViewportDimensions</c>).</item>
/// <item><b>Interface units</b>: the plane the HUD screen groups are drawn on, x right and y up, 640
/// units across the screen whatever its width (<c>RedInterfaceScreen::Render</c>, M <c>00817870</c>).
/// The draw rounds translations to whole units here.</item>
/// <item><b>Screen pixels</b>: where things land on the monitor.</item>
/// </list>
/// </remarks>
public static class EngineMath
{
    /// <summary>The width and height of the mapping the HUD screen groups are drawn through.</summary>
    public const float InterfaceWidth = 640f;
    public const float InterfaceHeight = 480f;

    /// <summary>
    /// The interface camera's frustum width at distance 1, <c>2 tan 30°</c>: it sees
    /// <see cref="InterfaceWidth"/> units across on the plane at <c>z = -640 / t</c>.
    /// </summary>
    public static readonly float FrustumWidth = 2f * MathF.Tan(MathF.PI / 6f);

    /// <summary>
    /// <c>HUD::ConvertRelativeToPixels</c> (M <c>00691120</c>, P <c>005F3090</c>): Pixels keeps the
    /// number, Screen multiplies by the screen's size, Viewport by the view's, Frame by the parent
    /// group's Rect.
    /// </summary>
    public static float ToPixels(RelativeMode mode, float value, float frame, float view, float screen) => mode switch
    {
        RelativeMode.Screen => value * screen,
        RelativeMode.Viewport => value * view,
        RelativeMode.Frame => value * frame,
        _ => value,
    };

    /// <summary>The inverse, as the editor writes a position back (M <c>00691170</c>).</summary>
    public static float ToRelative(RelativeMode mode, float pixels, float frame, float view, float screen)
    {
        var scale = mode switch
        {
            RelativeMode.Screen => screen,
            RelativeMode.Viewport => view,
            RelativeMode.Frame => frame,
            _ => 1f,
        };
        return scale == 0f ? 0f : pixels / scale;
    }

    /// <summary>
    /// The letterbox the HUD loader sets on each HUD screen group (M <c>006B7300</c>):
    /// <c>SetMatrixPosScale(x, 0.5 d, 1, 1 - d / H)</c> with <c>d = 0.75 W - H</c>, once the screen is
    /// at least 2 pixels wider than 4:3. The 640 × 480 mapping then stretches the group back by
    /// <c>0.75 W / H</c>, which leaves the HUD in a band: y 40 to 680 at 1280×720.
    /// </summary>
    public static (float ScaleY, float OffsetY) Letterbox(HudScreen screen)
    {
        var h = screen.HeightF;
        var w = screen.WidthF * 0.75f;
        if (!(MathF.Abs(h - w) >= 2f) || !(h < w)) return (1f, 0f);
        var d = w - h;
        return (1f - d / h, 0.5f * d);
    }

    /// <summary>The letterbox as the screen group's local matrix.</summary>
    public static Matrix4x4 LetterboxMatrix(HudScreen screen)
    {
        var (scaleY, offsetY) = Letterbox(screen);
        return new Matrix4x4(
            1f, 0f, 0f, 0f,
            0f, scaleY, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, offsetY, 0f, 1f);
    }

    /// <summary>
    /// The parent <c>RedInterfaceScreen::Render</c> draws a HUD screen group under (its flag bit 1,
    /// which <c>HUD::Manager::Open</c> sets on them, with the safe zone off): HUD pixels scaled to the
    /// 640 × 480 mapping, y turned up, the group's screen-relative corner (0, 0 with one viewport) at
    /// the top left, on the plane at <c>z = -640 / t</c>.
    /// </summary>
    public static Matrix4x4 ScreenGroupParent(HudScreen screen, float relX = 0f, float relY = 0f) => new(
        InterfaceWidth / screen.WidthF, 0f, 0f, 0f,
        0f, -InterfaceHeight / screen.HeightF, 0f, 0f,
        0f, 0f, 1f, 0f,
        (relX - 0.5f) * InterfaceWidth, (0.5f - relY) * InterfaceHeight, -InterfaceWidth / FrustumWidth, 1f);

    /// <summary>
    /// GameExt's <c>pixel_parent</c>: the parent an opted-in top-level piece is drawn under instead of
    /// its letterboxed group, one layout pixel to one screen pixel on the same plane, slid right by
    /// <paramref name="slide"/> pixels.
    /// </summary>
    public static Matrix4x4 PixelParent(HudScreen screen, float slide)
    {
        var k = InterfaceWidth / screen.WidthF;
        return new Matrix4x4(
            k, 0f, 0f, 0f,
            0f, -k, 0f, 0f,
            0f, 0f, 1f, 0f,
            (slide - 0.5f * screen.WidthF) * k, 0.5f * screen.HeightF * k, -InterfaceWidth / FrustumWidth, 1f);
    }

    /// <summary>
    /// Interface units on the HUD plane to screen pixels: the camera sees 640 units across the
    /// screen's width and as many per pixel down it. Depth is dropped; everything the HUD draws flat
    /// sits on the plane.
    /// </summary>
    public static Matrix4x4 UnitsToScreen(HudScreen screen)
    {
        var k = screen.WidthF / InterfaceWidth;
        return new Matrix4x4(
            k, 0f, 0f, 0f,
            0f, -k, 0f, 0f,
            0f, 0f, 1f, 0f,
            0.5f * screen.WidthF, 0.5f * screen.HeightF, 0f, 1f);
    }

    /// <summary>
    /// The vertical scale the stock mapping gives a screen group's contents, about the screen's middle:
    /// <c>a(2 - a)</c> with the letterbox and <c>a</c> without, where <c>a = 0.75 W / H</c>. 8/9 at
    /// 16:9, 1 at 4:3.
    /// </summary>
    public static float StockVerticalScale(HudScreen screen) =>
        Letterbox(screen).ScaleY * 0.75f * screen.WidthF / screen.HeightF;

    /// <summary>
    /// The vertical scale <c>ElementBitmapBase::PostReadSetup</c> (M <c>00698790</c>) puts on every
    /// bitmap's own axes, <c>0.75 / GetScreenAspectRatio()</c>: 4/3 at 16:9, so with the 8/9 band a
    /// bitmap stands 32/27 as tall as written. GameExt returns 0.75 for an opted-in element's call,
    /// which makes it 1.
    /// </summary>
    public static float BitmapStretch(HudScreen screen) => 0.75f / screen.AspectRatio;

    /// <summary>
    /// The draw's rounding (<c>RedInterfaceElement</c> draw, M <c>00816FA0</c>): the world matrix's
    /// translation x and y as <c>floor(v + 0.5)</c>, in interface units, at every element. Phantom and
    /// GameExt's <c>HudSubPixel</c> (on by default) skip it.
    /// </summary>
    public static Matrix4x4 RoundTranslation(Matrix4x4 m)
    {
        m.M41 = MathF.Floor(m.M41 + 0.5f);
        m.M42 = MathF.Floor(m.M42 + 0.5f);
        return m;
    }

    /// <summary>
    /// <c>HUD::Matrix::SetRotation</c> (P <c>005F51E0</c>): each angle wrapped into [0, 360), then
    /// <c>RotationX × RotationY × RotationZ</c>, X first.
    /// </summary>
    public static Matrix4x4 Rotation(float xDegrees, float yDegrees, float zDegrees)
    {
        const float toRadians = MathF.PI / 180f;
        return Matrix4x4.CreateRotationX(Wrap(xDegrees) * toRadians)
             * Matrix4x4.CreateRotationY(Wrap(yDegrees) * toRadians)
             * Matrix4x4.CreateRotationZ(Wrap(zDegrees) * toRadians);
    }

    private static float Wrap(float degrees)
    {
        if (!float.IsFinite(degrees)) return 0f;
        degrees %= 360f;
        return degrees < 0f ? degrees + 360f : degrees;
    }

    /// <summary>
    /// An element's local matrix after <c>HUD::Element::PostReadSetup</c> (P <c>005F3CA0</c>):
    /// <c>HUD::Matrix::ApplyScale</c> (P <c>005F2C40</c>) multiplies the rotation by the scale
    /// afterwards, <c>R × S</c>, so a scale works along the parent's axes; the translation is the
    /// position already converted to pixels.
    /// </summary>
    public static Matrix4x4 LocalMatrix(Matrix4x4 rotation, Vector3 scale, Vector3 translation)
    {
        var m = rotation * Matrix4x4.CreateScale(scale);
        m.M41 = translation.X;
        m.M42 = translation.Y;
        m.M43 = translation.Z;
        m.M44 = 1f;
        return m;
    }

    /// <summary>
    /// <c>RedInterfaceElement::Scale(sx, sy)</c> (M <c>008285F0</c>): scales the element's own x and y
    /// axes, the matrix's first two rows, before everything else in it.
    /// </summary>
    public static Matrix4x4 ScaleOwnAxes(Matrix4x4 m, float sx, float sy)
    {
        m.M11 *= sx; m.M12 *= sx; m.M13 *= sx; m.M14 = 0f;
        m.M21 *= sy; m.M22 *= sy; m.M23 *= sy; m.M24 = 0f;
        return m;
    }
}
