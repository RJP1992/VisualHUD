using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    // Custom RoutedCommands so keybindings work regardless of which sub-control has focus.
    // The older PreviewKeyDown approach was getting swallowed by the TreeView's type-ahead
    // search — InputBinding/CommandBinding bypasses that entirely.
    public static readonly RoutedCommand ToggleHideSelectedCommand = new("ToggleHideSelected", typeof(MainWindow));
    public static readonly RoutedCommand UnhideAllCommand = new("UnhideAll", typeof(MainWindow));

    private HudDocument? _document;
    private bool _suppressSyncBack;
    private string? _currentPath;
    private readonly UndoManager _undo = new();
    // The events sent in the Events tab: the game state the canvas draws while simulating.
    private readonly HudEventState _events = new();

    public MainWindow()
    {
        InitializeComponent();
        _undo.Changed += (_, _) => UpdateTitle();
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
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenFile();
    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) => OpenFile();
    private void Reload_Click(object sender, RoutedEventArgs e) => ReloadCurrent();
    private void Reload_Executed(object sender, ExecutedRoutedEventArgs e) => ReloadCurrent();
    private void Reload_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !string.IsNullOrEmpty(_currentPath);

    /// <summary>
    /// Restart the entire editor process, re-opening the currently loaded file. The current
    /// window is closed after the new instance has been started so there's no dead window on
    /// screen. Use this when a code change or asset refresh needs a clean slate instead of a
    /// soft in-place reload.
    /// </summary>
    private void ReloadCurrent()
    {
        if (string.IsNullOrEmpty(_currentPath)) return;
        if (!File.Exists(_currentPath))
        {
            MessageBox.Show(this, $"File no longer exists:\n{_currentPath}", "Reload failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Our own exe + the current path as a cli argument. The Window_Loaded hook already
        // reads args[1] and calls LoadFile, so the new process opens the same document.
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
                Arguments = $"\"{_currentPath}\"",
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
        Application.Current.Shutdown();
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Save();
    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => Save();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();
    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => SaveAs();
    private void Save_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _document is not null;
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length >= 2 && File.Exists(args[1]))
            LoadFile(args[1]);
    }

    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "HUD files (*.hud)|*.hud|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(DefaultHudFolder) ? DefaultHudFolder : null,
        };

        if (dialog.ShowDialog(this) != true) return;
        LoadFile(dialog.FileName);
    }

    private void LoadFile(string path)
    {
        try
        {
            var src = File.ReadAllText(path);
            var doc = HudParser.Parse(src, path);
            _document = doc;
            _currentPath = path;
            _undo.Clear();
            var assets = AssetResolver.ForHudFile(path, Directory.Exists(StockDataCommon) ? StockDataCommon : null);
            var localization = TryLoadLocalization(assets);
            var transforms = TryLoadTransformDocuments(path);
            // The tree is the file itself: its top-level blocks in order, each holding its elements.
            Tree.ItemsSource = ElementsOnlyConverter.View(doc.TopLevel);
            Canvas.LoadDocument(doc, assets, localization, transforms);
            RefreshEventList();
            Properties.SetBlock(null);
            UpdateTitle();
            var locStatus = localization is null ? "no localize" : $"{localization.Count} strings";
            var transformCount = Canvas.Transforms?.Nodes.Count ?? 0;
            var xformStatus = transforms.Count == 0 && transformCount == 0 ? "no transforms"
                : $"{transformCount} transforms from {transforms.Count + 1} file{(transforms.Count == 0 ? "" : "s")}";
            StatusText.Text = $"{path}    {CountBlocks(doc.TopLevel)} blocks    {locStatus}    {xformStatus}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Failed to open file", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = $"Failed to open {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private void Save()
    {
        if (_document is null) return;
        if (string.IsNullOrEmpty(_currentPath)) { SaveAs(); return; }
        WriteToPath(_currentPath);
    }

    private void SaveAs()
    {
        if (_document is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "HUD files (*.hud)|*.hud|All files (*.*)|*.*",
            FileName = _currentPath is null ? "untitled.hud" : Path.GetFileName(_currentPath),
            InitialDirectory = _currentPath is null ? null : Path.GetDirectoryName(_currentPath),
        };
        if (dialog.ShowDialog(this) != true) return;
        WriteToPath(dialog.FileName);
        _currentPath = dialog.FileName;
        UpdateTitle();
    }

    private void WriteToPath(string path)
    {
        try
        {
            File.WriteAllText(path, HudWriter.Write(_document!));
            _undo.MarkSaved();
            UpdateTitle();
            StatusText.Text = $"Saved to {path}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateTitle()
    {
        var name = _currentPath is null ? "(unsaved)" : Path.GetFileName(_currentPath);
        Title = $"VisualHUD — {name}{(_undo.IsDirty ? " *" : "")}";
    }

    private void Properties_PropertyEdited(object? sender, PropertyEditedEventArgs e)
    {
        var edit = new ReplaceArgumentEdit(e.Leaf, e.ArgIndex, e.OldArg, e.NewArg, affected: e.SelectedBlock);
        _undo.Push(edit);
        Canvas.Refresh();
        RefreshEventList();
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

    /// <summary>List the events the document and its transforms use in the Events tab.</summary>
    private void RefreshEventList()
    {
        if (_document is null || Canvas.Transforms is not { } graph) return;
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
        EventsList.SetEvents(HudEventUsage.Of(_document, graph), Suggest);
    }

    /// <summary>
    /// Send what shows the selected element: the EventEnable event of it and of every group above it,
    /// top first, with the value typed for each in the Events tab.
    /// </summary>
    private void ShowSelectedInSimulation()
    {
        if (_document is null || Tree.SelectedItem is not HudBlock block || _document.FindPathTo(block) is not { } path) return;
        var sends = new List<(string, HudEventValue)>();
        foreach (var b in path)
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
        // insert at 0.
        var insertAt = 0;
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

    /// <summary>H keybinding — toggles the selected block's visibility on the canvas.</summary>
    private void ToggleHideSelected_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase) return;
        if (Tree.SelectedItem is not HudBlock block) return;
        block.IsVisible = !block.IsVisible;
        Canvas.Refresh();
        StatusText.Text = $"{(block.IsVisible ? "Shown" : "Hidden")}: {block.Name} \"{FirstName(block)}\"";
        e.Handled = true;
    }

    /// <summary>Alt+H keybinding — unhides every hidden block.</summary>
    private void UnhideAll_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase) return;
        if (_document is null) return;
        var count = UnhideAll(_document.TopLevel);
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

    private void DeleteSelected()
    {
        if (_document is null || Tree.SelectedItem is not HudBlock block) return;
        var container = _document.FindContainer(block);
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

    private void InsertChildOfSelected(HudBlock newBlock)
    {
        if (_document is null || Tree.SelectedItem is not HudBlock parent)
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

    private void InsertTopLevel(HudBlock newBlock)
    {
        if (_document is null) return;
        var index = _document.TopLevel.Count;
        _document.TopLevel.Insert(index, newBlock);
        var edit = new InsertBlockEdit(_document.TopLevel, index, newBlock, $"insert top-level {newBlock.Name}", affected: newBlock);
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

    private string UniqueName(string baseName)
    {
        if (_document is null) return baseName;
        var existing = new HashSet<string>(StringComparer.Ordinal);
        CollectNames(_document.TopLevel, existing);
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
        if (_document is null) return;
        var path = _document.FindPathTo(block);
        if (path is null) return;

        _suppressSyncBack = true;
        try
        {
            SelectPath(path);
            Canvas.HighlightBlock(block);
        }
        finally { _suppressSyncBack = false; }
    }

    /// <summary>
    /// Select the tree row for <paramref name="path"/>, the blocks from the top of the file down to
    /// the one wanted, expanding each on the way.
    /// </summary>
    private void SelectPath(IReadOnlyList<HudBlock> path)
    {
        if (path.Count == 0) return;
        ItemsControl current = Tree;
        for (var i = 0; i < path.Count; i++)
        {
            current.UpdateLayout();
            if (current.ItemContainerGenerator.ContainerFromItem(path[i]) is not TreeViewItem container)
                return;

            if (i < path.Count - 1)
            {
                container.IsExpanded = true;
                container.UpdateLayout();
                current = container;
            }
            else
            {
                container.IsSelected = true;
                container.BringIntoView();
            }
        }
    }

    internal static LocalizationTable? TryLoadLocalization(AssetResolver assets)
    {
        var path = assets.FindLocalizationCfg("english");
        if (path is null) return null;
        try { return LocalizationTable.LoadFromCfg(path); }
        catch { return null; }
    }

    /// <summary>
    /// The transform files a HUD loads with: the folder's own <c>hudtransforms.hud</c> (a mod's
    /// replaces the stock one), else the stock one, then any other <c>hudtransforms*.hud</c> beside the
    /// file, such as GameExt's. The file's own transforms are read from it after these. A file that does
    /// not parse is skipped: transforms only feed the preview.
    /// </summary>
    internal static IReadOnlyList<HudDocument> TryLoadTransformDocuments(string mainHudPath)
    {
        var paths = new List<string>();
        var hudDir = Path.GetDirectoryName(mainHudPath);
        var own = hudDir is null ? null : Path.Combine(hudDir, "hudtransforms.hud");
        if (own is not null && File.Exists(own)) paths.Add(own);
        else if (File.Exists(Path.Combine(DefaultHudFolder, "hudtransforms.hud"))) paths.Add(Path.Combine(DefaultHudFolder, "hudtransforms.hud"));
        if (hudDir is not null)
            foreach (var extra in Directory.GetFiles(hudDir, "hudtransforms*.hud").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                if (!paths.Contains(extra, StringComparer.OrdinalIgnoreCase)) paths.Add(extra);

        var docs = new List<HudDocument>();
        foreach (var path in paths)
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(mainHudPath), StringComparison.OrdinalIgnoreCase)) continue;
            try { docs.Add(HudParser.Parse(File.ReadAllText(path), path)); }
            catch { /* skip a file that does not parse */ }
        }
        return docs;
    }

    private static int CountBlocks(IEnumerable<HudBlock> blocks)
    {
        var total = 0;
        foreach (var b in blocks) total += 1 + CountBlocks(b.Children);
        return total;
    }
}
