namespace SwbfHudEditor.Core.Assets;

/// <summary>A TGA texture from a file or a stream, as BGRA (<see cref="TgaDecoder"/>).</summary>
public static class TgaLoader
{
    public static TextureBitmap Load(string path) => TgaDecoder.Decode(File.ReadAllBytes(path), path);

    public static TextureBitmap Load(Stream stream, string? sourcePath = null)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return TgaDecoder.Decode(copy.GetBuffer().AsSpan(0, (int)copy.Length), sourcePath);
    }
}
