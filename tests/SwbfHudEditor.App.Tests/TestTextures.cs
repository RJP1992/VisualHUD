namespace SwbfHudEditor.App.Tests;

/// <summary>Textures for the tests, written as the game's files are.</summary>
public static class TestTextures
{
    /// <summary>A 32-bit TGA, top row first, every pixel this colour.</summary>
    public static byte[] Tga(int width, int height, byte r, byte g, byte b, byte a)
    {
        var bytes = new byte[18 + width * height * 4];
        bytes[2] = 2;
        bytes[12] = (byte)width;
        bytes[13] = (byte)(width >> 8);
        bytes[14] = (byte)height;
        bytes[15] = (byte)(height >> 8);
        bytes[16] = 32;
        bytes[17] = 0x28;
        for (var i = 18; i < bytes.Length; i += 4)
        {
            bytes[i] = b;
            bytes[i + 1] = g;
            bytes[i + 2] = r;
            bytes[i + 3] = a;
        }
        return bytes;
    }
}
