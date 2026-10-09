namespace SwbfHudEditor.Core.Layout;

/// <summary>
/// GameExt's <c>AuthoredRatio</c> / <c>TrueWidescreen</c> placement maths, ported line for line from
/// BF2GameExt's <c>PatcherDLL/src/render/hud_true_widescreen_core.hpp</c> so the preview places a
/// piece exactly where the game does. Sizes are screen pixels; "layout" is the screen of the file's
/// authored ratio at the real height.
/// </summary>
public static class TrueWidescreen
{
    /// <summary><c>TrueWidescreen(1)</c> files are authored for 4:3.</summary>
    public const float FourThree = 4f / 3f;

    /// <summary>The ratios <c>AuthoredRatio(w, h)</c> takes: from square to 4:1.</summary>
    public static bool ValidRatio(float ratio) => ratio >= 1f && ratio <= 4f;

    /// <summary>An opted-in file is laid out on a screen of its ratio at the real height.</summary>
    public static float LayoutWidth(float height, float ratio = FourThree) => height * ratio;

    /// <summary>
    /// Whether a file authored for <paramref name="ratio"/> is laid out on this screen at all: a 4:3
    /// file only on a screen the engine treats as wide, any other ratio on every screen.
    /// </summary>
    public static bool LaidOutOn(float width, float height, float ratio)
    {
        if (!(width > 0f && height > 0f) || !ValidRatio(ratio)) return false;
        return MathF.Abs(ratio - FourThree) > 1e-4f || height < 0.75f * width;
    }

    public enum Anchor { Left, Center, Right }

    /// <summary>The edge a piece without a <c>ScreenAnchor</c> keeps to, by the third of the layout its x is in.</summary>
    public static Anchor AnchorFor(float x, float layoutWidth)
    {
        if (x < layoutWidth / 3f) return Anchor.Left;
        if (x > layoutWidth * (2f / 3f)) return Anchor.Right;
        return Anchor.Center;
    }

    /// <summary>The share of the width beyond the layout a piece kept to an edge moves by.</summary>
    public static float ShareOf(Anchor anchor) => anchor switch
    {
        Anchor.Center => 0.5f,
        Anchor.Right => 1f,
        _ => 0f,
    };

    /// <summary>How far right a piece is drawn from where the layout puts it; negative on a narrower screen.</summary>
    public static float SlideForShare(float share, float width, float layoutWidth) => share * (width - layoutWidth);

    public static float SlideFor(Anchor anchor, float width, float layoutWidth) =>
        SlideForShare(ShareOf(anchor), width, layoutWidth);

    public static string AnchorName(Anchor anchor) => anchor switch
    {
        Anchor.Center => "Center",
        Anchor.Right => "Right",
        _ => "Left",
    };

    /// <summary><c>ScreenAnchor</c>'s names, in any case and nothing else (no prefix match).</summary>
    public static bool AnchorNamed(string? name, out Anchor anchor)
    {
        foreach (var a in new[] { Anchor.Left, Anchor.Center, Anchor.Right })
        {
            if (name is not null && string.Equals(name, AnchorName(a), StringComparison.OrdinalIgnoreCase))
            {
                anchor = a;
                return true;
            }
        }
        anchor = Anchor.Left;
        return false;
    }

    public static bool ValidShare(float share) => share >= 0f && share <= 1f;

    /// <summary>The name a named anchor's share was read from.</summary>
    public static Anchor NamedAnchor(float share) =>
        share < 0.25f ? Anchor.Left : share > 0.75f ? Anchor.Right : Anchor.Center;

    /// <summary>
    /// A piece written exactly at the layout's corner with nothing moving it there is a plain container,
    /// unless it has a <c>ScreenAnchor</c>: its children are placed one by one instead.
    /// </summary>
    public static bool IsOrigin(float x, float y) => x == 0f && y == 0f;

    public readonly record struct Placement(bool Container, float Slide);

    /// <summary>
    /// Where a load puts a piece: a top-level one at the corner without an anchor is a plain container;
    /// any other piece slides by its anchor's share if it has one, else by its third.
    /// <paramref name="x"/> and <paramref name="y"/> are its translation in layout pixels, after its
    /// group alignment.
    /// </summary>
    public static Placement PlacementFor(float x, float y, bool topLevel, float? share, float width, float layoutWidth)
    {
        if (share is { } s) return new Placement(false, SlideForShare(s, width, layoutWidth));
        if (topLevel && IsOrigin(x, y)) return new Placement(true, 0f);
        return new Placement(false, SlideFor(AnchorFor(x, layoutWidth), width, layoutWidth));
    }

    /// <summary>
    /// A world-tracking position (reticule, lock-on, target, command post) comes as a fraction of the
    /// real width; the fraction that lands on that point once laid out and slid.
    /// </summary>
    public static float TrackedFraction(float fraction, float width, float layoutWidth, float slide) =>
        (fraction * width - slide) / layoutWidth;
}
