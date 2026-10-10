using System.IO;
using System.Windows;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App;

/// <summary>The open files on disk: a copy before each save, and a word when another program changes one.</summary>
public partial class MainWindow
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    // What each file held on disk when it was last read or written here, to tell another program's change.
    private readonly Dictionary<string, string> _diskText = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _changedOnDisk = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Copy the file as it is into _backups beside it, before it is written over.</summary>
    private void BackUp(string path)
    {
        try { HudBackups.Backup(path, DateTime.Now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not back up {Path.GetFileName(path)} before saving: {ex.Message}";
        }
    }

    /// <summary>Remember what a file holds on disk now: read or just written by the editor.</summary>
    private void NoteDiskText(string path)
    {
        try { _diskText[path] = File.ReadAllText(path); }
        catch (IOException) { }
    }

    /// <summary>Watch the open files for changes made by other programs.</summary>
    private void WatchFiles()
    {
        // Tests change files on disk themselves, and nobody would answer the question.
        if (Environment.GetEnvironmentVariable("VISUALHUD_NO_WATCH") == "1") return;
        var paths = UserFiles.Select(f => f.FilePath).OfType<string>().Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in _watchers.Keys.Where(p => !paths.Contains(p)).ToList())
        {
            _watchers[gone].Dispose();
            _watchers.Remove(gone);
            _diskText.Remove(gone);
        }
        foreach (var path in paths)
        {
            if (!_diskText.ContainsKey(path)) NoteDiskText(path);
            if (_watchers.ContainsKey(path) || Path.GetDirectoryName(path) is not { } folder || !Directory.Exists(folder)) continue;
            var watcher = new FileSystemWatcher(folder, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            watcher.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(() => FileChangedOnDisk(path)));
            watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(new Action(() => FileChangedOnDisk(path)));
            watcher.EnableRaisingEvents = true;
            _watchers[path] = watcher;
        }
    }

    /// <summary>
    /// Another program wrote an open file (a text editor, a script): offer to load it again. Asked once per
    /// change, and only once the writing has settled.
    /// </summary>
    private void FileChangedOnDisk(string path)
    {
        if (!_changedOnDisk.Add(path)) return;
        var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            _changedOnDisk.Remove(path);
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { return; }
            if (_diskText.TryGetValue(path, out var known) && known == text) return;   // the editor's own write
            _diskText[path] = text;
            var file = UserFiles.FirstOrDefault(f => f.FilePath is { } p && string.Equals(Path.GetFullPath(p), path, StringComparison.OrdinalIgnoreCase));
            if (file is null) return;
            var unsaved = file.IsModified ? "\n\nYour unsaved edits to it would be lost." : string.Empty;
            var answer = MessageBox.Show(this, $"{file.Name} was changed by another program.\n\nLoad it again?{unsaved}",
                "File changed on disk", MessageBoxButton.YesNo, file.IsModified ? MessageBoxImage.Warning : MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) ReloadFromDisk(file);
        };
        settle.Start();
    }

    /// <summary>Load one file of the HUD again from disk, in its place; the undo history is cleared.</summary>
    internal void ReloadFromDisk(HudFile file)
    {
        if (file.FilePath is not { } path) return;
        HudFile fresh;
        try { fresh = new HudFile(HudFileSet.Parse(path), path); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{path}\n\n{ex.Message}", "Failed to load the file again", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        var at = _files.IndexOf(file);
        if (at < 0) return;
        _files[at] = fresh;
        _undo.Clear();
        NoteDiskText(Path.GetFullPath(path));
        AfterSetChanged($"Loaded {fresh.Name} again from disk. The undo history was cleared.");
    }
}
