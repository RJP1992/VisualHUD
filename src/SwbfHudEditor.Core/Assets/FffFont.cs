namespace SwbfHudEditor.Core.Assets;

/// <summary>
/// One character of a game font: the advance text moves by, <see cref="Width"/>, and the glyph's
/// box in the font's cell, as the munged <c>Glyph</c> the text layout reads keeps them.
/// </summary>
public readonly record struct FontGlyph(char Char, int Width, int Left, int Top, int Right, int Bottom)
{
    /// <summary><c>RedVariableWidthFont::IsPrintingChar</c> (P <c>008C82F0</c>): the glyph has pixels.</summary>
    public bool Printing => Left < Right;
}

/// <summary>
/// A glyph's pixels: <see cref="Width"/> × <see cref="Height"/> bytes of ink coverage, rows top to
/// bottom, 0 none and 255 full, drawn at the glyph's left and top in its cell.
/// </summary>
public sealed record GlyphImage(int Width, int Height, byte[] Alpha)
{
    /// <summary>
    /// The coverage as the game draws it: FontMunge packs it into a texture with four bits of alpha,
    /// the byte divided by 17, so the faintest level the file has (16) draws as nothing.
    /// </summary>
    public static byte InGame(byte alpha) => (byte)(alpha / 17 * 17);
}

/// <summary>What the text layout needs of a font.</summary>
public interface ITextFont
{
    /// <summary>The cell height, the line height at a text scale of 1 (<c>RedTextElement::GetTextHeight</c>).</summary>
    int Height { get; }

    /// <summary>The baseline's distance from the top of the cell.</summary>
    int Ascent { get; }

    FontGlyph? Find(char c);
}

/// <summary>
/// A game font from the FontEdit <c>.fff</c> file the game munges it from: "BORK", the number of
/// glyphs (u16), the cell height (u8) and two bytes always 0; seven bytes a glyph (the character as
/// u16, then its advance, left, top, right and bottom); then each glyph's image in that order, its
/// width and height (u8 each, the ink box's) and one byte of coverage a pixel, to the end of the file.
/// Read from FontEdit and FontMunge, and checked against the munged fonts' textures.
/// </summary>
public sealed class FffFont : ITextFont
{
    private readonly Dictionary<char, FontGlyph> _glyphs;
    private readonly Dictionary<char, GlyphImage> _images;

    private FffFont(string name, int height, Dictionary<char, FontGlyph> glyphs, Dictionary<char, GlyphImage> images)
    {
        Name = name;
        Height = height;
        _glyphs = glyphs;
        _images = images;
        // Where a capital ends: the baseline. The munged font's own ascent is not in the file.
        Ascent = glyphs.TryGetValue('A', out var a) ? a.Bottom : height;
    }

    public string Name { get; }
    public int Height { get; }
    public int Ascent { get; }
    public IReadOnlyDictionary<char, FontGlyph> Glyphs => _glyphs;

    /// <summary>The glyphs' pixels, for those that have ink.</summary>
    public IReadOnlyDictionary<char, GlyphImage> Images => _images;

    public FontGlyph? Find(char c) => _glyphs.TryGetValue(c, out var g) ? g : null;

    public GlyphImage? ImageOf(char c) => _images.TryGetValue(c, out var image) ? image : null;

    public static FffFont Read(string path) => Read(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));

    public static FffFont Read(byte[] data, string name)
    {
        if (data.Length < 9 || data[0] != 'B' || data[1] != 'O' || data[2] != 'R' || data[3] != 'K')
            throw new InvalidDataException($"{name}: not a FontEdit font (no BORK header)");
        var count = data[4] | data[5] << 8;
        var height = data[6];
        if (9 + count * 7 > data.Length) throw new InvalidDataException($"{name}: the glyph table runs past the end of the file");
        var glyphs = new Dictionary<char, FontGlyph>(count);
        var order = new List<char>(count);
        for (var i = 0; i < count; i++)
        {
            var o = 9 + i * 7;
            var c = (char)(data[o] | data[o + 1] << 8);
            glyphs[c] = new FontGlyph(c, data[o + 2], data[o + 3], data[o + 4], data[o + 5], data[o + 6]);
            order.Add(c);
        }

        // The images, a glyph after another; a damaged file keeps the ones it has.
        var images = new Dictionary<char, GlyphImage>();
        var p = 9 + count * 7;
        foreach (var c in order)
        {
            if (p + 2 > data.Length) break;
            int w = data[p], h = data[p + 1];
            if (p + 2 + w * h > data.Length) break;
            if (w > 0 && h > 0) images[c] = new GlyphImage(w, h, data.AsSpan(p + 2, w * h).ToArray());
            p += 2 + w * h;
        }
        return new FffFont(name, height, glyphs, images);
    }
}
