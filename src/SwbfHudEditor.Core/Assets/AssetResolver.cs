namespace SwbfHudEditor.Core.Assets;

/// <summary>
/// Finds the files a HUD names: in its own folders and the stock ones first, then in the asset
/// folders added to it, which fill in what those do not have.
/// </summary>
public sealed class AssetResolver
{
    private readonly List<string> _textureRoots = new();
    private readonly List<string> _meshRoots = new();
    private readonly List<string> _fontRoots = new();
    private readonly List<string> _localizeRoots = new();
    private readonly List<AssetFolderIndex> _folders = new();

    // Lazily-built filename-without-extension → full-path indexes. One per root, rebuilt when a root
    // is accessed for the first time. We recurse so mods that stash textures in subfolders (e.g.
    // interface/hud/, interface/ammo/) are still found.
    private readonly Dictionary<string, Dictionary<string, string>> _textureIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _meshIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _fontIndex = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> TextureRoots => _textureRoots;
    public IReadOnlyList<string> MeshRoots => _meshRoots;
    public IReadOnlyList<string> FontRoots => _fontRoots;
    public IReadOnlyList<string> LocalizeRoots => _localizeRoots;
    public IReadOnlyList<AssetFolderIndex> Folders => _folders;

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

    public void AddFontRoot(string path)
    {
        if (Directory.Exists(path) && !_fontRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
            _fontRoots.Add(path);
    }

    public void AddLocalizeRoot(string path)
    {
        if (Directory.Exists(path) && !_localizeRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
            _localizeRoots.Add(path);
    }

    /// <summary>An asset folder, searched with all its subfolders after every root, in the order added.</summary>
    public void AddFolder(AssetFolderIndex folder) => _folders.Add(folder);

    public string? FindLocalizationCfg(string language = "english")
    {
        foreach (var root in _localizeRoots)
        {
            var candidate = Path.Combine(root, language + ".cfg");
            if (File.Exists(candidate)) return candidate;
        }
        return InFolders(f => f.Localizations, language);
    }

    /// <summary>
    /// The localization files the game ends up with, in the order they lay over each other: each
    /// <c>Localize</c> folder from the stock one to the HUD's own, its <c>language.cfg</c> and then
    /// the PC build's <c>PC\language.cfg</c>, which the munger lays over it. Without a Localize
    /// folder, the first file an asset folder has.
    /// </summary>
    public IReadOnlyList<string> FindLocalizationLayers(string language = "english")
    {
        var layers = new List<string>();
        for (var i = _localizeRoots.Count - 1; i >= 0; i--)
        {
            var root = _localizeRoots[i];
            if (FileIn(root, language + ".cfg") is { } main) layers.Add(main);
            if (FileIn(Path.Combine(root, "PC"), language + ".cfg") is { } pc) layers.Add(pc);
        }
        if (layers.Count == 0 && InFolders(f => f.Localizations, language) is { } found) layers.Add(found);
        return layers;
    }

    /// <summary>A file of this name in a folder, whatever its case (<c>PC\English.cfg</c>).</summary>
    private static string? FileIn(string folder, string name)
    {
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder, "*.cfg")
            .FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public string? FindTexture(string textureName) =>
        InRoots(_textureRoots, "*.tga", _textureIndex, textureName) ?? InFolders(f => f.Textures, textureName);

    public string? FindMesh(string meshName) =>
        InRoots(_meshRoots, "*.msh", _meshIndex, meshName) ?? InFolders(f => f.Meshes, meshName);

    /// <summary>A font by its <c>TextFont</c> name: the <c>.fff</c> file FontEdit saved.</summary>
    public string? FindFont(string fontName) =>
        InRoots(_fontRoots, "*.fff", _fontIndex, fontName) ?? InFolders(f => f.Fonts, fontName);

    public string? Find(HudAssetKind kind, string name) => kind switch
    {
        HudAssetKind.Texture => FindTexture(name),
        HudAssetKind.Mesh => FindMesh(name),
        HudAssetKind.Font => FindFont(name),
        HudAssetKind.Localization => FindLocalizationCfg(name),
        _ => null,
    };

    private static string? InRoots(List<string> roots, string pattern,
        Dictionary<string, Dictionary<string, string>> cache, string name)
    {
        foreach (var root in roots)
            if (GetOrBuildIndex(root, pattern, cache).TryGetValue(name, out var path)) return path;
        return null;
    }

    private string? InFolders(Func<AssetFolderIndex, IReadOnlyDictionary<string, string>> files, string name)
    {
        foreach (var folder in _folders)
            if (files(folder).TryGetValue(name, out var path)) return path;
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

    /// <summary>
    /// The data folder of the mod a HUD file belongs to, the folder holding its <c>Common</c>, when
    /// the file is in <c>Common\hud</c> or a folder inside it, as the mod tools lay a mod out; else
    /// null. A drive's root is never one.
    /// </summary>
    public static string? ModDataFolderOf(string hudFilePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(hudFilePath));
        for (var dir = folder is null ? null : new DirectoryInfo(folder); dir?.Parent is { } parent; dir = parent)
        {
            if (dir.Name.Equals("hud", StringComparison.OrdinalIgnoreCase) && parent.Name.Equals("Common", StringComparison.OrdinalIgnoreCase))
                return parent.Parent is { Parent: not null } data ? data.FullName : null;
        }
        return null;
    }

    public static AssetResolver ForHudFile(string hudFilePath, string? stockDataCommonPath = null) =>
        ForHudFiles(new[] { hudFilePath }, stockDataCommonPath);

    /// <summary>The assets of a HUD made of several files: each file's folders, in load order, then the stock ones.</summary>
    public static AssetResolver ForHudFiles(IEnumerable<string> hudFilePaths, string? stockDataCommonPath = null)
    {
        var resolver = new AssetResolver();
        foreach (var path in hudFilePaths) AddRootsForHudFile(resolver, path);
        if (stockDataCommonPath is not null) AddRootsForCommon(resolver, stockDataCommonPath);
        return resolver;
    }

    private static void AddRootsForHudFile(AssetResolver resolver, string hudFilePath)
    {
        // hud file lives at <root>/hud/<name>.hud. Textures can live in several places:
        //   hud/<subfolders>/  — mods often bundle their textures here (BF3-style HUDs nest
        //                        hud/soldier_bars/, hud/reticules/, hud/targeting/, etc.).
        //                        We add the hud folder itself as a texture root and the index
        //                        recursively walks all subfolders.
        //   interface/         — standard HUD textures (stock convention)
        //   effects/           — placeholder "white.tga" and similar solid-color tints used by
        //                        meshes like hud_weapon_backdrop.
        //   mshs/              — some meshes bundle their texture alongside the .msh
        // Meshes come from mshs/, fonts from fonts/, localization from Localize/.
        var hudDir = Path.GetDirectoryName(hudFilePath);
        if (hudDir is not null)
        {
            resolver.AddTextureRoot(hudDir);
            var common = Path.GetDirectoryName(hudDir);
            if (common is not null) AddRootsForCommon(resolver, common);
        }
    }

    private static void AddRootsForCommon(AssetResolver resolver, string common)
    {
        resolver.AddTextureRoot(Path.Combine(common, "interface"));
        resolver.AddTextureRoot(Path.Combine(common, "effects"));
        resolver.AddTextureRoot(Path.Combine(common, "mshs"));
        resolver.AddMeshRoot(Path.Combine(common, "mshs"));
        resolver.AddFontRoot(Path.Combine(common, "fonts"));
        resolver.AddLocalizeRoot(Path.Combine(common, "Localize"));
    }
}
