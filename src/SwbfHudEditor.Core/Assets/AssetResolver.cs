namespace SwbfHudEditor.Core.Assets;

public sealed class AssetResolver
{
    private readonly List<string> _textureRoots = new();
    private readonly List<string> _meshRoots = new();
    private readonly List<string> _localizeRoots = new();

    // Lazily-built filename-without-extension → full-path indexes. One per root, rebuilt when a root
    // is accessed for the first time. We recurse so mods that stash textures in subfolders (e.g.
    // interface/hud/, interface/ammo/) are still found.
    private readonly Dictionary<string, Dictionary<string, string>> _textureIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _meshIndex = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> TextureRoots => _textureRoots;
    public IReadOnlyList<string> MeshRoots => _meshRoots;
    public IReadOnlyList<string> LocalizeRoots => _localizeRoots;

    public void AddTextureRoot(string path)
    {
        if (Directory.Exists(path) && !_textureRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
            _textureRoots.Add(path);
    }

    public void AddMeshRoot(string path)
    {
        if (Directory.Exists(path) && !_meshRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
            _meshRoots.Add(path);
    }

    public void AddLocalizeRoot(string path)
    {
        if (Directory.Exists(path) && !_localizeRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
            _localizeRoots.Add(path);
    }

    public string? FindLocalizationCfg(string language = "english")
    {
        foreach (var root in _localizeRoots)
        {
            var candidate = Path.Combine(root, language + ".cfg");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public string? FindTexture(string textureName)
    {
        foreach (var root in _textureRoots)
        {
            var index = GetOrBuildIndex(root, "*.tga", _textureIndex);
            if (index.TryGetValue(textureName, out var path)) return path;
        }
        return null;
    }

    public string? FindMesh(string meshName)
    {
        foreach (var root in _meshRoots)
        {
            var index = GetOrBuildIndex(root, "*.msh", _meshIndex);
            if (index.TryGetValue(meshName, out var path)) return path;
        }
        return null;
    }

    private static Dictionary<string, string> GetOrBuildIndex(
        string root, string pattern, Dictionary<string, Dictionary<string, string>> cache)
    {
        if (cache.TryGetValue(root, out var existing)) return existing;

        var idx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(root))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    // First match wins — keeps lookup deterministic if duplicate names exist in
                    // different subfolders.
                    idx.TryAdd(name, file);
                }
            }
            catch (UnauthorizedAccessException) { /* skip roots we can't walk */ }
            catch (IOException) { /* skip on transient IO errors */ }
        }
        cache[root] = idx;
        return idx;
    }

    public static AssetResolver ForHudFile(string hudFilePath, string? stockDataCommonPath = null)
    {
        var resolver = new AssetResolver();
        // hud file lives at <root>/hud/<name>.hud. Textures can live in several places:
        //   hud/<subfolders>/  — mods often bundle their textures here (BF3-style HUDs nest
        //                        hud/soldier_bars/, hud/reticules/, hud/targeting/, etc.).
        //                        We add the hud folder itself as a texture root and the index
        //                        recursively walks all subfolders.
        //   interface/         — standard HUD textures (stock convention)
        //   effects/           — placeholder "white.tga" and similar solid-color tints used by
        //                        meshes like hud_weapon_backdrop.
        //   mshs/              — some meshes bundle their texture alongside the .msh
        // Meshes come from mshs/. Localization from Localize/.
        var hudDir = Path.GetDirectoryName(hudFilePath);
        if (hudDir is not null)
        {
            resolver.AddTextureRoot(hudDir);
            var common = Path.GetDirectoryName(hudDir);
            if (common is not null) AddRootsForCommon(resolver, common);
        }
        if (stockDataCommonPath is not null) AddRootsForCommon(resolver, stockDataCommonPath);
        return resolver;
    }

    private static void AddRootsForCommon(AssetResolver resolver, string common)
    {
        resolver.AddTextureRoot(Path.Combine(common, "interface"));
        resolver.AddTextureRoot(Path.Combine(common, "effects"));
        resolver.AddTextureRoot(Path.Combine(common, "mshs"));
        resolver.AddMeshRoot(Path.Combine(common, "mshs"));
        resolver.AddLocalizeRoot(Path.Combine(common, "Localize"));
    }
}
