using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App.Views;

/// <summary>
/// The fonts a HUD's text is laid out and drawn with. A game font comes from its <c>.fff</c> file:
/// lines break and clip where the game's do, and each character is the game's own glyph image. A font
/// that cannot be found is stood in for with a system typeface at the stock size of its name.
/// </summary>
public sealed class TextFonts
{
    // The stock fonts' cell heights (data\Common\fonts), for a stand-in when a file is missing.
    private static readonly Dictionary<string, int> StockHeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gamefont_large"] = 22,
        ["gamefont_medium"] = 19,
        ["gamefont_small"] = 17,
        ["gamefont_tiny"] = 13,
        ["gamefont_super_tiny"] = 13,
    };

    private readonly AssetResolver? _assets;
    private readonly Dictionary<string, TextFont> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TextFonts(AssetResolver? assets) => _assets = assets;

    public TextFont For(string? name)
    {
        var key = name ?? string.Empty;
        if (_cache.TryGetValue(key, out var font)) return font;
        ITextFont? metrics = null;
        if (name is not null && _assets?.FindFont(name) is { } path)
        {
            try { metrics = FffFont.Read(path); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        metrics ??= new StandInFont(name is not null && StockHeights.TryGetValue(name, out var height) ? height : 17);
        return _cache[key] = new TextFont(metrics);
    }
}

/// <summary>A font's metrics, and what draws its glyphs: the game font's images, or a system typeface.</summary>
public sealed class TextFont
{
    private static readonly GlyphTypeface? Face = LoadFace();

    public TextFont(ITextFont metrics)
    {
        Metrics = metrics;
        // The em size that gives the system face the game font's capital height.
        var cap = metrics is FffFont && metrics.Find('A') is { } a && a.Bottom > a.Top ? a.Bottom - a.Top : metrics.Height * 0.53;
        EmSize = Face is null || Face.CapsHeight <= 0 ? metrics.Height : cap / Face.CapsHeight;
    }

    public ITextFont Metrics { get; }

    /// <summary>Whether the metrics are the game font's own, from its file.</summary>
    public bool FromGame => Metrics is FffFont;

    internal static GlyphTypeface? Typeface => Face;
    internal double EmSize { get; }

    /// <summary>
    /// The text in one image, for a game font: each glyph's image at its ink box in the cell, in the
    /// text's colour, its coverage at the sixteen levels the game's textures keep, with the shadow one
    /// pixel down and right under it. Null for a stand-in font, which <see cref="Draw"/> draws.
    /// <paramref name="area"/> is where the image goes, in the element's own pixels.
    /// </summary>
    public BitmapSource? Render(IReadOnlyList<PlacedGlyph> glyphs, Color color, bool shadow, out Rect area) =>
        Render(glyphs, color, shadow, false, out area);

    /// <summary>The text in one image; <paramref name="additive"/>: as a premultiplied colour with no alpha, which adds.</summary>
    public BitmapSource? Render(IReadOnlyList<PlacedGlyph> glyphs, Color color, bool shadow, bool additive, out Rect area)
    {
        area = Rect.Empty;
        if (Metrics is not FffFont font) return null;
        var placed = new List<(GlyphImage Image, double X, double Y, double ScaleX, double ScaleY)>();
        foreach (var g in glyphs)
        {
            if (font.ImageOf(g.Char) is not { Width: > 0, Height: > 0 } image) continue;
            var x = g.X + g.Glyph.Left * g.ScaleX;
            var y = g.Y + g.Glyph.Top * g.ScaleY;
            placed.Add((image, x, y, g.ScaleX, g.ScaleY));
            area.Union(new Rect(x, y, image.Width * g.ScaleX, image.Height * g.ScaleY));
            if (shadow) area.Union(new Rect(x + 1, y + 1, image.Width * g.ScaleX, image.Height * g.ScaleY));
        }
        if (area.IsEmpty) return null;
        var left = (int)Math.Floor(area.Left);
        var top = (int)Math.Floor(area.Top);
        var width = Math.Min(8192, (int)Math.Ceiling(area.Right) - left);
        var height = Math.Min(8192, (int)Math.Ceiling(area.Bottom) - top);
        if (width <= 0 || height <= 0) return null;

        // Coverage of the shadow and of the text, then the text over its shadow.
        var shadowCover = shadow ? new float[width * height] : null;
        var textCover = new float[width * height];
        foreach (var (image, x, y, sx, sy) in placed)
        {
            if (shadowCover is not null) Stamp(shadowCover, width, height, image, x + 1 - left, y + 1 - top, sx, sy);
            Stamp(textCover, width, height, image, x - left, y - top, sx, sy);
        }
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < textCover.Length; i++)
        {
            var t = textCover[i];
            var a = t + (shadowCover is null ? 0f : shadowCover[i] * (1f - t));
            if (a <= 0f) continue;
            pixels[i * 4] = (byte)(color.B * t + 0.5f);       // premultiplied: the shadow adds black
            pixels[i * 4 + 1] = (byte)(color.G * t + 0.5f);
            pixels[i * 4 + 2] = (byte)(color.R * t + 0.5f);
            pixels[i * 4 + 3] = additive ? (byte)0 : (byte)(Math.Min(1f, a) * 255f + 0.5f);
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze();
        area = new Rect(left, top, width, height);
        return bitmap;
    }

    /// <summary>A glyph's coverage laid into a buffer at (x, y), scaled; its pixel centres sampled between texels.</summary>
    private static void Stamp(float[] into, int width, int height, GlyphImage image, double x, double y, double sx, double sy)
    {
        if (sx == 1 && sy == 1 && x == Math.Floor(x) && y == Math.Floor(y))
        {
            // The common case: texel to pixel.
            int ox = (int)x, oy = (int)y;
            for (var j = 0; j < image.Height; j++)
            {
                var py = oy + j;
                if ((uint)py >= (uint)height) continue;
                for (var i = 0; i < image.Width; i++)
                {
                    var px = ox + i;
                    if ((uint)px >= (uint)width) continue;
                    var c = GlyphImage.InGame(image.Alpha[j * image.Width + i]) / 255f;
                    var at = py * width + px;
                    into[at] = c + into[at] * (1f - c);
                }
            }
            return;
        }
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = (int)Math.Ceiling(x + image.Width * sx);
        var y1 = (int)Math.Ceiling(y + image.Height * sy);
        for (var py = Math.Max(0, y0); py < Math.Min(height, y1); py++)
            for (var px = Math.Max(0, x0); px < Math.Min(width, x1); px++)
            {
                var u = (px + 0.5 - x) / sx - 0.5;
                var v = (py + 0.5 - y) / sy - 0.5;
                var c = Sample(image, u, v);
                if (c <= 0f) continue;
                var at = py * width + px;
                into[at] = c + into[at] * (1f - c);
            }
    }

    private static float Sample(GlyphImage image, double u, double v)
    {
        if (u < -1 || v < -1 || u > image.Width || v > image.Height) return 0f;
        var iu = (int)Math.Floor(u);
        var iv = (int)Math.Floor(v);
        var fu = (float)(u - iu);
        var fv = (float)(v - iv);
        float Texel(int i, int j) => (uint)i < (uint)image.Width && (uint)j < (uint)image.Height
            ? GlyphImage.InGame(image.Alpha[j * image.Width + i]) / 255f
            : 0f;
        var top = Texel(iu, iv) * (1 - fu) + Texel(iu + 1, iv) * fu;
        var bottom = Texel(iu, iv + 1) * (1 - fu) + Texel(iu + 1, iv + 1) * fu;
        return top * (1 - fv) + bottom * fv;
    }

    /// <summary>Draw a stand-in font's characters where the layout placed them, in the element's own pixels.</summary>
    public void Draw(DrawingContext dc, IReadOnlyList<PlacedGlyph> glyphs, Brush brush)
    {
        if (Face is null) return;
        foreach (var g in glyphs)
        {
            if (!Face.CharacterToGlyphMap.TryGetValue(g.Char, out var index)) continue;
            dc.PushTransform(new MatrixTransform(g.ScaleX, 0, 0, g.ScaleY, g.X, g.Y));
            // The stand-in glyph centred on where the game's glyph has its pixels.
            var advance = Face.AdvanceWidths[index] * EmSize;
            var left = g.Glyph.Left + (g.Glyph.Right - g.Glyph.Left - advance) / 2;
            var run = new GlyphRun(Face, 0, false, EmSize, 1.0f, new[] { index }, new Point(left, Metrics.Ascent),
                new[] { advance }, null, null, null, null, null, null);
            dc.DrawGlyphRun(brush, run);
            dc.Pop();
        }
    }

    private static GlyphTypeface? LoadFace()
    {
        foreach (var family in new[] { "Bahnschrift", "Segoe UI", "Arial" })
        {
            var typeface = new System.Windows.Media.Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (typeface.TryGetGlyphTypeface(out var face)) return face;
        }
        return null;
    }
}

/// <summary>
/// Metrics for a game font that cannot be found: its stock height, the system face's advances, and
/// the baseline where the stock fonts have it (14 of 17 pixels down).
/// </summary>
internal sealed class StandInFont(int height) : ITextFont
{
    private readonly Dictionary<char, FontGlyph?> _glyphs = new();

    public int Height => height;
    public int Ascent => (int)Math.Round(height * 14.0 / 17.0);

    public FontGlyph? Find(char c)
    {
        if (_glyphs.TryGetValue(c, out var known)) return known;
        FontGlyph? glyph = null;
        if (TextFont.Typeface is { } face && face.CharacterToGlyphMap.TryGetValue(c, out var index))
        {
            var em = height * 0.53 / Math.Max(face.CapsHeight, 0.1);
            var width = (int)Math.Round(face.AdvanceWidths[index] * em);
            glyph = new FontGlyph(c, width, 0, 0, char.IsWhiteSpace(c) ? 0 : Math.Max(1, width), height);
        }
        return _glyphs[c] = glyph;
    }
}

/// <summary>A text element's characters as the game places them, and its box when that is shown.</summary>
internal sealed class TextGlyphsVisual : FrameworkElement
{
    private static readonly Pen BoxPen = CreateBoxPen();

    private readonly TextLayoutResult _layout;
    private readonly TextFont _font;
    private readonly Brush _brush;
    private readonly bool _shadow;
    private readonly Rect? _box;
    private readonly Rect _bounds;
    // A game font's text, drawn once into an image, and its shadow: black, at half the alpha, offset.
    private readonly BitmapSource? _image;
    private readonly Rect _imageArea;
    private readonly BitmapSource? _shadowImage;
    private readonly Rect _shadowArea;
    private readonly Vector _shadowOffset;

    public TextGlyphsVisual(TextLayoutResult layout, TextFont font, Brush brush, bool shadow, Vector shadowOffset, bool additive, Rect? box)
    {
        _layout = layout;
        _font = font;
        _brush = brush;
        _shadow = shadow;
        _shadowOffset = shadowOffset;
        _box = box;
        if (brush is not SolidColorBrush solid) return;
        _image = font.Render(layout.Glyphs, solid.Color, false, additive, out _imageArea);
        if (shadow) _shadowImage = font.Render(layout.Glyphs, Colors.Black, false, false, out _shadowArea);
        // What can be clicked: the lines and the box.
        var text = Rect.Empty;
        foreach (var line in layout.Lines)
            text.Union(new Rect(line.X, line.Y, Math.Max(line.Width, 1), Math.Max(layout.LineHeight, 1)));
        TextExtent = text;
        var bounds = text;
        if (box is { } b) bounds.Union(b);
        _bounds = bounds;
    }

    /// <summary>Where the text's lines are, without its box, in the element's own pixels.</summary>
    public Rect TextExtent { get; }

    /// <summary>Where the text and its box are, in the element's own pixels.</summary>
    public Rect Bounds => _bounds;

    /// <summary>Where its shadow is drawn, in its own pixels, when it has one: for tests.</summary>
    internal Vector? ShadowAt => _shadow ? _shadowOffset : null;

    protected override void OnRender(DrawingContext dc)
    {
        if (!_bounds.IsEmpty) dc.DrawRectangle(Brushes.Transparent, null, _bounds);
        if (_box is { } box) dc.DrawRectangle(null, BoxPen, box);
        if (_image is not null)
        {
            // The interface shader's shadow pass, then the text (pcInterfaceShader::Render_Prog1, P 0040C7DE).
            if (_shadowImage is not null)
            {
                dc.PushOpacity(0.5);
                dc.DrawImage(_shadowImage, new Rect(_shadowArea.TopLeft + _shadowOffset, _shadowArea.Size));
                dc.Pop();
            }
            dc.DrawImage(_image, _imageArea);
            return;
        }
        if (_font.FromGame) return;
        if (_shadow)
        {
            dc.PushOpacity(0.5);
            dc.PushTransform(new TranslateTransform(_shadowOffset.X, _shadowOffset.Y));
            _font.Draw(dc, _layout.Glyphs, Brushes.Black);
            dc.Pop();
            dc.Pop();
        }
        _font.Draw(dc, _layout.Glyphs, _brush);
    }

    private static Pen CreateBoxPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 143, 184, 222)), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        pen.Freeze();
        return pen;
    }
}
