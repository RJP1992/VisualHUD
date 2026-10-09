using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwbfHudEditor.Core.Assets;

namespace SwbfHudEditor.App.Views;

public sealed class TextureCache
{
    private readonly AssetResolver _resolver;
    private readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TextureCache(AssetResolver resolver)
    {
        _resolver = resolver;
    }

    public BitmapSource? Get(string textureName)
    {
        if (_cache.TryGetValue(textureName, out var existing)) return existing;

        var path = _resolver.FindTexture(textureName);
        if (path is null)
        {
            _cache[textureName] = null;
            return null;
        }

        try
        {
            var bmp = TgaLoader.Load(path);
            var source = BitmapSource.Create(
                bmp.Width, bmp.Height,
                dpiX: 96, dpiY: 96,
                PixelFormats.Bgra32,
                palette: null,
                bmp.Bgra32Data,
                stride: bmp.Width * 4);
            source.Freeze();
            _cache[textureName] = source;
            return source;
        }
        catch
        {
            _cache[textureName] = null;
            return null;
        }
    }
}
