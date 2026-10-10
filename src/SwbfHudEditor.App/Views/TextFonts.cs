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
    // Each glyph's coverage as an opacity mask, as the game's four-bit texture holds it.
    private readonly Dictionary<char, ImageBrush?> _masks = new();

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

    /// <summary>Draw the characters where the layout placed them, in the element's own pixels.</summary>
    public void Draw(DrawingContext dc, IReadOnlyList<PlacedGlyph> glyphs, Brush brush)
    {
        if (Metrics is FffFont font)
        {
            // The glyph's image at its ink box in the cell, filled with the text's colour.
            foreach (var g in glyphs)
            {
                if (font.ImageOf(g.Char) is not { } image || MaskOf(g.Char, image) is not { } mask) continue;
                dc.PushTransform(new MatrixTransform(g.ScaleX, 0, 0, g.ScaleY, g.X, g.Y));
                dc.PushOpacityMask(mask);
                dc.DrawRectangle(brush, null, new Rect(g.Glyph.Left, g.Glyph.Top, image.Width, image.Height));
                dc.Pop();
                dc.Pop();
            }
            return;
        }
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

    private ImageBrush? MaskOf(char c, GlyphImage image)
    {
        if (_masks.TryGetValue(c, out var cached)) return cached;
        var pixels = new byte[image.Width * image.Height * 4];
        for (var i = 0; i < image.Alpha.Length; i++)
        {
            var a = GlyphImage.InGame(image.Alpha[i]);
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = pixels[i * 4 + 3] = a;
        }
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, image.Width * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        brush.Freeze();
        return _masks[c] = brush;
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

    public TextGlyphsVisual(TextLayoutResult layout, TextFont font, Brush brush, bool shadow, Rect? box)
    {
        _layout = layout;
        _font = font;
        _brush = brush;
        _shadow = shadow;
        _box = box;
        // What can be clicked: the lines and the box.
        var bounds = Rect.Empty;
        foreach (var line in layout.Lines)
            bounds.Union(new Rect(line.X, line.Y, Math.Max(line.Width, 1), Math.Max(layout.LineHeight, 1)));
        if (box is { } b) bounds.Union(b);
        _bounds = bounds;
    }

    /// <summary>Where the text and its box are, in the element's own pixels.</summary>
    public Rect Bounds => _bounds;

    protected override void OnRender(DrawingContext dc)
    {
        if (!_bounds.IsEmpty) dc.DrawRectangle(Brushes.Transparent, null, _bounds);
        if (_box is { } box) dc.DrawRectangle(null, BoxPen, box);
        if (_shadow)
        {
            dc.PushTransform(new TranslateTransform(1, 1));
            _font.Draw(dc, _layout.Glyphs, Brushes.Black);
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
