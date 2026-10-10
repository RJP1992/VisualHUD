namespace SwbfHudEditor.Core.Hud;

/// <summary>
/// A copy of a file as it was, made before saving over it: <c>_backups\name.hud.yyyyMMdd-HHmmss.bak</c> beside
/// the file, the newest <see cref="Keep"/> of each file kept. The <c>.bak</c> ending keeps the munger off them.
/// </summary>
public static class HudBackups
{
    public const string FolderName = "_backups";
    public const int Keep = 20;

    /// <summary>Copy the file as it is now; null when there is nothing to copy. Older copies past <see cref="Keep"/> go.</summary>
    public static string? Backup(string path, DateTime now)
    {
        if (!File.Exists(path)) return null;
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, FolderName);
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(path);
        var target = Path.Combine(folder, $"{name}.{now:yyyyMMdd-HHmmss}.bak");
        File.Copy(path, target, overwrite: true);
        // Only this file's own copies, by the name this writes: the newest first.
        var copies = Directory.GetFiles(folder, name + ".*.bak")
            .Where(f => IsCopyOf(Path.GetFileName(f), name))
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .Skip(Keep);
        foreach (var old in copies) File.Delete(old);
        return target;
    }

    /// <summary>name.yyyyMMdd-HHmmss.bak, and nothing else.</summary>
    private static bool IsCopyOf(string fileName, string name)
    {
        if (!fileName.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return false;
        var stamp = fileName[(name.Length + 1)..^4];
        return stamp.Length == 15 && stamp[8] == '-' && stamp.Remove(8, 1).All(char.IsDigit);
    }
}
