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
/// A game font's metrics from the FontEdit <c>.fff</c> file the game munges it from: "BORK", the
/// number of glyphs and the cell height, then seven bytes a glyph (the character, then its width,
/// left, top, right and bottom), then the glyph images, which the preview does not read yet.
/// </summary>
public sealed class FffFont : ITextFont
{
    private readonly Dictionary<char, FontGlyph> _glyphs;

    private FffFont(string name, int height, Dictionary<char, FontGlyph> glyphs)
    {
        Name = name;
        Height = height;
        _glyphs = glyphs;
        // Where a capital ends: the baseline. The munged font's own ascent is not in the file.
        Ascent = glyphs.TryGetValue('A', out var a) ? a.Bottom : height;
    }

    public string Name { get; }
    public int Height { get; }
    public int Ascent { get; }
    public IReadOnlyDictionary<char, FontGlyph> Glyphs => _glyphs;

    public FontGlyph? Find(char c) => _glyphs.TryGetValue(c, out var g) ? g : null;

    public static FffFont Read(string path) => Read(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));

    public static FffFont Read(byte[] data, string name)
    {
        if (data.Length < 9 || data[0] != 'B' || data[1] != 'O' || data[2] != 'R' || data[3] != 'K')
            throw new InvalidDataException($"{name}: not a FontEdit font (no BORK header)");
        var count = data[4] | data[5] << 8;
        var height = data[6] | data[7] << 8;
        if (9 + count * 7 > data.Length) throw new InvalidDataException($"{name}: the glyph table runs past the end of the file");
        var glyphs = new Dictionary<char, FontGlyph>(count);
        for (var i = 0; i < count; i++)
        {
            var o = 9 + i * 7;
            var c = (char)(data[o] | data[o + 1] << 8);
            glyphs[c] = new FontGlyph(c, data[o + 2], data[o + 3], data[o + 4], data[o + 5], data[o + 6]);
        }
        return new FffFont(name, height, glyphs);
    }
}
