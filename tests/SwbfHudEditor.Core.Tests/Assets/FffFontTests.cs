using SwbfHudEditor.Core.Assets;

namespace SwbfHudEditor.Core.Tests.Assets;

/// <summary>A FontEdit font's glyph images, read as FontEdit writes them.</summary>
public class FffFontTests
{
    /// <summary>A font of two glyphs: 'A' with a 2×3 image, and a space with none.</summary>
    private static byte[] TwoGlyphs()
    {
        var bytes = new List<byte> { (byte)'B', (byte)'O', (byte)'R', (byte)'K', 2, 0, 17, 0, 0 };
        bytes.AddRange(new byte[] { 0x20, 0, 4, 0, 0, 0, 0 });        // ' ': advance 4, no ink
        bytes.AddRange(new byte[] { 0x41, 0, 9, 1, 4, 3, 7 });        // 'A': advance 9, ink (1,4)-(3,7)
        bytes.AddRange(new byte[] { 0, 0 });                          // ' ': no image
        bytes.AddRange(new byte[] { 2, 3, 0, 255, 16, 17, 128, 254 }); // 'A': 2×3
        return bytes.ToArray();
    }

    [Fact]
    public void Each_Glyph_Has_Its_Image_After_The_Table()
    {
        var font = FffFont.Read(TwoGlyphs(), "test");
        Assert.Equal(17, font.Height);
        Assert.Null(font.ImageOf(' '));
        var a = font.ImageOf('A')!;
        Assert.Equal(2, a.Width);
        Assert.Equal(3, a.Height);
        Assert.Equal(new byte[] { 0, 255, 16, 17, 128, 254 }, a.Alpha);
        Assert.Equal(new FontGlyph('A', 9, 1, 4, 3, 7), font.Find('A'));
    }

    [Fact]
    public void The_Game_Draws_Coverage_With_Four_Bits()
    {
        Assert.Equal(0, GlyphImage.InGame(16));
        Assert.Equal(17, GlyphImage.InGame(17));
        Assert.Equal(119, GlyphImage.InGame(128));
        Assert.Equal(255, GlyphImage.InGame(255));
    }

    [Fact]
    public void The_Stock_Fonts_Read_To_Their_End()
    {
        var folder = @"C:\BF2_ModTools\data\Common\fonts";
        if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.EnumerateFiles(folder, "*.fff"))
        {
            var font = FffFont.Read(path);
            var data = File.ReadAllBytes(path);
            // Every glyph with ink has an image the size of its box, and the images fill the file.
            var size = 9 + font.Glyphs.Count * 7 + font.Glyphs.Count * 2 + font.Images.Values.Sum(i => i.Width * i.Height);
            Assert.Equal(data.Length, size);
            foreach (var (c, glyph) in font.Glyphs.Where(g => g.Value.Printing))
            {
                var image = font.ImageOf(c);
                Assert.NotNull(image);
                Assert.Equal(glyph.Right - glyph.Left, image!.Width);
                Assert.Equal(glyph.Bottom - glyph.Top, image.Height);
            }
            Assert.Contains(font.ImageOf('A')!.Alpha, b => b == 255);
        }
    }
}
