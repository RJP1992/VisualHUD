using System.Windows.Media.Imaging;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Msh;

namespace SwbfHudEditor.App.Views;

/// <summary>
/// Resolves a HUD Model3D's Mesh("name") to a texture for billboard rendering on the 2D canvas.
/// Pipeline: mesh name → AssetResolver.FindMesh → MshParser.ExtractTextureNames → first name
/// → strip .tga extension → TextureCache.Get. Caches per mesh name so repeat lookups are cheap.
/// </summary>
public sealed class MeshTextureCache
{
    private readonly AssetResolver _resolver;
    private readonly TextureCache _textures;
    private readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public MeshTextureCache(AssetResolver resolver, TextureCache textures)
    {
        _resolver = resolver;
        _textures = textures;
    }

    public BitmapSource? Get(string meshName)
    {
        if (_cache.TryGetValue(meshName, out var cached)) return cached;

        var meshPath = _resolver.FindMesh(meshName);
        if (meshPath is null) return Cache(meshName, null);

        try
        {
            var names = MshParser.ExtractTextureNames(meshPath);
            if (names.Count == 0) return Cache(meshName, null);
            return Cache(meshName, _textures.Get(StripTgaExtension(names[0])));
        }
        catch
        {
            return Cache(meshName, null);
        }
    }

    private BitmapSource? Cache(string meshName, BitmapSource? bmp)
    {
        _cache[meshName] = bmp;
        return bmp;
    }

    private static string StripTgaExtension(string name) =>
        name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
