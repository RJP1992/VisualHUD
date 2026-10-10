using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    public static readonly RoutedCommand RenameCommand = new("Rename", typeof(MainWindow));
    public static readonly RoutedCommand ShowOnlySelectedCommand = new("ShowOnlySelected", typeof(MainWindow));

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

    /// <summary>The events sent, for tests.</summary>
    internal HudEventState EventState => _events;
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
    // The Elements tab brought forward for a moment to pick a row in its tree.
    private bool _borrowingTree;

    public MainWindow()
    {
        InitializeComponent();
        Tree.ItemsSource = _files;
        BuildInsertMenu(EditInsertInside, inside: true);
        BuildInsertMenu(EditInsertAfter, inside: false);
        BuildInsertMenu(TreeInsertInside, inside: true);
        BuildInsertMenu(TreeInsertAfter, inside: false);
        BuildInsertMenu(ToolbarInsertMenu, inside: true);
        Canvas.DocumentVisible = doc => FileOf(doc)?.IsVisible ?? true;
        Properties.IsDefaultLine = line => (_defaultLines ??= FindDefaultLines()).Contains(line);
        Properties.ParentOf = block => Locate(block) is { Path: { Count: > 1 } path } ? path[^2] : null;
        Properties.KnownEvents = KnownEvents;
        Properties.BasisOf = BasisFor;
        Properties.DrawnAspect = DrawnAspect;
        Properties.HeightForAspect = HeightForAspect;
        Properties.TextureAspect = TextureAspect;
        Properties.Preview = () => Canvas.Refresh();
        _undo.Changed += (_, _) =>
        {
            _defaultLines = null;
            UpdateTitle();
            RefreshAssetList();
            RefreshFindList();
            RefreshProblemList();
        };
        SetUpFind();
        SetUpProblems();
        SetUpArrange();
        EventsList.Bind(_events);
        // The events sent always reach the canvas; the Events tab's box decides whether they also show and hide.
        Canvas.SetEventState(_events);
        EventsList.SimulateChanged += (_, on) => Canvas.ShowByEvents = on;
        EventsList.ShowSelectedRequested += (_, _) => ShowSelectedInSimulation();
        EventsList.ScenarioPicked += (_, name) => ApplyScenario(name);
        EventsList.ScenarioSaveRequested += (_, name) => SaveScenario(name);
        EventsList.ScenarioDeleteRequested += (_, name) => DeleteScenario(name);
        EventsList.ShowScenarios(_settings.Scenarios.Select(s => s.Name));
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
        var exePath = Environment.ProcessPath;
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

    /// <summary>The files to open as the window loads; null opens the ones on the command line.</summary>
    internal IReadOnlyList<string>? StartupFiles { get; init; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var paths = (StartupFiles ?? Environment.GetCommandLineArgs().Skip(1).ToList()).Where(File.Exists).ToList();
        if (paths.Count > 0) LoadFiles(paths);
    }

    /// <summary>Select an element by name in the tree, as a click on its row would.</summary>
    internal bool SelectByName(string name)
    {
        foreach (var file in _files)
            foreach (var block in AllBlocks(file.Document.TopLevel))
                if (block.HasBody && string.Equals(FirstName(block), name, StringComparison.OrdinalIgnoreCase) &&
                    Locate(block) is { } at)
                {
                    SelectPath(at.File, at.Path);
                    return true;
                }
        return false;
    }

    private static IEnumerable<HudBlock> AllBlocks(IEnumerable<HudBlock> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            foreach (var inner in AllBlocks(b.Children)) yield return inner;
        }
    }

    /// <summary>Show a tab of the left column by its header: Elements, Events or Assets.</summary>
    internal void ShowLeftTab(string header)
    {
        foreach (var item in LeftTabs.Items.OfType<TabItem>())
            if ((item.Tag ?? item.Header) is string text && text.Equals(header, StringComparison.OrdinalIgnoreCase)) item.IsSelected = true;
    }

    /// <summary>Close without asking about unsaved changes, as the tests and the off-screen render do.</summary>
    internal bool CloseWithoutAsking { get; set; }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_closingForReload && !CloseWithoutAsking && !ConfirmDiscard("closing")) e.Cancel = true;
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
    internal void LoadFiles(IEnumerable<string> paths)
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
        AddFilePaths(dialog.FileNames.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Add these files to the open HUD, after its files, in this order.</summary>
    private void AddFilePaths(IEnumerable<string> paths)
    {
        if (_files.Count == 0)
        {
            LoadFiles(paths);
            return;
        }
        var added = new List<string>();
        foreach (var path in paths.Select(Path.GetFullPath))
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
        WatchFiles();
        ClearPicks();
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
        RefreshFindList();
        RefreshProblemList();
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
        var strings = _localization is { } table ? key => table.TryGet(key) is not null ? table.SourceOf(key) : null : (Func<string, string?>?)null;
        AssetsList.ShowAssets(_files.Count == 0 || _assets is null ? null
            : HudAssets.Check(Documents, _assets, _meshTextures, strings, _localization?.Files));
    }

    private void LeftTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The lists and boxes inside the tabs raise this too.
        if (!ReferenceEquals(e.OriginalSource, LeftTabs) || _borrowingTree) return;
        if (AssetsTab.IsSelected && _assetListStale) RefreshAssetList();
        if (FindTab.IsSelected && _findListStale) RefreshFindList();
        if (ProblemsTab.IsSelected && _problemListStale) RefreshProblemList();
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
        // The tests open windows by the dozen and need no assets.
        if (Environment.GetEnvironmentVariable("VISUALHUD_NO_SCAN") == "1") return;
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
        WatchFiles();
        UpdateTitle();
        return true;
    }

    private bool WriteFile(HudFile file, string path)
    {
        try
        {
            // Lines that only restate the engine's defaults are left out of the file. The file as it was
            // goes to _backups first.
            BackUp(path);
            File.WriteAllText(path, ElementDefaults.WriteWithoutDefaults(file.Document));
            NoteDiskText(Path.GetFullPath(path));
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
        if (e.Stepped)
        {
            // Steps of the arrows or the wheel on the same numbers, one after another, undo as one.
            var steps = e.Changes.Select(c => new ArgumentChange(e.Leaf, c.Index, c.Old, c.New)).ToList();
            var now = DateTime.UtcNow;
            var description = $"step {e.Leaf.Name}";
            if (_undo.Top is ArgumentsEdit last && last.Description == description && (now - _stepAt).TotalSeconds < 1.5 && last.Covers(steps))
            {
                last.Extend(steps);
                _undo.Touch();
            }
            else _undo.Push(new ArgumentsEdit(steps, description, e.SelectedBlock));
            _stepAt = now;
        }
        else
        {
            var edits = e.Changes
                .Select(c => (IHudEdit)new ReplaceArgumentEdit(e.Leaf, c.Index, c.Old, c.New, affected: e.SelectedBlock))
                .ToList();
            _undo.Push(edits.Count == 1 ? edits[0] : new CompositeEdit(edits, $"edit {e.Leaf.Name}"));
        }
        _knownEvents = null;
        Canvas.Refresh();
        RefreshEventList();
        Properties.RefreshDefaultMarks();
    }

    // When the last step of the arrows or the wheel was taken, to join the next to it.
    private DateTime _stepAt;

    // The event names offered for an event argument: the game's, GameExt's and the HUD's own.
    private IReadOnlyList<string>? _knownEvents;

    private IReadOnlyList<string> KnownEvents()
    {
        if (_knownEvents is not null) return _knownEvents;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in HudEventCatalog.All) names.Add(info.Name);
        if (Canvas.Transforms is { } graph)
            foreach (var use in HudEventUsage.Of(Documents, graph)) names.Add(use.Name);
        return _knownEvents = names.ToList();
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
    /// Send what shows the selected elements: the EventEnable event of each and of every group above
    /// it, top first, each once, with the value typed for each in the Events tab.
    /// </summary>
    private void ShowSelectedInSimulation()
    {
        var blocks = SelectedItems.OfType<HudBlock>().ToList();
        if (blocks.Count == 0) return;
        var sends = new List<(string, HudEventValue)>();
        var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            if (Locate(block) is not { } at) continue;
            foreach (var b in at.Path)
            {
                if (!ElementKinds.TryGet(b, out var kind)) continue;
                if (ElementReader.Read(b, kind).EventEnable is not { } name || !sent.Add(name)) continue;
                var type = HudEventCatalog.Find(name)?.Type ?? HudEventType.Bool;
                sends.Add((name, EventsList.TypedValue(name) ?? HudEventValue.Default(type)));
            }
        }
        if (sends.Count == 0)
        {
            StatusText.Text = blocks.Count == 1
                ? $"{blocks[0].Name} \"{FirstName(blocks[0])}\" has no EventEnable: the game never shows it."
                : "None of the selected elements has an EventEnable: the game never shows them.";
            return;
        }
        _events.SendAll(sends);
        StatusText.Text = $"Sent {string.Join(", ", sends.Select(s => s.Item1))}" +
                          (EventsList.Simulate ? string.Empty : " (tick Show and hide as the game does in the Events tab to see it)");
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
        // A table row, such as NumberColor, goes after the last row of its table.
        for (var i = target.Children.Count - 1; i >= 0 && !e.Property.HasBody; i--)
            if (!target.Children[i].HasBody && target.Children[i].Name.Equals(e.Property.Name, StringComparison.OrdinalIgnoreCase))
            {
                insertAt = i + 1;
                break;
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
        RefreshEventList();
        StatusText.Text = e.CopiedLast
            ? $"Added a copy of the last {e.Property.Name} row to {target.Name} \"{FirstName(target)}\": give it a key of its own."
            : $"Added {e.Property.Name} to {target.Name} \"{FirstName(target)}\"";
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // Moving the selection by a plain click or the keyboard leaves one row selected.
        if (!_picking)
        {
            ClearPicks();
            _anchor = e.NewValue;
        }
        var block = e.NewValue as HudBlock;
        Properties.SetBlock(block);
        // The guides and the layout line follow the selection's file.
        if (SelectedFile is { } file)
        {
            Canvas.ActiveDocument = file.Document;
            if (e.NewValue is HudFile && _picked.Count == 0)
                StatusText.Text = $"{file.FilePath ?? file.Name}    {Canvas.DescribeFile(file.Document)}";
        }
        if (_suppressSyncBack) return;
        ShowSelection();
    }

    private void VisibilityCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: { } item }) return;
        // Ctrl+click: this one alone.
        if (Keyboard.Modifiers == ModifierKeys.Control) ShowOnly(new[] { item });
        // The tick of a selected row sets every selected row.
        else if (_picked.Count > 1 && _picked.Contains(item))
        {
            var shown = IsShownInTree(item);
            foreach (var other in _picked) SetShownInTree(other, shown);
            StatusText.Text = $"{(shown ? "Shown" : "Hidden")}: the {_picked.Count} selected.";
        }
        Canvas.Refresh();
    }

    /// <summary>
    /// A left-click in the tree selects the row it is on, wherever on the row: WPF's own lets the
    /// embedded CheckBox take the click without selecting. Ctrl+click picks or drops a row among
    /// several selected, Shift+click picks the rows from the last one clicked; a tick's click leaves a
    /// selection of several as it is.
    /// </summary>
    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject hit) return;
        var node = hit;
        var onTick = false;
        var onArrow = false;
        while (node is not null and not TreeViewItem)
        {
            // A click in the rename box is the box's: taking focus from it would end the rename.
            if (node is TextBox) return;
            if (node is CheckBox) onTick = true;
            else if (node is ToggleButton) onArrow = true;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        if (node is not TreeViewItem tvi || tvi.DataContext is not { } item) return;
        // A double-click on an element's line renames it; on the rest of the row it opens or closes it.
        if (e.ClickCount == 2 && IsInHeaderLabel(hit) && item is HudBlock block)
        {
            BeginRename(block);
            e.Handled = true;
            return;
        }
        var modifiers = Keyboard.Modifiers;
        if (onTick)
        {
            // While several rows are selected a tick leaves them so: it sets its row, or every selected
            // row when its row is one of them (VisibilityCheckBox_Click).
            if (_picked.Count > 0) return;
        }
        else if (!onArrow && modifiers.HasFlag(ModifierKeys.Shift))
        {
            PickRange(item, add: modifiers.HasFlag(ModifierKeys.Control));
            e.Handled = true;
            return;
        }
        else if (!onArrow && modifiers.HasFlag(ModifierKeys.Control))
        {
            TogglePick(item);
            e.Handled = true;
            return;
        }
        tvi.IsSelected = true;
        tvi.Focus();
        // A drag may start from here (Tree_PreviewMouseMove).
        if (!onTick && !onArrow && item is HudBlock dragged)
        {
            _dragStart = e.GetPosition(Tree);
            _dragBlock = dragged;
        }
    }

    /// <summary>Whether a click landed on an element's keyword and name in its row.</summary>
    private static bool IsInHeaderLabel(DependencyObject? node)
    {
        for (; node is not null and not TreeViewItem; node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement { Name: "HeaderLabel" }) return true;
        return false;
    }

    /// <summary>A right-click selects the row under it, so the context menu acts on that row, or on the selected rows it is one of.</summary>
    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var node = e.OriginalSource as DependencyObject;
        while (node is not null and not TreeViewItem)
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        if (node is not TreeViewItem tvi || _picked.Contains(tvi.DataContext)) return;
        tvi.IsSelected = true;
        tvi.Focus();
    }

    // ── Several rows ─────────────────────────────────────────────────────────────────

    // The rows picked with Ctrl or Shift, the tree's own selection among them; empty while one row is.
    private readonly List<object> _picked = new();
    // Where a Shift+click's rows start: the last row clicked or picked.
    private object? _anchor;
    // The tree's selection is moving to a picked row: the others stay picked.
    private bool _picking;

    /// <summary>The selected rows, files or blocks, as they stand: the picked ones, else the tree's selection.</summary>
    internal IReadOnlyList<object> SelectedItems =>
        _picked.Count > 0
            ? _picked.Where(i => i is HudFile f ? _files.Contains(f) : i is HudBlock b && Locate(b) is not null).ToList()
            : Tree.SelectedItem is { } one ? new[] { one } : Array.Empty<object>();

    private static bool IsShownInTree(object item) => item switch
    {
        HudBlock b => b.IsVisible,
        HudFile f => f.IsVisible,
        _ => true,
    };

    private static void SetShownInTree(object item, bool shown)
    {
        if (item is HudBlock b) b.IsVisible = shown;
        else if (item is HudFile f) f.IsVisible = shown;
    }

    private static void SetPickedFlag(object item, bool picked)
    {
        if (item is HudBlock b) b.IsMultiSelected = picked;
        else if (item is HudFile f) f.IsMultiSelected = picked;
    }

    private void Pick(object item)
    {
        if (_picked.Contains(item)) return;
        _picked.Add(item);
        SetPickedFlag(item, true);
    }

    internal void ClearPicks()
    {
        foreach (var item in _picked) SetPickedFlag(item, false);
        _picked.Clear();
    }

    /// <summary>Ctrl+click: add a row to the selected ones, or drop it; the row selected so far joins them.</summary>
    internal void TogglePick(object item)
    {
        // The one row selected stays selected: a selection is never empty.
        if (_picked.Count == 0 && ReferenceEquals(Tree.SelectedItem, item)) return;
        if (_picked.Count == 0 && Tree.SelectedItem is { } current) Pick(current);
        if (_picked.Contains(item))
        {
            _picked.Remove(item);
            SetPickedFlag(item, false);
            if (_picked.Count <= 1)
            {
                // One row left is a plain selection again.
                var only = _picked.FirstOrDefault();
                ClearPicks();
                if (only is not null) MakeCurrent(only);
            }
            else if (ReferenceEquals(Tree.SelectedItem, item)) MakeCurrent(_picked[^1]);
        }
        else
        {
            Pick(item);
            MakeCurrent(item);
        }
        _anchor = item;
        ShowSelection();
    }

    /// <summary>Shift+click: the rows from the last one clicked to this one, as the tree shows them; with Ctrl, added to those picked.</summary>
    internal void PickRange(object item, bool add)
    {
        var rows = ShownRows();
        var from = rows.IndexOf(_anchor ?? Tree.SelectedItem ?? item);
        var to = rows.IndexOf(item);
        if (from < 0 || to < 0)
        {
            TogglePick(item);
            return;
        }
        if (!add) ClearPicks();
        else if (_picked.Count == 0 && Tree.SelectedItem is { } current) Pick(current);
        for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++) Pick(rows[i]);
        if (_picked.Count == 1) ClearPicks();
        MakeCurrent(item);
        ShowSelection();
    }

    /// <summary>The rows of the tree from the top, the open ones' rows under them.</summary>
    private List<object> ShownRows()
    {
        var rows = new List<object>();
        void Walk(ItemsControl parent)
        {
            foreach (var item in parent.Items)
            {
                rows.Add(item);
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem { IsExpanded: true } open) Walk(open);
            }
        }
        Walk(Tree);
        return rows;
    }

    /// <summary>Make a picked row the one the Properties panel shows, keeping the others picked.</summary>
    private void MakeCurrent(object item)
    {
        _picking = true;
        try
        {
            switch (item)
            {
                case HudFile file when Tree.ItemContainerGenerator.ContainerFromItem(file) is TreeViewItem row:
                    row.IsSelected = true;
                    row.Focus();
                    break;
                case HudBlock block when Locate(block) is { } at:
                    SelectPath(at.File, at.Path)?.Focus();
                    break;
            }
        }
        finally { _picking = false; }
    }

    /// <summary>Outline the selected elements on the canvas, and say how many rows are selected.</summary>
    private void ShowSelection()
    {
        // The toolbar's Copy, Duplicate and Move follow the selection.
        CommandManager.InvalidateRequerySuggested();
        var items = SelectedItems;
        Canvas.HighlightBlocks(items.OfType<HudBlock>().ToList(), Tree.SelectedItem as HudBlock);
        if (_picked.Count > 1)
            StatusText.Text = $"{_picked.Count} selected: H hides or shows them, Shift+H shows only them, Del deletes them.";
    }

    private static string Describe(IReadOnlyList<object> items) => items.Count switch
    {
        1 when items[0] is HudBlock b => $"{b.Name} \"{FirstName(b)}\"",
        1 when items[0] is HudFile f => f.Name,
        _ => $"the {items.Count} selected",
    };

    private void Canvas_HoverInfoChanged(object? sender, string? info)
    {
        HoverText.Text = info ?? string.Empty;
    }

    private void Canvas_BlockDragged(object? sender, HudBlock block)
    {
        // The position's numbers follow the drag; the panel is built again only if the drag added a line.
        if (!Properties.ShowsBlock(block) || !Properties.RefreshValues()) Properties.SetBlock(block);
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
        ClearPicks();
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
        if (ToggleHideSelected()) e.Handled = true;
    }

    /// <summary>Hide the selected rows, or show them when all are hidden already.</summary>
    internal bool ToggleHideSelected()
    {
        var items = SelectedItems;
        if (items.Count == 0) return false;
        var shown = !items.Any(IsShownInTree);
        foreach (var item in items) SetShownInTree(item, shown);
        StatusText.Text = $"{(shown ? "Shown" : "Hidden")}: {Describe(items)}";
        Canvas.Refresh();
        return true;
    }

    /// <summary>Alt+H keybinding — unhides every hidden block and file.</summary>
    private void UnhideAll_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        ShowEverything();
        e.Handled = true;
    }

    internal void ShowEverything()
    {
        var count = 0;
        foreach (var file in _files)
        {
            if (!file.IsVisible) { file.IsVisible = true; count++; }
            count += UnhideAll(file.Document.TopLevel);
        }
        Canvas.Refresh();
        StatusText.Text = count > 0 ? $"Unhid {count} element{(count == 1 ? "" : "s")}." : "Nothing was hidden.";
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

    private void ToggleHide_Click(object sender, RoutedEventArgs e) => ToggleHideSelected();

    private void ShowOnly_Click(object sender, RoutedEventArgs e) => ShowOnlySelected();
    private void ShowOnly_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        ShowOnlySelected();
        e.Handled = true;
    }

    private void ShowOnlySelected()
    {
        var items = SelectedItems;
        if (items.Count == 0)
        {
            StatusText.Text = "Select elements or files in the tree to show only them.";
            return;
        }
        ShowOnly(items);
        Canvas.Refresh();
    }

    /// <summary>
    /// Hide everything but <paramref name="items"/> on the canvas: the other files, and in the files of
    /// the blocks among them every block that neither holds one nor is in one. A hidden block's own
    /// blocks keep their ticks, so showing it again brings them back as they were; what is in one of
    /// <paramref name="items"/> all shows, and a whole file among them keeps its ticks.
    /// </summary>
    internal void ShowOnly(IReadOnlyList<object> items)
    {
        var wholeFiles = items.OfType<HudFile>().ToHashSet();
        var kept = new HashSet<HudBlock>(ReferenceEqualityComparer.Instance);
        var holders = new HashSet<HudBlock>(ReferenceEqualityComparer.Instance);
        var pruned = new HashSet<HudFile>();
        foreach (var block in items.OfType<HudBlock>())
        {
            if (Locate(block) is not { } at) continue;
            kept.Add(block);
            holders.UnionWith(at.Path);
            if (!wholeFiles.Contains(at.File)) pruned.Add(at.File);
        }
        // A file without elements, such as the transforms, draws nothing: it keeps its tick.
        foreach (var f in _files)
            if (HudLayout.TopLevelElements(f.Document).Any()) f.IsVisible = wholeFiles.Contains(f) || pruned.Contains(f);
        void Apply(IEnumerable<HudBlock> blocks)
        {
            foreach (var b in blocks.Where(b => b.HasBody))
            {
                if (kept.Contains(b)) UnhideAll(new[] { b });
                else if (holders.Contains(b))
                {
                    b.IsVisible = true;
                    Apply(b.Children);
                }
                // A ViewPort's elements are top-level ones; FileInfo and transforms draw nothing.
                else if (!ElementKinds.TryGet(b, out _)) Apply(b.Children);
                else b.IsVisible = false;
            }
        }
        foreach (var file in pruned) Apply(file.Document.TopLevel);
        StatusText.Text = $"Showing only {Describe(items)}. Alt+H shows everything again.";
    }

    /// <summary>Show the selected rows, what holds them and everything in them.</summary>
    private void ShowInside_Click(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems;
        if (items.Count == 0) return;
        foreach (var item in items)
        {
            switch (item)
            {
                case HudFile file:
                    file.IsVisible = true;
                    UnhideAll(file.Document.TopLevel);
                    break;
                case HudBlock block when Locate(block) is { } at:
                    at.File.IsVisible = true;
                    foreach (var holder in at.Path) holder.IsVisible = true;
                    UnhideAll(block.Children);
                    break;
            }
        }
        StatusText.Text = $"Showing all of {Describe(items)}.";
        Canvas.Refresh();
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e) => ShowEverything();

    // ── Structural edits ───────────────────────────────────────────────────────────

    private void DeleteSelected_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    /// <summary>Delete the selected block, or take the selected file out of the HUD.</summary>
    /// <summary>
    /// Delete the selected blocks as one undoable edit; with only files selected, take them out of the
    /// HUD. A block inside another selected one goes with it.
    /// </summary>
    internal void DeleteSelected()
    {
        var items = SelectedItems;
        var blocks = items.OfType<HudBlock>().ToList();
        if (blocks.Count == 0)
        {
            foreach (var file in items.OfType<HudFile>().ToList()) RemoveFile(file);
            return;
        }
        var selected = new HashSet<HudBlock>(blocks, ReferenceEqualityComparer.Instance);
        var targets = new List<(ObservableCollection<HudBlock> List, int Index, HudBlock Block, HudBlock? Parent)>();
        foreach (var block in blocks)
        {
            if (Locate(block) is not { } at || at.Path.Take(at.Path.Count - 1).Any(selected.Contains)) continue;
            if (at.File.Document.FindContainer(block) is not { } container) continue;
            targets.Add((container.Container, container.Index, block, container.Parent));
        }
        if (targets.Count == 0) return;

        // The later ones in a list first, so each index still holds its block; undo puts them back in turn.
        var edits = targets.OrderByDescending(t => t.Index)
            .Select(t => (IHudEdit)new DeleteBlockEdit(t.List, t.Index, t.Block, affected: t.Parent))
            .ToList();
        var first = targets[0];
        IHudEdit edit = edits.Count == 1 ? edits[0] : new CompositeEdit(edits, $"delete {edits.Count} blocks");
        edit.Apply();
        _undo.Push(edit);

        ClearPicks();
        Canvas.Refresh();
        Properties.SetBlock(first.Parent);
        ShowSelection();
        RefreshEventList();
        StatusText.Text = targets.Count == 1
            ? $"Deleted {first.Block.Name} \"{FirstName(first.Block)}\""
            : $"Deleted {targets.Count} blocks. Ctrl+Z puts them back.";
    }

    // ── Insert ───────────────────────────────────────────────────────────────────────

    /// <summary>Where an insert goes: a file, the block that will hold it (null: the top of the file), the list and the index.</summary>
    private sealed record InsertTarget(HudFile File, HudBlock? Container, ObservableCollection<HudBlock> List, int Index);

    /// <summary>
    /// Fill an Insert menu with every block the editor can make, by category; a category of one is a
    /// single item. Whether each fits is decided when the menu opens (<see cref="UpdateInsertMenu"/>).
    /// </summary>
    private void BuildInsertMenu(ItemsControl root, bool inside)
    {
        TemplateFamily? family = null;
        foreach (var category in HudBlockTemplates.Categories)
        {
            var templates = HudBlockTemplates.All.Where(t => t.Category == category).ToList();
            if (templates.Count == 0) continue;
            if (family is { } last && last != templates[0].Family) root.Items.Add(new Separator());
            family = templates[0].Family;
            if (templates.Count == 1)
            {
                root.Items.Add(TemplateItem(templates[0], inside));
                continue;
            }
            var submenu = new MenuItem { Header = category };
            foreach (var template in templates) submenu.Items.Add(TemplateItem(template, inside));
            root.Items.Add(submenu);
        }
    }

    private MenuItem TemplateItem(BlockTemplate template, bool inside)
    {
        var item = new MenuItem
        {
            Header = template.Keyword,
            InputGestureText = template.GameExt ? "GameExt" : string.Empty,
            ToolTip = template.Description,
            Tag = (template, inside),
        };
        ToolTipService.SetShowOnDisabled(item, true);
        item.Click += InsertTemplate_Click;
        return item;
    }

    private static IEnumerable<MenuItem> TemplateItems(ItemsControl menu) =>
        menu.Items.OfType<MenuItem>().SelectMany(m => m.Tag is ValueTuple<BlockTemplate, bool> ? new[] { m } : TemplateItems(m));

    private void TreeMenu_Opened(object sender, RoutedEventArgs e)
    {
        UpdateInsertMenu(TreeInsertInside, inside: true);
        UpdateInsertMenu(TreeInsertAfter, inside: false);
        UpdateRenameItem(TreeRename);
        TreeToggleHide.Header = SelectedItems.Any(IsShownInTree) ? "Hide" : "Show";
    }

    private void EditMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        UpdateInsertMenu(EditInsertInside, inside: true);
        UpdateInsertMenu(EditInsertAfter, inside: false);
        UpdateRenameItem(EditRename);
    }

    /// <summary>Enable what fits where the insert would go; a greyed entry's tooltip says why not.</summary>
    private void UpdateInsertMenu(ItemsControl root, bool inside)
    {
        var target = InsertTargetFor(inside, out var why);
        if (root is MenuItem menu)
        {
            menu.IsEnabled = target is not null;
            menu.ToolTip = target is null ? why : $"Into {Describe(target)}.";
        }
        foreach (var item in TemplateItems(root))
        {
            if (target is null)
            {
                item.IsEnabled = false;
                item.ToolTip = why;
                continue;
            }
            var (template, _) = (ValueTuple<BlockTemplate, bool>)item.Tag;
            var reason = WhyNotInsert(template, target);
            item.IsEnabled = reason is null;
            item.ToolTip = reason is null ? template.Description : $"{reason}\n\n{template.Description}";
        }
    }

    private string? WhyNotInsert(BlockTemplate template, InsertTarget target) =>
        HudBlockTemplates.WhyNot(template, target.Container, target.File.Document) ??
        (template.Keyword != "FileInfo" && template.NewName(IsNameTaken()) is null
            ? "Every player has an ObjectiveList already. The game fills only the list named playerNobjectives."
            : null);

    /// <summary>
    /// Where Insert inside or Insert after would put a block, or null with <paramref name="why"/> not.
    /// Inside: into the selected block, at its end, or at the end of the selected file (or the first
    /// file, with nothing selected). After: right after the selected block, beside it.
    /// </summary>
    private InsertTarget? InsertTargetFor(bool inside, out string? why)
    {
        why = null;
        if (inside)
        {
            if (Tree.SelectedItem is HudBlock block && FileOf(block) is { } file)
            {
                why = HudBlockTemplates.WhyNothingFits(block);
                return why is null ? new InsertTarget(file, block, block.Children, block.Children.Count) : null;
            }
            if (ActiveFile is not { } active)
            {
                why = "Open a HUD file first.";
                return null;
            }
            return new InsertTarget(active, null, active.Document.TopLevel, active.Document.TopLevel.Count);
        }
        if (Tree.SelectedItem is not HudBlock selected || FileOf(selected) is not { } owner ||
            owner.Document.FindContainer(selected) is not { } at)
        {
            why = "Select an element in the tree to insert after it.";
            return null;
        }
        why = HudBlockTemplates.WhyNothingFits(at.Parent);
        return why is null ? new InsertTarget(owner, at.Parent, at.Container, at.Index + 1) : null;
    }

    private static string Describe(InsertTarget target) =>
        target.Container is { } block ? $"{block.Name} \"{FirstName(block)}\"" : target.File.Name;

    private void InsertTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: ValueTuple<BlockTemplate, bool> tag }) InsertTemplate(tag.Item1, tag.Item2);
    }

    /// <summary>Insert a new block of <paramref name="template"/> inside or after the selection; the block, or null.</summary>
    internal HudBlock? InsertTemplate(BlockTemplate template, bool inside)
    {
        if (InsertTargetFor(inside, out var why) is not { } target)
        {
            StatusText.Text = why ?? "There is nowhere to insert it.";
            return null;
        }
        if (WhyNotInsert(template, target) is { } reason)
        {
            StatusText.Text = reason;
            return null;
        }
        var name = template.Keyword == "FileInfo"
            ? Path.GetFileNameWithoutExtension(target.File.FilePath) ?? template.BaseName
            : template.NewName(IsNameTaken())!;
        // Inside an element it sits at its group's origin; at the top of a file, or in a ViewPort, mid-screen.
        var block = template.Create(name, nested: target.Container is { } container && ElementKinds.TryGet(container, out _));
        // FileInfo is read first: the game decides from it whether to load the file at all.
        var index = template.Keyword == "FileInfo" ? 0 : target.Index;
        // A blank line before it, as the stock files space their elements.
        if (index > 0) block.Trivia = new HudTrivia { Before = new List<string> { string.Empty } };
        target.List.Insert(index, block);
        _undo.Push(new InsertBlockEdit(target.List, index, block, $"insert {block.Name} \"{name}\"", affected: block));

        Canvas.Refresh();
        RefreshEventList();
        if (Locate(block) is { } at) SelectPath(at.File, at.Path);
        StatusText.Text = $"Inserted {block.Name} \"{name}\" into {Describe(target)}" +
                          (template.GameExt ? ". It needs GameExt: the stock game skips it." : ".");
        // Name it straight away, as a new folder is; Enter or a click elsewhere keeps the name.
        if (HudRename.WhyNot(block, target.Container) is null)
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => BeginRename(block));
        return block;
    }

    /// <summary>The open files, in load order.</summary>
    internal IReadOnlyList<HudFile> Files => _files;

    internal UndoManager History => _undo;

    internal string Status => StatusText.Text;

    private static string FirstName(HudBlock b) => HudRename.NameOf(b) ?? "(unnamed)";

    /// <summary>Whether an element or another block of any file of the HUD has a name; the game hashes names without case.</summary>
    private Func<string, bool> IsNameTaken()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in _files) CollectNames(file.Document.TopLevel, names);
        return names.Contains;
    }

    private static void CollectNames(IEnumerable<HudBlock> blocks, HashSet<string> into)
    {
        foreach (var b in blocks.Where(b => b.HasBody))
        {
            if (HudRename.NameOf(b) is { } name) into.Add(name);
            CollectNames(b.Children, into);
        }
    }

    // ── Rename ───────────────────────────────────────────────────────────────────────

    // The tree row being renamed: its block, the box in place of its line, the line, and the row.
    private (HudBlock Block, TextBox Box, FrameworkElement Label, TreeViewItem Row)? _renaming;

    private void Rename_Click(object sender, RoutedEventArgs e) => BeginRenameSelected();
    private void Rename_Executed(object sender, ExecutedRoutedEventArgs e) => BeginRenameSelected();

    private void UpdateRenameItem(MenuItem item)
    {
        var why = Tree.SelectedItem is HudBlock block ? RenameRefusal(block) : "Select an element in the tree to rename it.";
        item.IsEnabled = why is null;
        item.ToolTip = why ?? "Rename the selected element in the tree.";
    }

    private string? RenameRefusal(HudBlock block) => HudRename.WhyNot(block, ParentOf(block));

    private HudBlock? ParentOf(HudBlock block) => Locate(block) is { Path: { Count: > 1 } path } ? path[^2] : null;

    private void BeginRenameSelected()
    {
        if (Tree.SelectedItem is HudBlock block) BeginRename(block);
        else StatusText.Text = "Select an element in the tree to rename it.";
    }

    /// <summary>Swap the block's line in the tree for a box holding its name, selected for typing over.</summary>
    internal void BeginRename(HudBlock block)
    {
        if (_renaming is not null) return;
        if (RenameRefusal(block) is { } why)
        {
            StatusText.Text = why;
            return;
        }
        if (Locate(block) is not { } at || SelectPath(at.File, at.Path) is not { } row ||
            row.Template.FindName("PART_Header", row) is not ContentPresenter header ||
            header.ContentTemplate?.FindName("RenameBox", header) is not TextBox box ||
            header.ContentTemplate.FindName("HeaderLabel", header) is not FrameworkElement label)
        {
            Properties.FocusName();
            return;
        }
        _renaming = (block, box, label, row);
        box.Text = HudRename.NameOf(block);
        label.Visibility = Visibility.Collapsed;
        box.Visibility = Visibility.Visible;
        box.Focus();
        box.SelectAll();
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        var row = _renaming?.Row;
        EndRename(commit: e.Key == Key.Enter);
        row?.Focus();
        e.Handled = true;
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndRename(commit: true);

    private void EndRename(bool commit)
    {
        if (_renaming is not { } renaming) return;
        // First, so the focus the hidden box loses does not end it again.
        _renaming = null;
        renaming.Box.Visibility = Visibility.Collapsed;
        renaming.Label.Visibility = Visibility.Visible;
        if (commit) ApplyRename(renaming.Block, renaming.Box.Text);
    }

    // ---- Scenarios: sets of sends saved under a name ----------------------------------------------------

    /// <summary>Save the sends made so far, with their times, under a name; a name in use is replaced.</summary>
    internal void SaveScenario(string name)
    {
        var history = _events.History;
        if (history.Count == 0)
        {
            StatusText.Text = "Nothing has been sent yet: send events, then save them as a scenario.";
            return;
        }
        var scenario = new SavedScenario
        {
            Name = name,
            Sends = history.Select(s => new SavedSend
            {
                Event = s.Name,
                Type = s.Value.Type.ToString(),
                Value = ScenarioText(s.Value),
                Time = s.Time,
            }).ToList(),
        };
        _settings.Scenarios.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        _settings.Scenarios.Add(scenario);
        _settings.Scenarios.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        var error = _settings.Save();
        EventsList.ShowScenarios(_settings.Scenarios.Select(s => s.Name), name);
        StatusText.Text = error is null ? $"Saved {history.Count} sends as \"{name}\"." : $"Could not save the scenario: {error}";
    }

    /// <summary>A value written so it reads back exactly: floats in full, a colour with its alpha.</summary>
    private static string ScenarioText(HudEventValue v)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return v.Type switch
        {
            HudEventType.Float => v.Float.ToString("R", c),
            HudEventType.Vector3 => string.Join(", ", new[] { v.Vector.X, v.Vector.Y, v.Vector.Z }.Select(f => f.ToString("R", c))),
            HudEventType.Color => $"{v.Color.R}, {v.Color.G}, {v.Color.B}, {v.Color.A}",
            _ => HudEventText.Format(v),
        };
    }

    /// <summary>Send a saved scenario again, at its times, in place of what is sent now; the clock goes to 0.</summary>
    internal void ApplyScenario(string name)
    {
        if (_settings.Scenarios.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } scenario) return;
        _events.Clear();
        var skipped = 0;
        foreach (var send in scenario.Sends.OrderBy(s => s.Time))
        {
            if (!Enum.TryParse<HudEventType>(send.Type, out var type) || !HudEventText.TryParse(type, send.Value, out var value))
            {
                skipped++;
                continue;
            }
            _events.SetTime(send.Time);
            _events.Send(send.Event, value);
        }
        _events.SetTime(0);
        StatusText.Text = $"Sent \"{scenario.Name}\": {scenario.Sends.Count - skipped} events" +
                          (skipped > 0 ? $", {skipped} that could not be read again skipped." : ".");
    }

    internal void DeleteScenario(string name)
    {
        if (_settings.Scenarios.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) == 0) return;
        var error = _settings.Save();
        EventsList.ShowScenarios(_settings.Scenarios.Select(s => s.Name), string.Empty);
        StatusText.Text = error is null ? $"Forgot the scenario \"{name}\"." : $"Could not save the settings: {error}";
    }

    // ---- Nudging: the arrow keys on the canvas move the selection -------------------------------------

    private DateTime _nudgeAt;

    private void Canvas_NudgeRequested(object? sender, System.Windows.Vector by) => Nudge(by.X, by.Y);

    /// <summary>Move the selection by screen pixels; a run of nudges joins one undo step.</summary>
    internal void Nudge(double dx, double dy)
    {
        var blocks = SelectedItems.OfType<HudBlock>().ToList();
        var changes = Canvas.MoveChanges(blocks, dx, dy);
        if (changes.Count == 0)
        {
            StatusText.Text = blocks.Count == 0 ? "Select an element to move it with the arrow keys."
                : "Nothing to move: the selection has no Position line (add one from the Add tab).";
            return;
        }
        var now = DateTime.UtcNow;
        if (_undo.Top is ArgumentsEdit last && last.Description == "nudge" && (now - _nudgeAt).TotalSeconds < 1.5 && last.Covers(changes))
        {
            last.Extend(changes);
            last.Apply();
            _undo.Touch();
        }
        else
        {
            var edit = new ArgumentsEdit(changes, "nudge", blocks.FirstOrDefault());
            edit.Apply();
            _undo.Push(edit);
        }
        _nudgeAt = now;
        Canvas.Refresh();
        if (!Properties.RefreshValues()) Properties.SetBlock(Tree.SelectedItem as HudBlock);
    }

    // ---- Shapes: a BitmapRect's height for the shape it is drawn with -------------------------------

    /// <summary>
    /// How many screen pixels one of the element's own pixels takes, across and down: its matrix's axes,
    /// with the stock layout's stretch and band, GameExt's layout and every group's scale in them.
    /// </summary>
    private (double Across, double Down)? DrawnScale(HudBlock element)
    {
        if (Canvas.LaidOut(element) is not { } e) return null;
        var m = e.ToScreen;
        var across = Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
        var down = Math.Sqrt(m.M21 * m.M21 + m.M22 * m.M22);
        return across > 0 && down > 0 ? (across, down) : null;
    }

    /// <summary>A BitmapRect's drawn width over its drawn height, as the canvas draws it now.</summary>
    internal double? DrawnAspect(HudBlock element, HudBlock line)
    {
        if (DrawnScale(element) is not { } scale || BasisFor(element) is not { } basis || line.Arguments.Count < 2 ||
            !EngineArgs.IsNumber(line.Arguments[0]) || !EngineArgs.IsNumber(line.Arguments[1])) return null;
        var mode = UnitConversion.ModeOf(line);
        var w = basis.ToPixels(mode, EngineArgs.Float(line.Arguments[0]), false) * scale.Across;
        var h = basis.ToPixels(mode, EngineArgs.Float(line.Arguments[1]), true) * scale.Down;
        return h > 0 ? w / h : null;
    }

    /// <summary>The height a BitmapRect needs, in its own unit, to be drawn <paramref name="aspect"/> times as wide as tall.</summary>
    internal double? HeightForAspect(HudBlock element, HudBlock line, double aspect)
    {
        if (aspect <= 0 || DrawnScale(element) is not { } scale || BasisFor(element) is not { } basis || line.Arguments.Count < 2 ||
            !EngineArgs.IsNumber(line.Arguments[0])) return null;
        var mode = UnitConversion.ModeOf(line);
        var drawnWidth = basis.ToPixels(mode, EngineArgs.Float(line.Arguments[0]), false) * scale.Across;
        var heightPixels = drawnWidth / aspect / scale.Down;
        return Math.Round(basis.ToRelative(mode, (float)heightPixels, true), 6);
    }

    /// <summary>The element's texture's width over its height, over the part its TexCoords show.</summary>
    internal double? TextureAspect(HudBlock element)
    {
        if (Canvas.LaidOut(element) is not { Texture: { } name } e || Canvas.TextureSizeOf(name) is not { } size) return null;
        var uv = e.Spec.TexCoords;
        var du = Math.Abs(uv.U1 - uv.U0);
        var dv = Math.Abs(uv.V1 - uv.V0);
        return du > 0 && dv > 0 && size.Height > 0 ? size.Width * du / (size.Height * dv) : null;
    }

    // ---- Units: a line's numbers rewritten in another unit, so nothing moves ------------------------------

    /// <summary>
    /// The sizes a block's numbers are read against, as the canvas lays it out now: an element's own, and
    /// for a block inside one (a MeshInfo, a template), its element's, a group's Rect being a template's frame.
    /// </summary>
    internal UnitBasis? BasisFor(HudBlock block)
    {
        var inner = false;
        for (HudBlock? b = block; b is not null; b = ParentOf(b), inner = true)
        {
            if (Canvas.LaidOut(b) is not { } e) continue;
            var groupFrame = inner && ElementKinds.IsGroupBased(e.Kind) && e.RectWidthPixels > 0 && e.RectHeightPixels > 0;
            return new UnitBasis(
                groupFrame ? e.RectWidthPixels : e.FrameWidth, groupFrame ? e.RectHeightPixels : e.FrameHeight,
                e.ViewWidth, e.ViewHeight, e.ScreenWidth, e.ScreenHeight);
        }
        return null;
    }

    /// <summary>
    /// The edits that rewrite in <paramref name="to"/> the unit lines of these blocks and of every block in
    /// them. A VehicleSeating's own Position is left: the game reads it as pixels whatever its mode.
    /// </summary>
    private List<IHudEdit> UnitEdits(IEnumerable<HudBlock> roots, RelativeMode to, List<string> skipped)
    {
        var edits = new List<IHudEdit>();
        void Visit(HudBlock block)
        {
            foreach (var line in block.Children.Where(c => !c.HasBody && UnitConversion.Of(c.Name) is not null))
            {
                if (block.Name == "VehicleSeating" && line.Name == "Position")
                {
                    skipped.Add($"{block.Name} \"{block.ItemName}\" Position (read as pixels whatever its mode)");
                    continue;
                }
                if (!UnitConversion.CanConvert(line, to) || BasisFor(block) is not { } basis) continue;
                if (UnitConversion.Convert(line, to, basis) is { } args && !args.SequenceEqual(line.Arguments))
                    edits.Add(new ReplaceArgumentsEdit(line, args, block));
            }
            foreach (var child in block.Children.Where(c => c.HasBody)) Visit(child);
        }
        foreach (var root in roots) Visit(root);
        return edits;
    }

    /// <summary>Edit > Convert units: the selection and everything in it, or whole files, in one undo step.</summary>
    internal int ConvertUnits(IReadOnlyList<object> items, RelativeMode to)
    {
        var roots = items.SelectMany(i => i switch
        {
            HudFile file => file.Document.TopLevel.Where(b => b.HasBody),
            HudBlock block => new[] { block },
            _ => Enumerable.Empty<HudBlock>(),
        }).ToList();
        var skipped = new List<string>();
        var edits = UnitEdits(roots, to, skipped);
        var at = Canvas.ScreenSize;
        if (edits.Count == 0)
        {
            StatusText.Text = $"Nothing to convert to {to}: every Position, Rect and BitmapRect there is in it already, or cannot take it." +
                              (skipped.Count > 0 ? $" Left: {string.Join("; ", skipped)}." : string.Empty);
            return 0;
        }
        foreach (var edit in edits) edit.Apply();
        _undo.Push(new CompositeEdit(edits, $"convert {edits.Count} lines to {to}"));
        Canvas.Refresh();
        Properties.SetBlock(Tree.SelectedItem as HudBlock);
        StatusText.Text = $"Converted {edits.Count} line{(edits.Count == 1 ? "" : "s")} to {to}, keeping them where they are at {at.Width}×{at.Height}." +
                          (skipped.Count > 0 ? $" Left: {string.Join("; ", skipped)}." : string.Empty);
        return edits.Count;
    }

    private void ConvertUnits_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } && Enum.TryParse<RelativeMode>(name, out var to)) ConvertUnits(SelectedItems, to);
    }

    private void Properties_ShapeRefused(object? sender, string why) => StatusText.Text = why;

    /// <summary>A unit chosen in the Properties panel: the line's numbers follow.</summary>
    private void Properties_UnitChangeRequested(object? sender, UnitChangeRequestedEventArgs e)
    {
        if (BasisFor(e.Target) is not { } basis || UnitConversion.Convert(e.Line, e.To, basis) is not { } args) return;
        var edit = new ReplaceArgumentsEdit(e.Line, args, e.Target);
        edit.Apply();
        _undo.Push(edit);
        Canvas.Refresh();
        Properties.SetBlock(e.Target);
        var at = Canvas.ScreenSize;
        StatusText.Text = $"{e.Line.Name} in {e.To}, where it was at {at.Width}×{at.Height}.";
    }

    private void Properties_RenameRequested(object? sender, RenameRequestedEventArgs e)
    {
        if (!ApplyRename(e.Block, e.NewName)) Properties.RefreshHeader();
    }

    /// <summary>
    /// Rename a block, and the lines that find it by name, as one undoable edit; the status bar says
    /// what the game makes of the new name. False when the name cannot be written.
    /// </summary>
    internal bool ApplyRename(HudBlock block, string newName)
    {
        if (HudRename.NameOf(block) is not { } oldName) return false;
        newName = newName.Trim();
        if (newName == oldName) return true;
        if (HudRename.Invalid(newName) is { } invalid)
        {
            StatusText.Text = invalid;
            return false;
        }
        var plan = HudRename.Plan(Documents, block, newName);
        plan.Edit.Apply();
        _undo.Push(plan.Edit);
        Canvas.Refresh();
        RefreshEventList();
        Properties.RefreshHeader();
        StatusText.Text = $"Renamed {block.Name} \"{oldName}\" to \"{newName}\"." +
                          (plan.Notes.Count > 0 ? " " + string.Join(" ", plan.Notes) : string.Empty);
        return true;
    }

    private void Canvas_BlockSelected(object? sender, HudBlock block)
    {
        if (Locate(block) is not { } at) return;
        // Ctrl+click on the canvas picks or drops it, as in the tree.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            TogglePick(block);
            return;
        }

        _suppressSyncBack = true;
        try
        {
            ClearPicks();
            SelectPath(at.File, at.Path);
            Canvas.HighlightBlock(block);
        }
        finally { _suppressSyncBack = false; }
    }

    /// <summary>
    /// Select the tree row for the last block of <paramref name="path"/>, the blocks from the top of
    /// <paramref name="file"/> down to it, expanding each on the way; the row, or null when it has none.
    /// </summary>
    private TreeViewItem? SelectPath(HudFile file, IReadOnlyList<HudBlock> path)
    {
        // The tree makes its rows only while it is laid out. With another tab in front (a match picked in
        // the Find tab, a click on the canvas over the Events tab), the Elements tab comes forward for as
        // long as picking the row takes, before anything is drawn, and the other goes back.
        if (!ElementsTab.IsSelected)
        {
            var front = LeftTabs.SelectedItem;
            var focused = Keyboard.FocusedElement;
            _borrowingTree = true;
            try
            {
                ElementsTab.IsSelected = true;
                LeftTabs.UpdateLayout();
                return SelectPath(file, path);
            }
            finally
            {
                LeftTabs.SelectedItem = front;
                LeftTabs.UpdateLayout();
                _borrowingTree = false;
                if (focused is DependencyObject d && PresentationSource.FromDependencyObject(d) is not null) Keyboard.Focus(focused);
            }
        }
        Tree.UpdateLayout();
        if (Tree.ItemContainerGenerator.ContainerFromItem(file) is not TreeViewItem current) return null;
        foreach (var block in path)
        {
            current.IsExpanded = true;
            current.UpdateLayout();
            if (current.ItemContainerGenerator.ContainerFromItem(block) is not TreeViewItem next) return null;
            current = next;
        }
        current.IsSelected = true;
        current.BringIntoView();
        current.UpdateLayout();
        return current;
    }

    /// <summary>
    /// The English strings as the game ends up with them: the stock localization, its PC overrides,
    /// then each mod's and its PC overrides, a later file's string replacing an earlier one's.
    /// </summary>
    internal static LocalizationTable? TryLoadLocalization(AssetResolver assets)
    {
        var layers = assets.FindLocalizationLayers("english");
        if (layers.Count == 0) return null;
        try { return LocalizationTable.LoadLayered(layers); }
        catch { return null; }
    }

    private static int CountBlocks(IEnumerable<HudBlock> blocks)
    {
        var total = 0;
        foreach (var b in blocks) total += 1 + CountBlocks(b.Children);
        return total;
    }
}
