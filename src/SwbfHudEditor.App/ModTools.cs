using System.IO;

namespace SwbfHudEditor.App;

/// <summary>
/// Where the Battlefront II mod tools are: the stock HUD files, textures, models, fonts and strings come from
/// their <c>data\Common</c>. Asked for on the first start and kept in the settings; else, or when that folder is
/// gone, the first <c>BF2_ModTools</c> at the root of a drive.
/// </summary>
internal static class ModTools
{
    private static string? _folder;
    private static bool _known;

    /// <summary>The BF2_ModTools folder the stock assets are read from, or null when there is none.</summary>
    public static string? Folder
    {
        get
        {
            if (!_known) Folder = Choose(AppSettings.Load().ModToolsFolder);
            return _folder;
        }
        set
        {
            _folder = value;
            _known = true;
        }
    }

    /// <summary>The stock <c>data\Common</c>, when there are mod tools.</summary>
    public static string? DataCommon => Folder is { } folder ? Path.Combine(folder, "data", "Common") : null;

    /// <summary>The stock HUD files' folder, when there are mod tools.</summary>
    public static string? HudFolder => DataCommon is { } common ? Path.Combine(common, "hud") : null;

    /// <summary>
    /// The folder the setting names while it holds the mod tools; none when the user chose to go without (an empty
    /// setting); else, never asked or the folder gone, the one found at a drive's root.
    /// </summary>
    public static string? Choose(string? saved) => saved switch
    {
        "" => null,
        { } path when IsModTools(path) => path,
        _ => Detect(),
    };

    /// <summary>Whether a folder is the mod tools': it holds <c>data\Common</c>.</summary>
    public static bool IsModTools(string folder) => Directory.Exists(Path.Combine(folder, "data", "Common"));

    /// <summary>
    /// The mod tools a picked folder belongs to: the folder itself, the one it is in (a <c>data</c> or a mod's
    /// <c>data_XXX</c> picked by mistake), or a <c>BF2_ModTools</c> in it. Null when there are none there.
    /// </summary>
    public static string? Resolve(string picked)
    {
        if (string.IsNullOrWhiteSpace(picked)) return null;
        string full;
        try { full = Path.GetFullPath(picked.Trim().Trim('"')); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        for (var dir = new DirectoryInfo(full); dir is not null; dir = dir.Parent)
            if (IsModTools(dir.FullName)) return dir.FullName.TrimEnd(Path.DirectorySeparatorChar);
        var inside = Path.Combine(full, "BF2_ModTools");
        return IsModTools(inside) ? inside : null;
    }

    /// <summary>The first <c>BF2_ModTools</c> at the root of a fixed drive.</summary>
    public static string? Detect()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => Path.Combine(d.RootDirectory.FullName, "BF2_ModTools"))
                .FirstOrDefault(IsModTools);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
