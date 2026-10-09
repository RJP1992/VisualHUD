namespace SwbfHudEditor.Core.Assets;

/// <summary>
/// Every asset file in a folder and all its subfolders, by name without extension: textures
/// (<c>.tga</c>), models (<c>.msh</c>), fonts (<c>.fff</c>) and localization files (<c>.cfg</c> in a
/// <c>Localize</c> folder). Read in one pass, so a large folder such as a whole mod tools install is
/// walked once.
/// </summary>
public sealed class AssetFolderIndex
{
    private readonly Dictionary<string, string> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _meshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _localizations = new(StringComparer.OrdinalIgnoreCase);

    private AssetFolderIndex(string folder) => Folder = folder;

    public string Folder { get; }
    public IReadOnlyDictionary<string, string> Textures => _textures;
    public IReadOnlyDictionary<string, string> Meshes => _meshes;
    public IReadOnlyDictionary<string, string> Fonts => _fonts;
    /// <summary>Localization files by language, such as <c>english</c>.</summary>
    public IReadOnlyDictionary<string, string> Localizations => _localizations;

    /// <summary>How many files the folder holds, assets or not.</summary>
    public int FileCount { get; private set; }

    /// <summary>
    /// Walk the folder. A name found in more than one place resolves to the first path in ordinal
    /// order, so it resolves the same way every time. Folders that cannot be read, hidden and system
    /// files, and links are skipped.
    /// </summary>
    public static AssetFolderIndex Scan(string folder, CancellationToken cancel = default)
    {
        var index = new AssetFolderIndex(folder);
        if (!Directory.Exists(folder)) return index;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        var assets = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder, "*", options))
        {
            cancel.ThrowIfCancellationRequested();
            index.FileCount++;
            if (index.ListFor(path) is not null) assets.Add(path);
        }
        assets.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var path in assets)
            index.ListFor(path)!.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        return index;
    }

    private Dictionary<string, string>? ListFor(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)) return _textures;
        if (extension.Equals(".msh", StringComparison.OrdinalIgnoreCase)) return _meshes;
        if (extension.Equals(".fff", StringComparison.OrdinalIgnoreCase)) return _fonts;
        if (extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "Localize", StringComparison.OrdinalIgnoreCase))
            return _localizations;
        return null;
    }
}
