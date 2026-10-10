using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;
using SwbfHudEditor.Core.Localization;

namespace SwbfHudEditor.App;

public partial class MainWindow : Window
{
    internal const string DefaultHudFolder = @"C:\BF2_ModTools\data\Common\hud";
    internal const string StockDataCommon = @"C:\BF2_ModTools\data\Common";
    private const string HudFilter = "HUD files (*.hud)|*.hud|All files (*.*)|*.*";

    // Custom RoutedCommands so keybindings work regardless of which sub-control has focus.
    // The older PreviewKeyDown approach was getting swallowed by the TreeView's type-ahead
    // search — InputBinding/CommandBinding bypasses that entirely.
    public static readonly RoutedCommand ToggleHideSelectedCommand = new("ToggleHideSelected", typeof(MainWindow));
    public static readonly RoutedCommand UnhideAllCommand = new("UnhideAll", typeof(MainWindow));

    // The open HUD: its files in load order, the transform files they need first. The game loads a
    // HUD's files additively, so the tree has a node per file and Save writes each file with edits.
    private readonly ObservableCollection<HudFile> _files = new();
    // Transform files the user removed from the set, not to be loaded with it again.
    private readonly HashSet<string> _declinedAutomatic = new(HudFileSet.PathComparer);
    private bool _suppressSyncBack;
    private bool _closingForReload;
    private readonly UndoManager _undo = new();
    // The events sent in the Events tab: the game state the canvas draws while simulating.
    private readonly HudEventState _events = new();
    // The lines saving leaves out as engine defaults, for the Properties panel; found again after a change.
    private HashSet<HudBlock>? _defaultLines;
    // Asset folders, kept in the user's settings and each read in the background with all its
    // subfolders. A folder has an index once its scan has finished.
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Dictionary<string, AssetFolderIndex> _folderIndexes = new(HudFileSet.PathComparer);
    private readonly Dictionary<string, string> _folderErrors = new(HudFileSet.PathComparer);
    private readonly Dictionary<string, int> _scanGeneration = new(HudFileSet.PathComparer);
    private readonly HashSet<string> _scanning = new(HudFileSet.PathComparer);
    private readonly Dictionary<string, IReadOnlyList<string>> _meshTextures = new(StringComparer.OrdinalIgnoreCase);
    private AssetResolver? _assets;
    private LocalizationTable? _localization;
    private bool _assetListStale = true;

    public MainWindow()
    {
        InitializeComponent();
        Tree.ItemsSource = _files;
        Canvas.DocumentVisible = doc => FileOf(doc)?.IsVisible ?? true;
        Properties.IsDefaultLine = line => (_defaultLines ??= FindDefaultLines()).Contains(line);
        Properties.ParentOf = block => Locate(block) is { Path: { Count: > 1 } path } ? path[^2] : null;
        _undo.Changed += (_, _) =>
        {
            _defaultLines = null;
            UpdateTitle();
            RefreshAssetList();
        };
        EventsList.Bind(_events);
        EventsList.SimulateChanged += (_, on) => Canvas.SetEventState(on ? _events : null);
        EventsList.ShowSelectedRequested += (_, _) => ShowSelectedInSimulation();
        EventsList.OnFootRequested += (_, _) =>
        {
            if (Canvas.Transforms is not { } graph) return;
            _events.SendAll(HudEventPresets.OnFoot(graph));
            EventsList.StartSimulating();
            StatusText.Text = "Sent the on-foot events.";
        };
        Canvas.SimulationUpdated += (_, simulation) => EventsList.ShowSimulation(simulation);
        AssetsList.AddFolderRequested += (_, _) => AddAssetFolders();
        AssetsList.RemoveFolderRequested += (_, folder) => RemoveAssetFolder(folder);
        AssetsList.RescanRequested += (_, _) =>
        {
            _meshTextures.Clear();
            foreach (var folder in SearchedAssetFolders()) ScanAssetFolder(folder);
        };
        ShowAssetFolders();
        foreach (var folder in SearchedAssetFolders()) ScanAssetFolder(folder, report: false);

        if (_settings.ReferenceImage is { } reference && File.Exists(reference)) LoadReference(reference, quiet: true);
        Canvas.ReferenceSettingsChanged += (_, _) =>
        {
            _settings.ReferenceOpacity = Canvas.ReferenceOpacity;
            _settings.ReferenceOnTop = Canvas.ReferenceOnTop;
            _settings.ReferenceShown = Canvas.ReferenceShown;
            _settings.Save();
        };
    }

    // ── Reference image ──────────────────────────────────────────────────────────────

    private void LoadReference_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load a reference image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.tga)|*.png;*.jpg;*.jpeg;*.bmp;*.tga|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_settings.ReferenceImage) is { } last && Directory.Exists(last) ? last : StartFolder(),
        };
        if (dialog.ShowDialog(this) == true) LoadReference(dialog.FileName, quiet: false);
    }

    private void ClearReference_Click(object sender, RoutedEventArgs e)
    {
        Canvas.ReferenceImage = null;
        _settings.ReferenceImage = null;
        _settings.Save();
        StatusText.Text = "Reference image cleared.";
    }

    /// <summary>Read an image file and lay it under the HUD; the file is not kept open.</summary>
    private void LoadReference(string path, bool quiet)
    {
        BitmapSource image;
        try { image = ReadImage(path); }
        catch (Exception ex)
        {
            if (!quiet) MessageBox.Show(this, $"{path}\n\n{ex.Message}", "Could not load the image", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Canvas.ReferenceImage = image;
        Canvas.ReferenceOpacity = _settings.ReferenceOpacity;
        Canvas.ReferenceOnTop = _settings.ReferenceOnTop;
        Canvas.ReferenceShown = quiet ? _settings.ReferenceShown : true;
        _settings.ReferenceImage = path;
        _settings.ReferenceShown = Canvas.ReferenceShown;
        _settings.Save();
        if (!quiet) StatusText.Text = $"Reference image: {path} ({image.PixelWidth}×{image.PixelHeight}), stretched across the screen.";
    }

    /// <summary>A PNG, JPEG or BMP through WPF, a TGA through the texture loader.</summary>
    internal static BitmapSource ReadImage(string path)
    {
        if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            var tga = TgaLoader.Load(path);
            var source = BitmapSource.Create(tga.Width, tga.Height, 96, 96, PixelFormats.Bgra32, null, tga.Bgra32Data, tga.Width * 4);
            source.Freeze();
            return source;
        }
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private IReadOnlyList<HudDocument> Documents => _files.Select(f => f.Document).ToList();
    private IEnumerable<HudFile> UserFiles => _files.Where(f => !f.AddedAutomatically);

    private void Open_Click(object sender, RoutedEventArgs e) => OpenFiles();
    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) => OpenFiles();
    private void AddFiles_Click(object sender, RoutedEventArgs e) => AddFiles();
    private void RemoveFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFile is { } file) RemoveFile(file);
        else StatusText.Text = "Select a file, or an element in it, to remove it from the HUD.";
    }
    private void AddAssetFolder_Click(object sender, RoutedEventArgs e) => AddAssetFolders();
    private void Reload_Click(object sender, RoutedEventArgs e) => ReloadCurrent();
    private void Reload_Executed(object sender, ExecutedRoutedEventArgs e) => ReloadCurrent();
    private void Reload_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = UserFiles.Any(f => f.FilePath is not null);

    /// <summary>
    /// Restart the entire editor process, re-opening the HUD's files. The current window is closed
    /// after the new instance has been started so there's no dead window on screen. Use this when a
    /// code change or asset refresh needs a clean slate instead of a soft in-place reload.
    /// </summary>
    private void ReloadCurrent()
    {
        var paths = UserFiles.Select(f => f.FilePath).OfType<string>().ToList();
        if (paths.Count == 0) return;
        var missing = paths.Where(p => !File.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(this, $"File no longer exists:\n{string.Join("\n", missing)}", "Reload failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!ConfirmDiscard("reloading")) return;

        // Our own exe + the files as cli arguments. Window_Loaded opens every file it is given, in order.
        var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrEmpty(exePath))
        {
            MessageBox.Show(this, "Could not determine the editor's exe path — can't restart.",
                "Reload failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = string.Join(" ", paths.Select(p => $"\"{p}\"")),
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not spawn new instance:\n{ex.Message}",
                "Reload failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _closingForReload = true;
        Application.Current.Shutdown();
    }
    private void Save_Click(object sender, RoutedEventArgs e) => SaveAll();
    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => SaveAll();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();
    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => SaveAs();
    private void Save_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _files.Count > 0;
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var paths = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToList();
        if (paths.Count > 0) LoadFiles(paths);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_closingForReload && !ConfirmDiscard("closing")) e.Cancel = true;
    }

    // ── Files ────────────────────────────────────────────────────────────────────────

    private void OpenFiles()
    {
        var dialog = new OpenFileDialog
        {
            Filter = HudFilter,
            Multiselect = true,
            Title = "Open the files of a HUD",
            InitialDirectory = StartFolder(),
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!ConfirmDiscard("opening other files")) return;
        LoadFiles(dialog.FileNames.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase));
    }

    private string? StartFolder() =>
        UserFiles.Select(f => Path.GetDirectoryName(f.FilePath)).FirstOrDefault(Directory.Exists)
        ?? (Directory.Exists(DefaultHudFolder) ? DefaultHudFolder : null);

    /// <summary>Open these files as the HUD, in this load order, with the transform files they need.</summary>
    private void LoadFiles(IEnumerable<string> paths)
    {
        var loaded = new List<HudFile>();
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(HudFileSet.PathComparer))
        {
            try { loaded.Add(new HudFile(HudFileSet.Parse(path), path)); }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{path}\n\n{ex.Message}", "Failed to open file", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        if (loaded.Count == 0) return;
        _files.Clear();
        _declinedAutomatic.Clear();
        foreach (var file in loaded) _files.Add(file);
        SyncAutomaticFiles();
        _undo.Clear();
        AfterSetChanged();
    }

    /// <summary>Add files to the open HUD, loaded after its files, as the game adds a mod's HUD files.</summary>
    private void AddFiles()
    {
        if (_files.Count == 0)
        {
            OpenFiles();
            return;
        }
        var dialog = new OpenFileDialog
        {
            Filter = HudFilter,
            Multiselect = true,
            Title = "Add files to the HUD",
            InitialDirectory = StartFolder(),
        };
        if (dialog.ShowDialog(this) != true) return;
        var added = new List<string>();
        foreach (var path in dialog.FileNames.Select(Path.GetFullPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (FileAt(path) is { } open)
            {
                // A transform file the HUD already loads becomes one the user opened.
                if (open.AddedAutomatically)
                {
                    open.AddedAutomatically = false;
                    added.Add(open.Name);
                }
                continue;
            }
            try
            {
                _files.Add(new HudFile(HudFileSet.Parse(path), path));
                added.Add(Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{path}\n\n{ex.Message}", "Failed to open file", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        if (added.Count == 0) return;
        SyncAutomaticFiles();
        AfterSetChanged($"Added {string.Join(", ", added)}");
    }

    /// <summary>Take a file out of the HUD. The file on disk is not changed.</summary>
    private void RemoveFile(HudFile file)
    {
        file.Refresh();
        if (file.IsModified && MessageBox.Show(this,
                $"{file.Name} has unsaved changes. Remove it from the HUD anyway? The file on disk is not changed.",
                "Remove file", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _files.Remove(file);
        if (file.AddedAutomatically && file.FilePath is { } path) _declinedAutomatic.Add(Path.GetFullPath(path));
        SyncAutomaticFiles();
        AfterSetChanged($"Removed {file.Name} from the HUD; the file on disk is unchanged.");
    }

    /// <summary>
    /// Bring the transform files loaded with the HUD in line with its files: the ones now needed join
    /// ahead of the files, and unedited ones no longer needed leave.
    /// </summary>
    private void SyncAutomaticFiles()
    {
        var own = UserFiles.Select(f => f.FilePath).OfType<string>().ToList();
        var wanted = HudFileSet.TransformFilesFor(own).Where(p => !_declinedAutomatic.Contains(p)).ToList();
        for (var i = _files.Count - 1; i >= 0; i--)
        {
            var file = _files[i];
            if (!file.AddedAutomatically) continue;
            file.Refresh();
            if (!file.IsModified && !wanted.Contains(Path.GetFullPath(file.FilePath!), HudFileSet.PathComparer)) _files.RemoveAt(i);
        }
        var at = 0;
        foreach (var path in wanted)
        {
            var index = IndexOfPath(path);
            if (index >= 0)
            {
                at = Math.Max(at, index + 1);
                continue;
            }
            if (HudFileSet.TryParse(path) is { } doc) _files.Insert(at++, new HudFile(doc, path, addedAutomatically: true));
        }
    }

    private int IndexOfPath(string path)
    {
        for (var i = 0; i < _files.Count; i++)
            if (_files[i].FilePath is { } p && HudFileSet.PathComparer.Equals(Path.GetFullPath(p), path)) return i;
        return -1;
    }

    private HudFile? FileAt(string path) => IndexOfPath(path) is var i and >= 0 ? _files[i] : null;

    /// <summary>After the files of the HUD change: assets, the canvas and the Events tab over all of them.</summary>
    private void AfterSetChanged(string? status = null)
    {
        _defaultLines = null;
        // The data folders of the open files' mods join the search, read the first time they are needed.
        foreach (var folder in AutomaticAssetFolders()) EnsureScanned(folder);
        ShowAssetFolders();
        var localization = LoadAssets();
        RefreshEventList();
        Properties.SetBlock(null);
        UpdateTitle();
        if (status is not null)
        {
            StatusText.Text = status;
            return;
        }
        var user = UserFiles.ToList();
        var what = user.Count == 1 ? user[0].FilePath : $"{user.Count} files";
        var locStatus = localization is null ? "no localize" : $"{localization.Count} strings";
        var transformCount = Canvas.Transforms?.Nodes.Count ?? 0;
        StatusText.Text = $"{what}    {CountBlocks(Documents.SelectMany(d => d.TopLevel))} blocks    {locStatus}    " +
                          $"{transformCount} transforms from {_files.Count} file{(_files.Count == 1 ? "" : "s")}";
    }

    // ── Assets ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Find the HUD's assets again: in its folders, the stock ones, then the asset folders read so
    /// far, and draw with them.
    /// </summary>
    private LocalizationTable? LoadAssets()
    {
        _assets = ResolverFor(UserFiles.Select(f => f.FilePath).OfType<string>(), ReadAssetFolders());
        _localization = TryLoadLocalization(_assets);
        Canvas.LoadDocuments(Documents, _assets, _localization);
        RefreshAssetList();
        return _localization;
    }

    /// <summary>The asset folders read so far, in search order.</summary>
    private IEnumerable<AssetFolderIndex> ReadAssetFolders() =>
        SearchedAssetFolders().Where(_folderIndexes.ContainsKey).Select(f => _folderIndexes[f]);

    /// <summary>Every asset folder in search order: the automatic ones, then those added.</summary>
    private IReadOnlyList<string> SearchedAssetFolders() =>
        AutomaticAssetFolders().Concat(_settings.AssetFolders).Distinct(HudFileSet.PathComparer).ToList();

    private IReadOnlyList<string> AutomaticAssetFolders() =>
        AutomaticAssetFoldersFor(UserFiles.Select(f => f.FilePath).OfType<string>());

    /// <summary>
    /// The folders searched, with all their subfolders, without being added: the data folder of the
    /// mod each file belongs to (<c>data_BF3</c> for <c>data_BF3\Common\hud\x.hud</c>), then the stock
    /// data folder.
    /// </summary>
    internal static IReadOnlyList<string> AutomaticAssetFoldersFor(IEnumerable<string> hudPaths)
    {
        var folders = hudPaths.Select(AssetResolver.ModDataFolderOf).OfType<string>().ToList();
        if (Path.GetDirectoryName(StockDataCommon) is { } stock && Directory.Exists(stock)) folders.Add(stock);
        return folders.Distinct(HudFileSet.PathComparer).ToList();
    }

    /// <summary>The resolver for a HUD's files: their folders, the stock ones, then these asset folders.</summary>
    internal static AssetResolver ResolverFor(IEnumerable<string> hudPaths, IEnumerable<AssetFolderIndex> folders)
    {
        var resolver = AssetResolver.ForHudFiles(hudPaths, Directory.Exists(StockDataCommon) ? StockDataCommon : null);
        foreach (var folder in folders) resolver.AddFolder(folder);
        return resolver;
    }

    /// <summary>Check the HUD's assets again in the Assets tab, or when it is next opened.</summary>
    private void RefreshAssetList()
    {
        _assetListStale = true;
        if (!AssetsTab.IsSelected) return;
        _assetListStale = false;
        var strings = _localization is { } table ? key => table.TryGet(key) is not null : (Func<string, bool>?)null;
        AssetsList.ShowAssets(_files.Count == 0 || _assets is null ? null : HudAssets.Check(Documents, _assets, _meshTextures, strings));
    }

    private void LeftTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The lists and boxes inside the tabs raise this too.
        if (!ReferenceEquals(e.OriginalSource, LeftTabs)) return;
        if (AssetsTab.IsSelected && _assetListStale) RefreshAssetList();
    }

    private void AddAssetFolders()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Add asset folders: searched with all their subfolders for missing textures, meshes, fonts and localization",
            Multiselect = true,
            InitialDirectory = _settings.AssetFolders.LastOrDefault(Directory.Exists) ?? StartFolder(),
        };
        if (dialog.ShowDialog(this) != true) return;
        LeftTabs.SelectedItem = AssetsTab;
        var added = new List<string>();
        foreach (var folder in dialog.FolderNames.Select(Path.GetFullPath))
        {
            if (_settings.AssetFolders.Contains(folder, HudFileSet.PathComparer)) continue;
            _settings.AssetFolders.Add(folder);
            added.Add(folder);
        }
        if (added.Count == 0)
        {
            StatusText.Text = "Those folders are already asset folders.";
            return;
        }
        if (_settings.Save() is { } error) StatusText.Text = $"Could not save the settings: {error}";
        foreach (var folder in added) ScanAssetFolder(folder);
    }

    private void RemoveAssetFolder(string folder)
    {
        _settings.AssetFolders.RemoveAll(f => HudFileSet.PathComparer.Equals(f, folder));
        var error = _settings.Save();
        ShowAssetFolders();
        if (_files.Count > 0) LoadAssets();
        StatusText.Text = error is null ? $"No longer searching {folder}." : $"Could not save the settings: {error}";
    }

    /// <summary>Read a folder unless it has been read, or is being read, already.</summary>
    private void EnsureScanned(string folder)
    {
        if (_folderIndexes.ContainsKey(folder) || _folderErrors.ContainsKey(folder) || _scanning.Contains(folder)) return;
        ScanAssetFolder(folder, report: false);
    }

    /// <summary>
    /// Read an asset folder with all its subfolders away from the window, then draw with what it holds.
    /// What a folder holds is kept for the session, so a mod opened again is not read again.
    /// </summary>
    private async void ScanAssetFolder(string folder, bool report = true)
    {
        var generation = _scanGeneration.GetValueOrDefault(folder) + 1;
        _scanGeneration[folder] = generation;
        _scanning.Add(folder);
        _folderIndexes.Remove(folder);
        _folderErrors.Remove(folder);
        ShowAssetFolders();
        if (report) StatusText.Text = $"Scanning {folder}...";

        AssetFolderIndex? index = null;
        string? error = null;
        try { index = await Task.Run(() => AssetFolderIndex.Scan(folder)); }
        catch (Exception ex) { error = ex.Message; }

        // Scanned again in the meantime.
        if (_scanGeneration[folder] != generation) return;
        _scanning.Remove(folder);
        if (index is null) _folderErrors[folder] = error ?? "unknown error";
        else _folderIndexes[folder] = index;
        ShowAssetFolders();
        if (_files.Count > 0 && SearchedAssetFolders().Contains(folder, HudFileSet.PathComparer)) LoadAssets();
        if (report) StatusText.Text = index is null ? $"Could not read {folder}: {error}" : $"Scanned {folder}: {Describe(index)}.";
    }

    /// <summary>The Assets tab's folder list: the automatic folders, then those added, which can be removed.</summary>
    private void ShowAssetFolders()
    {
        AssetFolderRow Row(string folder, bool automatic)
        {
            var status = _folderErrors.TryGetValue(folder, out var error) ? $"could not be read: {error}"
                : _folderIndexes.TryGetValue(folder, out var index) ? (Directory.Exists(folder) ? Describe(index) : "not found")
                : "scanning...";
            return new AssetFolderRow(folder, automatic ? $"searched automatically · {status}" : status, automatic);
        }
        AssetsList.ShowFolders(AutomaticAssetFolders()
            .Where(f => !_settings.AssetFolders.Contains(f, HudFileSet.PathComparer)).Select(f => Row(f, automatic: true))
            .Concat(_settings.AssetFolders.Select(f => Row(f, automatic: false)))
            .ToList());
    }

    private static string Describe(AssetFolderIndex index) =>
        $"{index.Textures.Count:N0} textures, {index.Meshes.Count:N0} meshes, {index.Fonts.Count:N0} fonts in {index.FileCount:N0} files";

    /// <summary>Ask about unsaved edits before they would be lost; false to stay.</summary>
    private bool ConfirmDiscard(string doing)
    {
        foreach (var file in _files) file.Refresh();
        var modified = _files.Where(f => f.IsModified).Select(f => f.Name).ToList();
        if (modified.Count == 0) return true;
        var answer = MessageBox.Show(this, $"Save the changes to {string.Join(", ", modified)} before {doing}?",
            "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return answer switch
        {
            MessageBoxResult.Yes => SaveAll(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    /// <summary>Write every file with unsaved edits. False if one was not saved.</summary>
    private bool SaveAll()
    {
        var saved = new List<string>();
        foreach (var file in _files.ToList())
        {
            file.Refresh();
            if (!file.IsModified) continue;
            var written = file.FilePath is { } path ? WriteFile(file, path) : SaveFileAs(file);
            if (!written) return false;
            saved.Add(file.Name);
        }
        UpdateTitle();
        StatusText.Text = saved.Count == 0 ? "No unsaved changes." : $"Saved {string.Join(", ", saved)}";
        return true;
    }

    /// <summary>Save the selection's file under a new name.</summary>
    private void SaveAs()
    {
        if (ActiveFile is { } file && SaveFileAs(file)) StatusText.Text = $"Saved to {file.FilePath}";
    }

    private bool SaveFileAs(HudFile file)
    {
        var dialog = new SaveFileDialog
        {
            Filter = HudFilter,
            FileName = file.FilePath is null ? "untitled.hud" : file.Name,
            InitialDirectory = Path.GetDirectoryName(file.FilePath),
        };
        if (dialog.ShowDialog(this) != true) return false;
        if (!WriteFile(file, dialog.FileName)) return false;
        file.FilePath = dialog.FileName;
        file.AddedAutomatically = false;
        UpdateTitle();
        return true;
    }

    private bool WriteFile(HudFile file, string path)
    {
        try
        {
            // Lines that only restate the engine's defaults are left out of the file.
            File.WriteAllText(path, ElementDefaults.WriteWithoutDefaults(file.Document));
            file.MarkSaved();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{path}\n\n{ex.Message}", "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private HashSet<HudBlock> FindDefaultLines()
    {
        var lines = new HashSet<HudBlock>(ReferenceEqualityComparer.Instance);
        foreach (var file in _files) lines.UnionWith(ElementDefaults.RedundantLines(file.Document));
        return lines;
    }

    private void UpdateTitle()
    {
        foreach (var file in _files) file.Refresh();
        var user = UserFiles.ToList();
        var name = user.Count == 0 ? "(no file)" : user[0].Name + (user.Count > 1 ? $" (+{user.Count - 1})" : string.Empty);
        Title = $"VisualHUD — {name}{(_files.Any(f => f.IsModified) ? " *" : "")}";
    }

    /// <summary>The file holding a block, with the blocks from the top of that file down to it.</summary>
    private (HudFile File, IReadOnlyList<HudBlock> Path)? Locate(HudBlock block)
    {
        foreach (var file in _files)
            if (file.Document.FindPathTo(block) is { } path) return (file, path);
        return null;
    }

    private HudFile? FileOf(HudBlock block) => Locate(block)?.File;
    private HudFile? FileOf(HudDocument doc) => _files.FirstOrDefault(f => ReferenceEquals(f.Document, doc));

    private HudFile? SelectedFile => Tree.SelectedItem switch
    {
        HudFile file => file,
        HudBlock block => FileOf(block),
        _ => null,
    };

    /// <summary>The file an action without a block works on: the selection's, else the first the user opened.</summary>
    private HudFile? ActiveFile => SelectedFile ?? UserFiles.FirstOrDefault() ?? _files.FirstOrDefault();

    private void Properties_PropertyEdited(object? sender, PropertyEditedEventArgs e)
    {
        var edit = new ReplaceArgumentEdit(e.Leaf, e.ArgIndex, e.OldArg, e.NewArg, affected: e.SelectedBlock);
        _undo.Push(edit);
        Canvas.Refresh();
        RefreshEventList();
        Properties.RefreshDefaultMarks();
    }

    /// <summary>The Properties panel's delete button: remove that line, as an undoable edit.</summary>
    private void Properties_PropertyDeleteRequested(object? sender, PropertyDeleteRequestedEventArgs e)
    {
        var index = e.Target.Children.IndexOf(e.Property);
        if (index < 0) return;
        e.Target.Children.RemoveAt(index);
        _undo.Push(new DeleteBlockEdit(e.Target.Children, index, e.Property, affected: e.Target));
        Canvas.Refresh();
        Properties.SetBlock(e.Target);
        RefreshEventList();
        StatusText.Text = $"Removed {e.Property.Name} from {e.Target.Name} \"{FirstName(e.Target)}\"";
    }

    // ── Events ───────────────────────────────────────────────────────────────────────

    /// <summary>List the events the HUD's files and their transforms use in the Events tab.</summary>
    private void RefreshEventList()
    {
        if (_files.Count == 0 || Canvas.Transforms is not { } graph) return;
        var meshes = graph.Nodes.OfType<NameMeshNode>().SelectMany(n => n.Rows.Select(r => r.Mesh))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
        IReadOnlyList<string> Suggest(HudEventUse use)
        {
            // A weapon change or other name hash: the names its TransformNameMesh tables know.
            var names = graph.Nodes.OfType<NameMeshNode>()
                .Where(n => string.Equals(n.Input, use.Name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(n => n.Rows.Select(r => r.Source)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return use.Type switch
            {
                HudEventType.Uint when names.Count > 0 => names,
                HudEventType.Model => meshes,
                HudEventType.Bool => new[] { "true", "false" },
                HudEventType.Float => new[] { "0", "0.25", "0.5", "0.75", "1" },
                HudEventType.Color => new[] { "255, 255, 255", "255, 0, 0", "0, 255, 0", "0, 0, 255", "255, 200, 0" },
                HudEventType.Vector3 => new[] { "0.5, 0.5, 0", "0, 0, 0" },
                _ => Array.Empty<string>(),
            };
        }
        EventsList.SetEvents(HudEventUsage.Of(Documents, graph), Suggest);
    }

    /// <summary>
    /// Send what shows the selected element: the EventEnable event of it and of every group above it,
    /// top first, with the value typed for each in the Events tab.
    /// </summary>
    private void ShowSelectedInSimulation()
    {
        if (Tree.SelectedItem is not HudBlock block || Locate(block) is not { } at) return;
        var sends = new List<(string, HudEventValue)>();
        foreach (var b in at.Path)
        {
            if (!ElementKinds.TryGet(b, out var kind)) continue;
            if (ElementReader.Read(b, kind).EventEnable is not { } name) continue;
            var type = HudEventCatalog.Find(name)?.Type ?? HudEventType.Bool;
            sends.Add((name, EventsList.TypedValue(name) ?? HudEventValue.Default(type)));
        }
        if (sends.Count == 0)
        {
            StatusText.Text = $"{block.Name} \"{FirstName(block)}\" has no EventEnable: the game never shows it.";
            return;
        }
        _events.SendAll(sends);
        StatusText.Text = $"Sent {string.Join(", ", sends.Select(s => s.Item1))}" +
                          (EventsList.Simulate ? string.Empty : " (turn on simulation in the Events tab to see it)");
    }

    /// <summary>
    /// Handle a click on one of the "Add" tab buttons. Inserts the pre-built property leaf as
    /// a child of the selected block and wraps it in an undo-able edit. We insert BEFORE any
    /// nested (HasBody=true) children so property rows stay grouped at the top — that matches
    /// how stock .hud files are laid out, where leaf properties precede any sub-elements.
    /// </summary>
    private void Properties_PropertyInsertRequested(object? sender, PropertyInsertRequestedEventArgs e)
    {
        var target = e.Target;
        // Index = last leaf child + 1, so Rotation/Scale/etc land next to Position instead of
        // getting dumped at the very end after every nested Group. If there are no children yet,
        // insert at 0. A block, such as a MeshInfo, goes at the end.
        var insertAt = 0;
        if (e.Property.HasBody) insertAt = target.Children.Count;
        else
            for (var i = 0; i < target.Children.Count; i++)
            {
                if (!target.Children[i].HasBody) insertAt = i + 1;
                else break;
            }
        target.Children.Insert(insertAt, e.Property);
        var edit = new InsertBlockEdit(target.Children, insertAt, e.Property,
            $"add {e.Property.Name} to {target.Name}", affected: target);
        _undo.Push(edit);

        // Refresh panel so the new property shows up in the Edit tab and vanishes from Add,
        // refresh canvas in case the property affects layout (Position/Rotation/Scale do),
        // and keep the host block selected — the user usually wants to tweak the new value next.
        Canvas.Refresh();
        Properties.SetBlock(target);
        StatusText.Text = $"Added {e.Property.Name} to {target.Name} \"{FirstName(target)}\"";
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var block = e.NewValue as HudBlock;
        Properties.SetBlock(block);
        // The guides and the layout line follow the selection's file.
        if (SelectedFile is { } file)
        {
            Canvas.ActiveDocument = file.Document;
            if (e.NewValue is HudFile)
                StatusText.Text = $"{file.FilePath ?? file.Name}    {Canvas.DescribeFile(file.Document)}";
        }
        if (_suppressSyncBack) return;
        Canvas.HighlightBlock(block);
    }

    private void VisibilityCheckBox_Click(object sender, RoutedEventArgs e)
    {
        Canvas.Refresh();
    }

    /// <summary>
    /// On any left-click inside the tree, walk up from the hit element to find the containing
    /// TreeViewItem and select it. Default WPF behaviour lets the embedded CheckBox consume
    /// the click, so selecting-by-click-on-checkbox doesn't happen — the tree appears to
    /// highlight a row without updating Tree.SelectedItem.
    /// </summary>
    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject hit) return;
        var node = hit;
        while (node is not null and not TreeViewItem)
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        if (node is TreeViewItem tvi)
        {
            tvi.IsSelected = true;
            tvi.Focus();
        }
    }

    private void Canvas_HoverInfoChanged(object? sender, string? info)
    {
        HoverText.Text = info ?? string.Empty;
    }

    private void Canvas_BlockDragged(object? sender, HudBlock block)
    {
        // Refresh property panel so position values update live during drag.
        Properties.SetBlock(block);
    }

    private void Canvas_BlockDragCommitted(object? sender, IHudEdit edit)
    {
        _undo.Push(edit);
    }

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var affected = _undo.Undo();
        AfterUndoRedo(affected, "Undid");
    }

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var affected = _undo.Redo();
        AfterUndoRedo(affected, "Redid");
    }

    private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _undo.CanUndo;
    private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _undo.CanRedo;

    private void AfterUndoRedo(HudBlock? affected, string verb)
    {
        Canvas.Refresh();
        RefreshEventList();
        if (affected is not null)
        {
            Properties.SetBlock(affected);
            Canvas.HighlightBlock(affected);
        }
        var desc = verb == "Undid" ? _undo.PeekRedoDescription() : _undo.PeekUndoDescription();
        StatusText.Text = desc is null ? $"{verb}." : $"{verb}: {desc}";
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase) return;
        // H / Alt+H are handled via InputBinding + CommandBinding; see ToggleHideSelected_Executed /
        // UnhideAll_Executed. Only Delete is left here because it's unbound (Delete works naturally
        // inside most focused controls so we keep the skip-textbox guard above).
        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    /// <summary>
    /// H and Alt+H are bound for the whole window. While a text box has focus they stand aside and
    /// let the key go on, so the box gets its "h": a key bound to a command that cannot run is still
    /// taken unless the routing continues.
    /// </summary>
    private void HideCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = Keyboard.FocusedElement is not TextBoxBase;
        e.ContinueRouting = !e.CanExecute;
    }

    /// <summary>H keybinding — toggles the selected block's, or file's, visibility on the canvas.</summary>
    private void ToggleHideSelected_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        switch (Tree.SelectedItem)
        {
            case HudBlock block:
                block.IsVisible = !block.IsVisible;
                StatusText.Text = $"{(block.IsVisible ? "Shown" : "Hidden")}: {block.Name} \"{FirstName(block)}\"";
                break;
            case HudFile file:
                file.IsVisible = !file.IsVisible;
                StatusText.Text = $"{(file.IsVisible ? "Shown" : "Hidden")}: {file.Name}";
                break;
            default:
                return;
        }
        Canvas.Refresh();
        e.Handled = true;
    }

    /// <summary>Alt+H keybinding — unhides every hidden block and file.</summary>
    private void UnhideAll_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var count = 0;
        foreach (var file in _files)
        {
            if (!file.IsVisible) { file.IsVisible = true; count++; }
            count += UnhideAll(file.Document.TopLevel);
        }
        Canvas.Refresh();
        StatusText.Text = count > 0 ? $"Unhid {count} element{(count == 1 ? "" : "s")}." : "Nothing was hidden.";
        e.Handled = true;
    }

    private static int UnhideAll(IEnumerable<HudBlock> blocks)
    {
        var changed = 0;
        foreach (var b in blocks)
        {
            if (!b.IsVisible) { b.IsVisible = true; changed++; }
            changed += UnhideAll(b.Children);
        }
        return changed;
    }

    // ── Structural edits ───────────────────────────────────────────────────────────

    private void DeleteSelected_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    /// <summary>Delete the selected block, or take the selected file out of the HUD.</summary>
    private void DeleteSelected()
    {
        if (Tree.SelectedItem is HudFile file)
        {
            RemoveFile(file);
            return;
        }
        if (Tree.SelectedItem is not HudBlock block || FileOf(block) is not { } owner) return;
        var container = owner.Document.FindContainer(block);
        if (container is null) return;
        var (list, index, parent) = container.Value;

        list.RemoveAt(index);
        var edit = new DeleteBlockEdit(list, index, block, affected: parent);
        _undo.Push(edit);

        Canvas.Refresh();
        Properties.SetBlock(parent);
        RefreshEventList();
        StatusText.Text = $"Deleted {block.Name} \"{FirstName(block)}\"";
    }

    private void InsertChild_Group_Click(object sender, RoutedEventArgs e) => InsertChildOfSelected(HudBlockTemplates.NewGroup(UniqueName("new_group")));
    private void InsertChild_Bitmap_Click(object sender, RoutedEventArgs e) => InsertChildOfSelected(HudBlockTemplates.NewBitmap(UniqueName("new_bitmap")));
    private void InsertChild_BarBitmap_Click(object sender, RoutedEventArgs e) => InsertChildOfSelected(HudBlockTemplates.NewBarBitmap(UniqueName("new_barbitmap")));
    private void InsertChild_Text_Click(object sender, RoutedEventArgs e) => InsertChildOfSelected(HudBlockTemplates.NewText(UniqueName("new_text")));
    private void InsertChild_Model3D_Click(object sender, RoutedEventArgs e) => InsertChildOfSelected(HudBlockTemplates.NewModel3D(UniqueName("new_model3d")));

    private void InsertTopLevel_Group_Click(object sender, RoutedEventArgs e) => InsertTopLevel(HudBlockTemplates.NewGroup(UniqueName("new_group")));
    private void InsertTopLevel_Bitmap_Click(object sender, RoutedEventArgs e) => InsertTopLevel(HudBlockTemplates.NewBitmap(UniqueName("new_bitmap")));
    private void InsertTopLevel_BarBitmap_Click(object sender, RoutedEventArgs e) => InsertTopLevel(HudBlockTemplates.NewBarBitmap(UniqueName("new_barbitmap")));
    private void InsertTopLevel_Text_Click(object sender, RoutedEventArgs e) => InsertTopLevel(HudBlockTemplates.NewText(UniqueName("new_text")));
    private void InsertTopLevel_Model3D_Click(object sender, RoutedEventArgs e) => InsertTopLevel(HudBlockTemplates.NewModel3D(UniqueName("new_model3d")));

    /// <summary>Insert into the selected block, or at the top of the selected file.</summary>
    private void InsertChildOfSelected(HudBlock newBlock)
    {
        if (Tree.SelectedItem is not HudBlock parent)
        {
            InsertTopLevel(newBlock);
            return;
        }
        if (!parent.HasBody)
        {
            MessageBox.Show(this, $"'{parent.Name}' is a leaf node and can't hold children. Selecting a Group or other container first.",
                "Can't insert child", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var index = parent.Children.Count;
        parent.Children.Insert(index, newBlock);
        var edit = new InsertBlockEdit(parent.Children, index, newBlock, $"insert {newBlock.Name}", affected: newBlock);
        _undo.Push(edit);
        AfterStructuralInsert(newBlock);
    }

    /// <summary>Insert at the end of the selection's file.</summary>
    private void InsertTopLevel(HudBlock newBlock)
    {
        if (ActiveFile is not { } file) return;
        var topLevel = file.Document.TopLevel;
        var index = topLevel.Count;
        topLevel.Insert(index, newBlock);
        var edit = new InsertBlockEdit(topLevel, index, newBlock, $"insert top-level {newBlock.Name}", affected: newBlock);
        _undo.Push(edit);
        AfterStructuralInsert(newBlock);
    }

    private void AfterStructuralInsert(HudBlock newBlock)
    {
        Canvas.Refresh();
        RefreshEventList();
        Properties.SetBlock(newBlock);
        StatusText.Text = $"Inserted {newBlock.Name} \"{FirstName(newBlock)}\"";
    }

    private static string FirstName(HudBlock b) =>
        b.Arguments.Count > 0 && b.Arguments[0].Kind == HudArgKind.String ? b.Arguments[0].Raw : "(unnamed)";

    /// <summary>A name no block of any file of the HUD has yet.</summary>
    private string UniqueName(string baseName)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in _files) CollectNames(file.Document.TopLevel, existing);
        if (!existing.Contains(baseName)) return baseName;
        for (var i = 1; i < 10_000; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
        return baseName;
    }

    private static void CollectNames(IEnumerable<HudBlock> blocks, HashSet<string> into)
    {
        foreach (var b in blocks)
        {
            if (b.Arguments.Count > 0 && b.Arguments[0].Kind == HudArgKind.String)
                into.Add(b.Arguments[0].Raw);
            CollectNames(b.Children, into);
        }
    }

    private void Canvas_BlockSelected(object? sender, HudBlock block)
    {
        if (Locate(block) is not { } at) return;

        _suppressSyncBack = true;
        try
        {
            SelectPath(at.File, at.Path);
            Canvas.HighlightBlock(block);
        }
        finally { _suppressSyncBack = false; }
    }

    /// <summary>
    /// Select the tree row for the last block of <paramref name="path"/>, the blocks from the top of
    /// <paramref name="file"/> down to it, expanding each on the way.
    /// </summary>
    private void SelectPath(HudFile file, IReadOnlyList<HudBlock> path)
    {
        Tree.UpdateLayout();
        if (Tree.ItemContainerGenerator.ContainerFromItem(file) is not TreeViewItem current) return;
        foreach (var block in path)
        {
            current.IsExpanded = true;
            current.UpdateLayout();
            if (current.ItemContainerGenerator.ContainerFromItem(block) is not TreeViewItem next) return;
            current = next;
        }
        current.IsSelected = true;
        current.BringIntoView();
    }

    internal static LocalizationTable? TryLoadLocalization(AssetResolver assets)
    {
        var path = assets.FindLocalizationCfg("english");
        if (path is null) return null;
        try { return LocalizationTable.LoadFromCfg(path); }
        catch { return null; }
    }

    private static int CountBlocks(IEnumerable<HudBlock> blocks)
    {
        var total = 0;
        foreach (var b in blocks) total += 1 + CountBlocks(b.Children);
        return total;
    }
}
