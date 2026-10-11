using SwbfHudEditor.Core.Assets;

namespace SwbfHudEditor.Core.Tests.Assets;

/// <summary>TGA files of each kind read into BGRA, top row first.</summary>
public class TgaDecoderTests
{
    private static byte[] Tga(int type, int depth, int width, int height, int descriptor, byte[] pixels, byte[]? map = null, int mapBits = 0, int mapFirst = 0)
    {
        var header = new byte[18];
        header[1] = (byte)(map is null ? 0 : 1);
        header[2] = (byte)type;
        if (map is not null)
        {
            var entries = map.Length / ((mapBits + 7) / 8);
            header[3] = (byte)mapFirst;
            header[5] = (byte)entries;
            header[6] = (byte)(entries >> 8);
            header[7] = (byte)mapBits;
        }
        header[12] = (byte)width;
        header[13] = (byte)(width >> 8);
        header[14] = (byte)height;
        header[15] = (byte)(height >> 8);
        header[16] = (byte)depth;
        header[17] = (byte)descriptor;
        return header.Concat(map ?? Array.Empty<byte>()).Concat(pixels).ToArray();
    }

    /// <summary>The pixel at (x, y) as B, G, R, A.</summary>
    private static byte[] At(TextureBitmap image, int x, int y) => image.Bgra32Data.AsSpan((y * image.Width + x) * 4, 4).ToArray();

    [Fact]
    public void True_Colour_Stored_Bottom_Row_First_Comes_Out_Top_Row_First()
    {
        // 24-bit, bottom row first: the bottom row is blue, green; the top row red, white.
        var image = TgaDecoder.Decode(Tga(2, 24, 2, 2, 0x00, new byte[]
        {
            255, 0, 0, 0, 255, 0,
            0, 0, 255, 255, 255, 255,
        }));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, At(image, 0, 0));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, At(image, 1, 0));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, At(image, 0, 1));
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, At(image, 1, 1));
    }

    [Fact]
    public void Thirty_Two_Bits_Keep_Their_Alpha_And_Right_To_Left_Is_Turned()
    {
        var pixels = new byte[] { 1, 2, 3, 40, 5, 6, 7, 80 };
        var image = TgaDecoder.Decode(Tga(2, 32, 2, 1, 0x28, pixels));
        Assert.Equal(pixels, image.Bgra32Data);
        var turned = TgaDecoder.Decode(Tga(2, 32, 2, 1, 0x38, pixels));
        Assert.Equal(new byte[] { 5, 6, 7, 80, 1, 2, 3, 40 }, turned.Bgra32Data);
    }

    [Fact]
    public void Run_Length_Packets_Run_On_Across_Rows()
    {
        // 3 x 2, top row first: a run of four of one colour, then two raw pixels.
        var image = TgaDecoder.Decode(Tga(10, 32, 3, 2, 0x28, new byte[]
        {
            0x83, 10, 20, 30, 255,
            0x01, 1, 1, 1, 128, 2, 2, 2, 64,
        }));
        for (var i = 0; i < 4; i++) Assert.Equal(new byte[] { 10, 20, 30, 255 }, At(image, i % 3, i / 3));
        Assert.Equal(new byte[] { 1, 1, 1, 128 }, At(image, 1, 1));
        Assert.Equal(new byte[] { 2, 2, 2, 64 }, At(image, 2, 1));
    }

    [Fact]
    public void Grey_Is_Opaque_Grey_Plain_Or_Run_Length()
    {
        var plain = TgaDecoder.Decode(Tga(3, 8, 2, 1, 0x20, new byte[] { 0, 200 }));
        Assert.Equal(new byte[] { 0, 0, 0, 255, 200, 200, 200, 255 }, plain.Bgra32Data);
        var packed = TgaDecoder.Decode(Tga(11, 8, 2, 1, 0x20, new byte[] { 0x81, 90 }));
        Assert.Equal(new byte[] { 90, 90, 90, 255, 90, 90, 90, 255 }, packed.Bgra32Data);
    }

    [Fact]
    public void Sixteen_Bits_Are_Five_Five_Five_With_The_Alpha_Bit_Where_The_File_Has_One()
    {
        // Red with the alpha bit clear, green with it set.
        var pixels = new byte[] { 0x00, 0x7C, 0xE0, 0x83 };
        var withAlpha = TgaDecoder.Decode(Tga(2, 16, 2, 1, 0x21, pixels));
        Assert.Equal(new byte[] { 0, 0, 255, 0 }, At(withAlpha, 0, 0));
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, At(withAlpha, 1, 0));
        var opaque = TgaDecoder.Decode(Tga(2, 16, 2, 1, 0x20, pixels));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, At(opaque, 0, 0));
    }

    [Fact]
    public void Colour_Mapped_Pixels_Take_Their_Maps_Colours()
    {
        // Two 24-bit entries from index 5: blue, red.
        var image = TgaDecoder.Decode(Tga(1, 8, 2, 1, 0x20, new byte[] { 6, 5 }, new byte[] { 255, 0, 0, 0, 0, 255 }, 24, mapFirst: 5));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, At(image, 0, 0));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, At(image, 1, 0));
    }

    [Fact]
    public void A_File_Cut_Off_Or_Of_Another_Kind_Is_Refused()
    {
        Assert.Throws<InvalidDataException>(() => TgaDecoder.Decode(Tga(2, 32, 2, 2, 0x28, new byte[8])));
        Assert.Throws<InvalidDataException>(() => TgaDecoder.Decode(Tga(10, 32, 2, 2, 0x28, new byte[] { 0x83 })));
        Assert.Throws<InvalidDataException>(() => TgaDecoder.Decode(Tga(32, 8, 1, 1, 0, new byte[1])));
        Assert.Throws<InvalidDataException>(() => TgaDecoder.Decode(new byte[10]));
    }
}
