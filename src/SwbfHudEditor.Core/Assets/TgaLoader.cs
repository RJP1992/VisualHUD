using Pfim;

namespace SwbfHudEditor.Core.Assets;

public static class TgaLoader
{
    public static TextureBitmap Load(string path)
    {
        using var image = Pfimage.FromFile(path);
        return ToBgra32(image, path);
    }

    public static TextureBitmap Load(Stream stream, string? sourcePath = null)
    {
        using var image = Pfimage.FromStream(stream);
        return ToBgra32(image, sourcePath);
    }

    private static TextureBitmap ToBgra32(IImage image, string? sourcePath)
    {
        var w = image.Width;
        var h = image.Height;
        var dst = new byte[w * h * 4];

        switch (image.Format)
        {
            case ImageFormat.Rgba32:
                CopyRows(image.Data, image.Stride, dst, w * 4, h, w * 4);
                break;
            case ImageFormat.Rgb24:
                ExpandBgrToBgra(image.Data, image.Stride, dst, w, h);
                break;
            case ImageFormat.R5g6b5:
                Expand565ToBgra(image.Data, image.Stride, dst, w, h);
                break;
            default:
                throw new NotSupportedException($"Unsupported TGA pixel format: {image.Format} ({sourcePath})");
        }

        return new TextureBitmap(w, h, dst, sourcePath);
    }

    private static void CopyRows(byte[] src, int srcStride, byte[] dst, int dstStride, int height, int rowBytes)
    {
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(src, y * srcStride, dst, y * dstStride, rowBytes);
    }

    private static void ExpandBgrToBgra(byte[] src, int srcStride, byte[] dst, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var srcRow = y * srcStride;
            var dstRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var s = srcRow + x * 3;
                var d = dstRow + x * 4;
                dst[d + 0] = src[s + 0];
                dst[d + 1] = src[s + 1];
                dst[d + 2] = src[s + 2];
                dst[d + 3] = 255;
            }
        }
    }

    private static void Expand565ToBgra(byte[] src, int srcStride, byte[] dst, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var srcRow = y * srcStride;
            var dstRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var lo = src[srcRow + x * 2];
                var hi = src[srcRow + x * 2 + 1];
                var packed = (ushort)(lo | (hi << 8));
                var b = (packed & 0x1F) << 3;
                var g = ((packed >> 5) & 0x3F) << 2;
                var r = (packed >> 11) << 3;
                var d = dstRow + x * 4;
                dst[d + 0] = (byte)b;
                dst[d + 1] = (byte)g;
                dst[d + 2] = (byte)r;
                dst[d + 3] = 255;
            }
        }
    }
}
