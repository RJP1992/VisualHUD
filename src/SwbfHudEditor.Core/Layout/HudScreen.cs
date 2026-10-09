namespace SwbfHudEditor.Core.Layout;

/// <summary>
/// The screen the game draws its HUD on, in device pixels: <c>RedInterfaceScreen::s_screenFull</c>,
/// which <c>RedRenderer::internalResetState</c> sets to the back buffer. Under Shader Patch's display
/// scaling this is the resolution the game perceives, not the monitor's (1280×720 on a 4K screen at
/// 150%), and it decides how big a <c>"Pixels"</c> value is.
/// </summary>
public readonly record struct HudScreen(int Width, int Height)
{
    public float WidthF => Width;
    public float HeightF => Height;

    /// <summary>
    /// <c>RedRenderer::m_screenAspectRatio</c>, height over width: what <c>GetScreenAspectRatio</c>
    /// returns to the bitmap, minimap and segmented-bar setups.
    /// </summary>
    public float AspectRatio => (float)Height / Width;

    /// <summary>
    /// The engine treats a screen as wide once its height is below 3/4 of its width. GameExt's
    /// <c>is_wide</c>; a <c>TrueWidescreen(1)</c> file is only laid out on such a screen.
    /// </summary>
    public bool IsWide => Width > 0 && Height > 0 && Height < 0.75f * Width;

    public bool IsValid => Width > 0 && Height > 0;

    public override string ToString() => $"{Width}×{Height}";
}
