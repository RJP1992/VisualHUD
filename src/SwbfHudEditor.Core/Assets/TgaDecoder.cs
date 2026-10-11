namespace SwbfHudEditor.Core.Assets;

/// <summary>
/// A Truevision TGA file read into 32-bit BGRA, top row first: true-colour (15, 16, 24 or 32 bits),
/// grey (8 bits, opaque) and colour-mapped images, each plain or run-length encoded, from either corner.
/// </summary>
public static class TgaDecoder
{
    private const int HeaderSize = 18;

    public static TextureBitmap Decode(ReadOnlySpan<byte> file, string? sourcePath = null)
    {
        if (file.Length < HeaderSize) throw Bad("shorter than its header", sourcePath);
        int idLength = file[0];
        int mapType = file[1];
        int imageType = file[2];
        int mapFirst = file[3] | file[4] << 8;
        int mapLength = file[5] | file[6] << 8;
        int mapBits = file[7];
        int width = file[12] | file[13] << 8;
        int height = file[14] | file[15] << 8;
        int depth = file[16];
        int descriptor = file[17];
        var alphaBits = descriptor & 0x0F;
        var rightToLeft = (descriptor & 0x10) != 0;
        var topDown = (descriptor & 0x20) != 0;

        if (width == 0 || height == 0) throw Bad("empty", sourcePath);
        var kind = imageType & ~8;   // 9, 10, 11 are 1, 2, 3 run-length encoded
        var rle = (imageType & 8) != 0;
        if (kind is not (1 or 2 or 3)) throw Bad($"image type {imageType} is not read", sourcePath);

        var at = HeaderSize + idLength;
        // The colour map, as BGRA, when the image indexes one.
        byte[]? palette = null;
        if (mapType == 1)
        {
            var entryBytes = (mapBits + 7) / 8;
            var mapSize = mapLength * entryBytes;
            if (file.Length < at + mapSize) throw Bad("cut off in its colour map", sourcePath);
            if (kind == 1)
            {
                palette = new byte[(mapFirst + mapLength) * 4];
                for (var i = 0; i < mapLength; i++)
                    Pixel(file.Slice(at + i * entryBytes, entryBytes), mapBits, alphaBits, palette.AsSpan((mapFirst + i) * 4, 4));
            }
            at += mapSize;
        }
        if (kind == 1 && palette is null) throw Bad("colour-mapped without a colour map", sourcePath);

        var pixelBytes = (depth + 7) / 8;
        if (kind == 2 && depth is not (15 or 16 or 24 or 32) || kind is 1 or 3 && depth is not (8 or 16))
            throw Bad($"{depth}-bit pixels are not read for image type {imageType}", sourcePath);

        var count = width * height;
        var pixels = new byte[count * 4];
        void Put(ReadOnlySpan<byte> source, int index)
        {
            var target = pixels.AsSpan(index * 4, 4);
            switch (kind)
            {
                case 1:
                    var entry = depth == 8 ? source[0] : source[0] | source[1] << 8;
                    if ((entry + 1) * 4 <= palette!.Length) palette.AsSpan(entry * 4, 4).CopyTo(target);
                    break;
                case 3:
                    target[0] = target[1] = target[2] = source[0];
                    target[3] = depth == 16 ? source[1] : (byte)255;
                    break;
                default:
                    Pixel(source, depth, alphaBits, target);
                    break;
            }
        }

        if (!rle)
        {
            if (file.Length < at + count * pixelBytes) throw Bad("cut off in its pixels", sourcePath);
            for (var i = 0; i < count; i++) Put(file.Slice(at + i * pixelBytes, pixelBytes), i);
        }
        else
        {
            // Packets of a run of one pixel or of raw pixels, which may run on across rows.
            var i = 0;
            while (i < count)
            {
                if (at >= file.Length) throw Bad("cut off in its run-length pixels", sourcePath);
                var header = file[at++];
                var length = Math.Min((header & 0x7F) + 1, count - i);
                if ((header & 0x80) != 0)
                {
                    if (file.Length < at + pixelBytes) throw Bad("cut off in a run", sourcePath);
                    var one = file.Slice(at, pixelBytes);
                    for (var n = 0; n < length; n++) Put(one, i++);
                    at += pixelBytes;
                }
                else
                {
                    if (file.Length < at + length * pixelBytes) throw Bad("cut off in a raw packet", sourcePath);
                    for (var n = 0; n < length; n++, at += pixelBytes) Put(file.Slice(at, pixelBytes), i++);
                }
            }
        }

        // Stored bottom row first unless the descriptor says top; right to left only when it says so.
        if (!topDown || rightToLeft)
        {
            var turned = new byte[pixels.Length];
            var row = width * 4;
            for (var y = 0; y < height; y++)
            {
                var from = (topDown ? y : height - 1 - y) * row;
                if (!rightToLeft) Buffer.BlockCopy(pixels, from, turned, y * row, row);
                else
                    for (var x = 0; x < width; x++)
                        Buffer.BlockCopy(pixels, from + (width - 1 - x) * 4, turned, y * row + x * 4, 4);
            }
            pixels = turned;
        }
        return new TextureBitmap(width, height, pixels, sourcePath);
    }

    /// <summary>One true-colour pixel as BGRA: 15 or 16 bits are 5-5-5 with an alpha bit when the image has one.</summary>
    private static void Pixel(ReadOnlySpan<byte> source, int bits, int alphaBits, Span<byte> target)
    {
        switch (bits)
        {
            case 32:
                source[..4].CopyTo(target);
                break;
            case 24:
                source[..3].CopyTo(target);
                target[3] = 255;
                break;
            case 15 or 16:
                var v = source[0] | source[1] << 8;
                target[0] = Five(v & 0x1F);
                target[1] = Five(v >> 5 & 0x1F);
                target[2] = Five(v >> 10 & 0x1F);
                target[3] = bits == 16 && alphaBits > 0 ? (v & 0x8000) != 0 ? (byte)255 : (byte)0 : (byte)255;
                break;
            default:
                target.Clear();
                break;
        }
    }

    private static byte Five(int v) => (byte)(v << 3 | v >> 2);

    private static InvalidDataException Bad(string why, string? path) =>
        new($"Not a TGA this reads: {why}" + (path is null ? "." : $" ({path})."));
}
