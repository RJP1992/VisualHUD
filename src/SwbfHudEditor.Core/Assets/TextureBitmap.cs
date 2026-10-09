namespace SwbfHudEditor.Core.Assets;

public sealed record TextureBitmap(int Width, int Height, byte[] Bgra32Data, string? SourcePath = null);
