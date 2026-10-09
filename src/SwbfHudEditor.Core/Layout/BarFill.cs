namespace SwbfHudEditor.Core.Layout;

/// <summary>A quad in an element's own pixels: left, top, right, bottom.</summary>
public readonly record struct QuadRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
}

/// <summary>A texture sub-rectangle: the U and V at the quad's left/top and right/bottom edges.</summary>
public readonly record struct TexRect(float U0, float V0, float U1, float V1)
{
    public static readonly TexRect Full = new(0f, 0f, 1f, 1f);
}

/// <summary>Which end of a <c>BarBitmap</c> stays put as it fills. Left is the stock fill; the rest are GameExt's <c>FillFrom</c>.</summary>
public enum FillFrom { Left, Right, Bottom, Top }

/// <summary>
/// How a <c>BarBitmap</c> draws for a value: the stock fill and GameExt's <c>FillFrom</c>, ported
/// from BF2GameExt's <c>PatcherDLL/src/render/hud_bar_fill_from_core.hpp</c>.
/// </summary>
/// <remarks>
/// <c>ElementBarBitmap::SetValue</c> (P <c>005F7920</c>) keeps the left edge and the left U and moves
/// the other end: <c>right = left + value × width</c>, <c>u1 = u0 + value × spanU</c>. Its setup stores
/// <c>spanU = u1</c>, the right-hand U itself, so the fill is only right while <c>TexCoords</c> starts
/// at <c>u0 = 0</c>: the stock bug the preview keeps.
/// </remarks>
public static class BarFill
{
    public readonly record struct Bar(QuadRect Rect, TexRect Uv, float Width, float SpanU);

    /// <summary>The state stock <c>PostReadSetup</c> leaves.</summary>
    public static Bar Stock(QuadRect r, TexRect t) => new(r, t, r.Right - r.Left, t.U1);

    /// <summary>The same bar stored end for end so the stock fill anchors on its right (<c>FillFrom("Right")</c>).</summary>
    public static Bar AnchorRight(QuadRect r, TexRect t) =>
        new(new QuadRect(r.Right, r.Top, r.Left, r.Bottom), new TexRect(t.U1, t.V0, t.U0, t.V1),
            r.Left - r.Right, t.U0 - t.U1);

    /// <summary>
    /// <c>SetValue</c>'s geometry for a fraction: <c>ScaleSize</c> moves the edge, <c>ScaleTexture</c>
    /// crops U. Both are on by default.
    /// </summary>
    public static Bar Fill(Bar b, float value, bool scaleTexture = true, bool scaleSize = true)
    {
        var rect = b.Rect;
        var uv = b.Uv;
        if (scaleSize) rect = rect with { Right = b.Rect.Left + value * b.Width };
        if (scaleTexture) uv = uv with { U1 = b.Uv.U0 + value * b.SpanU };
        return b with { Rect = rect, Uv = uv };
    }

    /// <summary>A stock bar as its setup left it, from what it draws at any value.</summary>
    public static Bar Unfilled(Bar drawn) =>
        new(new QuadRect(drawn.Rect.Left, drawn.Rect.Top, drawn.Rect.Left + drawn.Width, drawn.Rect.Bottom),
            new TexRect(drawn.Uv.U0, drawn.Uv.V0, drawn.SpanU, drawn.Uv.V1), drawn.Width, drawn.SpanU);

    /// <summary>
    /// <c>FillFrom("Bottom")</c> and <c>("Top")</c>: GameExt lays a vertical bar out from its full
    /// rectangle, the anchored edge kept and the other moved, V cropped to match unless
    /// <c>ScaleTexture</c> is off.
    /// </summary>
    public readonly record struct Vertical(QuadRect Full, TexRect FullUv, bool FromTop, bool Crop);

    public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    public static Bar FillVertical(Vertical b, float value)
    {
        var v = Clamp01(value);
        var r = b.Full;
        var t = b.FullUv;
        var rect = r;
        var uv = t;
        if (b.FromTop)
        {
            rect = rect with { Bottom = r.Top + v * (r.Bottom - r.Top) };
            if (b.Crop) uv = uv with { V1 = t.V0 + v * (t.V1 - t.V0) };
        }
        else
        {
            rect = rect with { Top = r.Bottom + v * (r.Top - r.Bottom) };
            if (b.Crop) uv = uv with { V0 = t.V1 + v * (t.V0 - t.V1) };
        }
        return new Bar(rect, uv, 0f, 0f);
    }

    /// <summary>
    /// What a <c>BarBitmap</c> draws at a bar fraction: the stock fill for <see cref="FillFrom.Left"/>
    /// (or whenever GameExt is not there to read <c>FillFrom</c>), else GameExt's.
    /// </summary>
    public static (QuadRect Rect, TexRect Uv) Draw(QuadRect full, TexRect uv, float fraction, FillFrom from,
        bool scaleTexture, bool scaleSize) => from switch
    {
        FillFrom.Right => Pick(Fill(AnchorRight(full, uv), fraction, scaleTexture, scaleSize)),
        // GameExt clears a vertical bar's two flags and lays it out itself: the edge always moves,
        // and ScaleTexture as written decides the crop.
        FillFrom.Bottom or FillFrom.Top => Pick(FillVertical(new Vertical(full, uv, from == FillFrom.Top, scaleTexture), fraction)),
        _ => Pick(Fill(Stock(full, uv), fraction, scaleTexture, scaleSize)),
    };

    private static (QuadRect, TexRect) Pick(Bar b) => (b.Rect, b.Uv);
}
