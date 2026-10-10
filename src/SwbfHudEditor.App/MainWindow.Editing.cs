using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App;

/// <summary>New files, the clipboard, duplicating and moving blocks, the tree's filter, the title bar.</summary>
public partial class MainWindow
{
    public static readonly RoutedCommand DuplicateCommand = new("Duplicate", typeof(MainWindow));
    public static readonly RoutedCommand MoveUpCommand = new("MoveUp", typeof(MainWindow));
    public static readonly RoutedCommand MoveDownCommand = new("MoveDown", typeof(MainWindow));

    // ── Title bar ────────────────────────────────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>A dark title bar to go with the window (DWMWA_USE_IMMERSIVE_DARK_MODE; 19 before Windows 10 2004).</summary>
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = 1;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0) DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }

    // ── New file ─────────────────────────────────────────────────────────────────────

    private void New_Click(object sender, RoutedEventArgs e) => NewFile();
    private void New_Executed(object sender, ExecutedRoutedEventArgs e) => NewFile();

    /// <summary>Start a .hud file: its FileInfo, written to disk, alone or added to the open HUD.</summary>
    private void NewFile()
    {
        var dialog = new NewHudDialog(StartFolder() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            canAdd: UserFiles.Any()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FilePath;
        if (File.Exists(path) && MessageBox.Show(this, $"{path} exists. Replace it?", "New HUD file",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, dialog.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"{path}\n\n{ex.Message}", "Could not create the file", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (dialog.AddToOpen) AddFilePaths(new[] { path });
        else
        {
            if (!ConfirmDiscard("starting a new file")) return;
            LoadFiles(new[] { path });
        }
        if (FileAt(Path.GetFullPath(path)) is { } file) SelectPath(file, Array.Empty<HudBlock>());
        StatusText.Text = $"Created {path}. Insert elements into it with Insert, or paste them.";
    }

    // ── Clipboard ────────────────────────────────────────────────────────────────────

    private bool TypingInBox => Keyboard.FocusedElement is TextBoxBase;

    /// <summary>The selected blocks without those inside another selected one, which go with it.</summary>
    private List<HudBlock> TopSelectedBlocks()
    {
        var blocks = SelectedItems.OfType<HudBlock>().ToList();
        var set = new HashSet<HudBlock>(blocks, ReferenceEqualityComparer.Instance);
        return blocks.Where(b => Locate(b) is { } at && !at.Path.Take(at.Path.Count - 1).Any(set.Contains)).ToList();
    }

    private void BlockCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !TypingInBox && SelectedItems.OfType<HudBlock>().Any();
        e.ContinueRouting = TypingInBox;
    }

    private void Copy_Executed(object sender, ExecutedRoutedEventArgs e) => CopySelection();

    private void Cut_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (CopySelection()) DeleteSelected();
    }

    internal bool CopySelection()
    {
        var blocks = TopSelectedBlocks();
        if (blocks.Count == 0) return false;
        var indent = Locate(blocks[0])?.File.Document.Indent ?? "    ";
        try
        {
            Clipboard.SetText(HudClipboard.Write(blocks, indent));
        }
        catch (COMException)
        {
            StatusText.Text = "The clipboard is busy; try again.";
            return false;
        }
        StatusText.Text = $"Copied {Describe(blocks.Cast<object>().ToList())} as .hud text.";
        return true;
    }

    private void Paste_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !TypingInBox && _files.Count > 0 && Clipboard.ContainsText();
        e.ContinueRouting = TypingInBox;
    }

    private void Paste_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        string text;
        try { text = Clipboard.GetText(); }
        catch (COMException)
        {
            StatusText.Text = "The clipboard is busy; try again.";
            return;
        }
        Paste(text);
    }

    /// <summary>
    /// Paste <c>.hud</c> text: lines go into the selected block, replacing a line of the same name it
    /// has once; blocks go into the selection where they fit, else after it, renamed where their names
    /// are taken.
    /// </summary>
    internal bool Paste(string text)
    {
        if (HudClipboard.Read(text) is not { } blocks)
        {
            StatusText.Text = "The clipboard holds no .hud blocks.";
            return false;
        }
        if (blocks.All(b => !b.HasBody)) return PasteLines(blocks);
        if (blocks.Any(b => !b.HasBody))
        {
            StatusText.Text = "Paste lines and blocks separately: lines go into an element, blocks beside or into one.";
            return false;
        }

        // Inside the selection if everything fits there, else after it.
        var target = InsertTargetFor(inside: true, out _);
        if (target is null || blocks.Any(b => HudBlockTemplates.WhyNot(b, target.Container, target.File.Document) is not null))
            target = InsertTargetFor(inside: false, out _) is { } after &&
                     blocks.All(b => HudBlockTemplates.WhyNot(b, after.Container, after.File.Document) is null)
                ? after
                : null;
        if (target is null)
        {
            var reason = blocks.Select(b => InsertTargetFor(true, out _) is { } t ? HudBlockTemplates.WhyNot(b, t.Container, t.File.Document) : null)
                .FirstOrDefault(r => r is not null);
            StatusText.Text = reason ?? "Select where to paste first.";
            return false;
        }

        var renamed = HudClipboard.MakeNamesUnique(blocks, IsNameTaken(), IsEventTaken());
        InsertBlocks(target.List, target.Index, blocks, $"paste {blocks.Count} block{(blocks.Count == 1 ? "" : "s")}");
        StatusText.Text = $"Pasted {Describe(blocks.Cast<object>().ToList())} into {Describe(target)}." +
                          (renamed.Count > 0 ? $" Renamed, as the names were taken: {string.Join(", ", renamed.Select(r => $"{r.From} to {r.To}"))}." : string.Empty);
        return true;
    }

    /// <summary>Lines into the selected block: one it has once is replaced, any other is added.</summary>
    private bool PasteLines(IReadOnlyList<HudBlock> lines)
    {
        if (Tree.SelectedItem is not HudBlock target)
        {
            StatusText.Text = "Select the element to paste the lines into.";
            return false;
        }
        var edits = new List<IHudEdit>();
        foreach (var line in lines)
        {
            var same = target.Children.Where(c => !c.HasBody && c.Name.Equals(line.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (same.Count == 1 && same[0].Arguments.Count == line.Arguments.Count)
            {
                for (var i = 0; i < line.Arguments.Count; i++)
                    if (same[0].Arguments[i] != line.Arguments[i])
                        edits.Add(new ReplaceArgumentEdit(same[0], i, same[0].Arguments[i], line.Arguments[i], affected: target));
                continue;
            }
            var at = target.Children.TakeWhile(c => !c.HasBody).Count();
            var lastSame = same.Count > 0 ? target.Children.IndexOf(same[^1]) + 1 : at;
            edits.Add(new InsertBlockEdit(target.Children, lastSame, line, $"paste {line.Name}", affected: target));
            // Each insert lands before the next one is made, so apply as they are built.
            edits[^1].Apply();
        }
        foreach (var edit in edits.OfType<ReplaceArgumentEdit>()) edit.Apply();
        if (edits.Count == 0)
        {
            StatusText.Text = "The element has those lines already.";
            return false;
        }
        _undo.Push(new CompositeEdit(edits, $"paste {lines.Count} line{(lines.Count == 1 ? "" : "s")} into {target.Name}"));
        AfterBlocksChanged(target);
        StatusText.Text = $"Pasted {lines.Count} line{(lines.Count == 1 ? "" : "s")} into {target.Name} \"{FirstName(target)}\".";
        return true;
    }

    /// <summary>The events transforms send already, which a pasted transform must not send too.</summary>
    private Func<string, bool> IsEventTaken()
    {
        var events = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _, _) in HudTransformGraph.From(Documents).Outputs) events.Add(name);
        return events.Contains;
    }

    private void Duplicate_Executed(object sender, ExecutedRoutedEventArgs e) => DuplicateSelection();

    /// <summary>A copy of each selected block right after it, renamed, in one undo step.</summary>
    internal bool DuplicateSelection()
    {
        var blocks = TopSelectedBlocks();
        if (blocks.Count == 0) return false;
        var taken = IsNameTaken();
        var events = IsEventTaken();
        var copies = new List<HudBlock>();
        var edits = new List<IHudEdit>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Later ones first in each list, so each original's index still holds when its copy goes in.
        foreach (var block in blocks.OrderByDescending(b => Locate(b)?.File.Document.FindContainer(b)?.Index ?? 0))
        {
            if (Locate(block) is not { } at || at.File.Document.FindContainer(block) is not { } container) continue;
            var copy = HudClipboard.Clone(block);
            HudClipboard.MakeNamesUnique(new[] { copy }, n => taken(n) || names.Contains(n), events);
            Collect(copy, names);
            var edit = new InsertBlockEdit(container.Container, container.Index + 1, copy, $"duplicate {block.Name}", affected: copy);
            edit.Apply();
            edits.Add(edit);
            copies.Add(copy);
        }
        if (edits.Count == 0) return false;
        _undo.Push(edits.Count == 1 ? edits[0] : new CompositeEdit(edits, $"duplicate {edits.Count} blocks"));
        copies.Reverse();
        AfterBlocksChanged(null);
        SelectBlocks(copies);
        StatusText.Text = $"Duplicated {Describe(copies.Cast<object>().ToList())}.";
        return true;

        static void Collect(HudBlock block, HashSet<string> into)
        {
            if (block.HasBody && block.ItemName.Length > 0) into.Add(block.ItemName);
            foreach (var child in block.Children) Collect(child, into);
        }
    }

    /// <summary>Insert blocks one after another from <paramref name="index"/>, as one undo step, and select them.</summary>
    private void InsertBlocks(ObservableCollection<HudBlock> list, int index, IReadOnlyList<HudBlock> blocks, string description)
    {
        var edits = new List<IHudEdit>();
        foreach (var block in blocks)
        {
            if (index > 0) block.Trivia ??= new HudTrivia { Before = new List<string> { string.Empty } };
            var edit = new InsertBlockEdit(list, index++, block, description, affected: block);
            edit.Apply();
            edits.Add(edit);
        }
        _undo.Push(edits.Count == 1 ? edits[0] : new CompositeEdit(edits, description));
        AfterBlocksChanged(null);
        SelectBlocks(blocks);
    }

    /// <summary>Select these blocks in the tree: one as a plain selection, several picked together.</summary>
    private void SelectBlocks(IReadOnlyList<HudBlock> blocks)
    {
        ClearPicks();
        if (blocks.Count == 0) return;
        if (blocks.Count > 1) foreach (var block in blocks) Pick(block);
        MakeCurrent(blocks[^1]);
        ShowSelection();
    }

    /// <summary>After blocks were added, moved or removed: the canvas, the events and the panel follow.</summary>
    private void AfterBlocksChanged(HudBlock? show)
    {
        Canvas.Refresh();
        RefreshEventList();
        if (show is not null) Properties.SetBlock(show);
        if (TreeFilter.Text.Length > 0) ApplyTreeFilter();
    }

    // ── Move up and down ─────────────────────────────────────────────────────────────

    private void MoveCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !TypingInBox && SelectedItems.OfType<HudBlock>().Any();
        e.ContinueRouting = TypingInBox;
    }

    private void MoveUp_Executed(object sender, ExecutedRoutedEventArgs e) => MoveSelection(-1);
    private void MoveDown_Executed(object sender, ExecutedRoutedEventArgs e) => MoveSelection(+1);

    /// <summary>
    /// Move the selected blocks past the block before or after them in their list, lines skipped: for
    /// elements of an equal ZOrder, the order is the draw order.
    /// </summary>
    internal bool MoveSelection(int direction)
    {
        var blocks = TopSelectedBlocks();
        var selected = new HashSet<HudBlock>(blocks, ReferenceEqualityComparer.Instance);
        var edits = new List<IHudEdit>();
        var order = direction < 0
            ? blocks.OrderBy(b => Locate(b)?.File.Document.FindContainer(b)?.Index ?? 0)
            : blocks.OrderByDescending(b => Locate(b)?.File.Document.FindContainer(b)?.Index ?? 0);
        foreach (var block in order.ToList())
        {
            if (Locate(block) is not { } at || at.File.Document.FindContainer(block) is not { } container) continue;
            var list = container.Container;
            var i = container.Index;
            var j = i + direction;
            while (j >= 0 && j < list.Count && !list[j].HasBody) j += direction;
            // At the end of its list, or against another selected block: it stays.
            if (j < 0 || j >= list.Count || selected.Contains(list[j])) continue;
            var edit = new MoveBlockEdit(list, i, list, j, block);
            edit.Apply();
            edits.Add(edit);
        }
        if (edits.Count == 0)
        {
            StatusText.Text = direction < 0 ? "Nothing to move it above." : "Nothing to move it below.";
            return false;
        }
        _undo.Push(edits.Count == 1 ? edits[0] : new CompositeEdit(edits, $"move {edits.Count} blocks"));
        AfterBlocksChanged(null);
        SelectBlocks(blocks);
        StatusText.Text = $"Moved {Describe(blocks.Cast<object>().ToList())} {(direction < 0 ? "up" : "down")}. Among elements of one ZOrder, the order is the draw order.";
        return true;
    }

    /// <summary>
    /// Move blocks into <paramref name="list"/> at <paramref name="index"/> (counted before they leave
    /// their places), in their order, as one undo step: a drop in the tree.
    /// </summary>
    /// <summary>
    /// Move blocks to a place in a list. With <paramref name="keepPlace"/> the elements stay where they are drawn
    /// in their new parent, their positions (and a Scale, for a parent that scales otherwise) written again,
    /// and one moved to the top of a file takes the viewport it showed in.
    /// </summary>
    internal bool MoveBlocks(IReadOnlyList<HudBlock> blocks, ObservableCollection<HudBlock> list, int index, bool keepPlace = true)
    {
        var options = keepPlace ? KeepPlaceOptions : null;
        var elements = blocks.Where(IsElement).ToList();
        var keep = options is not null && elements.Count > 0 ? HudKeepPlace.Capture(HudLayout.Lay(Documents, options), elements) : null;
        var viewports = elements.ToDictionary(b => b, b => Canvas.LaidOut(b)?.Viewport ?? 0);
        var edits = new List<IHudEdit>();
        foreach (var block in blocks)
        {
            if (Locate(block) is not { } at || at.File.Document.FindContainer(block) is not { } from) continue;
            var to = index;
            if (ReferenceEquals(from.Container, list) && from.Index < index) to--;
            if (!ReferenceEquals(from.Container, list)) ForgetIndentation(block);
            var edit = new MoveBlockEdit(from.Container, from.Index, list, to, block);
            edit.Apply();
            edits.Add(edit);
            index = to + 1;
        }
        if (edits.Count == 0) return false;
        var moved = edits.Count;
        _moveNotes.Clear();
        if (keep is not null)
        {
            if (IsTopList(list))
                foreach (var block in elements.Where(b => viewports[b] > 0 && !b.Children.Any(c => !c.HasBody && c.Name == "Viewport")))
                {
                    var add = new InsertBlockEdit(block.Children, 0, new HudBlock("Viewport", HudArgument.FromInt(viewports[block])), "add Viewport", affected: block);
                    add.Apply();
                    edits.Add(add);
                }
            edits.AddRange(keep.Restore(HudLayout.Lay(Documents, options!), _moveNotes));
        }
        _undo.Push(edits.Count == 1 ? edits[0] : new CompositeEdit(edits, $"move {moved} blocks"));
        AfterBlocksChanged(null);
        SelectBlocks(blocks);
        return true;
    }

    // What a move could not keep, said with it.
    private readonly List<string> _moveNotes = new();

    /// <summary>A block in a new place is written at that place's depth.</summary>
    private static void ForgetIndentation(HudBlock block)
    {
        if (block.Trivia is { } trivia)
        {
            trivia.Indent = null;
            trivia.OpenIndent = null;
            trivia.CloseIndent = null;
            trivia.Reindent = true;
        }
        foreach (var child in block.Children) ForgetIndentation(child);
    }

    // ── Drag and drop in the tree ────────────────────────────────────────────────────

    private const string DragFormat = "VisualHUD.Blocks";
    private Point? _dragStart;
    private HudBlock? _dragBlock;
    private DropMarker? _dropMarker;

    private enum DropPlace { Before, Inside, After }

    private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start || _dragBlock is not { } block || _renaming is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed) _dragStart = null;
            return;
        }
        var now = e.GetPosition(Tree);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragStart = null;
        var blocks = _picked.Contains(block) ? TopSelectedBlocks() : new List<HudBlock> { block };
        try
        {
            DragDrop.DoDragDrop(Tree, new DataObject(DragFormat, blocks), DragDropEffects.Move);
        }
        finally { ClearDropMarker(); }
    }

    /// <summary>Where a drop at <paramref name="point"/> would put blocks: the list, the index and the row it is on.</summary>
    private (ObservableCollection<HudBlock> List, int Index, HudBlock? Container, HudFile File, TreeViewItem Row, DropPlace Place)? DropTargetAt(Point point)
    {
        if (Tree.InputHitTest(point) is not DependencyObject hit) return null;
        var node = hit;
        while (node is not null and not TreeViewItem) node = VisualTreeHelper.GetParent(node);
        if (node is not TreeViewItem row) return null;
        if (row.Template.FindName("Bd", row) is not FrameworkElement header) return null;
        var y = Tree.TranslatePoint(point, header).Y;
        var h = header.ActualHeight;
        switch (row.DataContext)
        {
            case HudFile file:
                return (file.Document.TopLevel, file.Document.TopLevel.Count, null, file, row, DropPlace.Inside);
            case HudBlock block when Locate(block) is { } at && at.File.Document.FindContainer(block) is { } spot:
                var place = y < h * 0.3 ? DropPlace.Before : y > h * 0.7 ? DropPlace.After : DropPlace.Inside;
                // A block that holds nothing takes drops beside it.
                if (place == DropPlace.Inside && HudBlockTemplates.WhyNothingFits(block) is not null)
                    place = y < h / 2 ? DropPlace.Before : DropPlace.After;
                return place switch
                {
                    DropPlace.Inside => (block.Children, block.Children.Count, block, at.File, row, place),
                    DropPlace.Before => (spot.Container, spot.Index, spot.Parent, at.File, row, place),
                    _ => (spot.Container, spot.Index + 1, spot.Parent, at.File, row, place),
                };
        }
        return null;
    }

    private string? WhyNotDrop(IReadOnlyList<HudBlock> blocks, HudBlock? container, HudFile file)
    {
        foreach (var block in blocks)
        {
            // Not into itself or anything it holds.
            if (container is not null && (ReferenceEquals(block, container) ||
                                          Locate(container) is { } at && at.Path.Contains(block)))
                return "A block cannot go inside itself.";
            if (HudBlockTemplates.WhyNot(block, container, file.Document) is { } why) return why;
        }
        return null;
    }

    private void Tree_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(DragFormat) is not List<HudBlock> blocks || DropTargetAt(e.GetPosition(Tree)) is not { } target)
        {
            ClearDropMarker();
            return;
        }
        var why = WhyNotDrop(blocks, target.Container, target.File);
        e.Effects = why is null ? DragDropEffects.Move : DragDropEffects.None;
        ShowDropMarker(target.Row, target.Place, why is null);
        StatusText.Text = why ?? $"Drop to move {Describe(blocks.Cast<object>().ToList())} " +
            (target.Place == DropPlace.Inside ? $"into {(target.Container is { } c ? $"{c.Name} \"{FirstName(c)}\"" : target.File.Name)}."
                : target.Place == DropPlace.Before ? "before this row." : "after this row.");
        // Scroll near the edges.
        var y = e.GetPosition(Tree).Y;
        if (FindScroller(Tree) is { } scroller)
        {
            if (y < 24) scroller.LineUp();
            else if (y > Tree.ActualHeight - 24) scroller.LineDown();
        }
    }

    private void Tree_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropMarker();
        if (e.Data.GetData(DragFormat) is not List<HudBlock> blocks || DropTargetAt(e.GetPosition(Tree)) is not { } target) return;
        if (WhyNotDrop(blocks, target.Container, target.File) is { } why)
        {
            StatusText.Text = why;
            return;
        }
        // Alt held as it is dropped keeps the numbers as written, and the place as they make it.
        var keepPlace = (e.KeyStates & DragDropKeyStates.AltKey) == 0;
        if (MoveBlocks(blocks, target.List, target.Index, keepPlace))
            StatusText.Text = $"Moved {Describe(blocks.Cast<object>().ToList())}" +
                              (keepPlace ? ", where they were drawn." : ", its numbers as written (Alt).") +
                              (_moveNotes.Count > 0 ? " " + string.Join(" ", _moveNotes) : string.Empty);
    }

    private void Tree_DragLeave(object sender, DragEventArgs e)
    {
        if (DropTargetAt(e.GetPosition(Tree)) is null) ClearDropMarker();
    }

    private void ShowDropMarker(TreeViewItem row, DropPlace place, bool allowed)
    {
        if (row.Template.FindName("Bd", row) is not FrameworkElement header) return;
        if (_dropMarker is { } old && (!ReferenceEquals(old.AdornedElement, header) || old.Place != (int)place || old.Allowed != allowed))
            ClearDropMarker();
        if (_dropMarker is not null || AdornerLayer.GetAdornerLayer(header) is not { } layer) return;
        _dropMarker = new DropMarker(header, (int)place, allowed);
        layer.Add(_dropMarker);
    }

    private void ClearDropMarker()
    {
        if (_dropMarker is { } marker && AdornerLayer.GetAdornerLayer(marker.AdornedElement) is { } layer) layer.Remove(marker);
        _dropMarker = null;
    }

    private static ScrollViewer? FindScroller(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scroller) return scroller;
            if (FindScroller(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>Where a dragged row would land: a line above or below a row, or a box around it to go inside.</summary>
    private sealed class DropMarker : Adorner
    {
        public DropMarker(UIElement row, int place, bool allowed) : base(row)
        {
            Place = place;
            Allowed = allowed;
            IsHitTestVisible = false;
        }

        public int Place { get; }
        public bool Allowed { get; }

        protected override void OnRender(DrawingContext dc)
        {
            var brush = (Brush)Application.Current.Resources[Allowed ? "AccentBrush" : "ErrorBrush"];
            var pen = new Pen(brush, 2);
            var size = AdornedElement.RenderSize;
            switch (Place)
            {
                case (int)DropPlace.Before:
                    dc.DrawLine(pen, new Point(0, 0), new Point(size.Width, 0));
                    break;
                case (int)DropPlace.After:
                    dc.DrawLine(pen, new Point(0, size.Height), new Point(size.Width, size.Height));
                    break;
                default:
                    dc.DrawRoundedRectangle(null, pen, new Rect(1, 1, Math.Max(0, size.Width - 2), Math.Max(0, size.Height - 2)), 3, 3);
                    break;
            }
        }
    }

    // ── The tree: filter, expand and collapse ────────────────────────────────────────

    private void TreeFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyTreeFilter();

    /// <summary>Show only the rows whose name or keyword contains the filter, and the rows that hold them, opened.</summary>
    private void ApplyTreeFilter()
    {
        var text = TreeFilter.Text.Trim();
        var matches = new List<(HudFile File, IReadOnlyList<HudBlock> Path)>();
        bool Visit(HudFile file, HudBlock block, List<HudBlock> path)
        {
            path.Add(block);
            var match = text.Length == 0 ||
                        block.ItemName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                        block.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
            if (match && text.Length > 0) matches.Add((file, path.ToList()));
            var below = false;
            foreach (var child in block.Children.Where(c => c.HasBody)) below |= Visit(file, child, path);
            path.RemoveAt(path.Count - 1);
            block.IsFilteredOut = !(match || below);
            return match || below;
        }
        foreach (var file in _files)
            foreach (var block in file.Document.TopLevel.Where(b => b.HasBody)) Visit(file, block, new List<HudBlock>());
        if (text.Length == 0) return;
        // Open the rows down to each match, a few hundred at most.
        foreach (var (file, path) in matches.Take(300)) ExpandPath(file, path.Take(path.Count - 1).ToList());
        StatusText.Text = matches.Count == 0 ? $"No element's name or keyword contains \"{text}\"." : $"{matches.Count} rows match \"{text}\".";
    }

    private void ExpandPath(HudFile file, IReadOnlyList<HudBlock> path)
    {
        if (Tree.ItemContainerGenerator.ContainerFromItem(file) is not TreeViewItem current) return;
        current.IsExpanded = true;
        foreach (var block in path)
        {
            current.UpdateLayout();
            if (current.ItemContainerGenerator.ContainerFromItem(block) is not TreeViewItem next) return;
            next.IsExpanded = true;
            current = next;
        }
    }

    private void ExpandAll_Click(object sender, RoutedEventArgs e) => SetExpanded(Tree, true);
    private void CollapseAll_Click(object sender, RoutedEventArgs e) => SetExpanded(Tree, false);

    private static void SetExpanded(ItemsControl parent, bool expanded)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem row) continue;
            row.IsExpanded = expanded;
            if (expanded) row.UpdateLayout();
            SetExpanded(row, expanded);
        }
    }

    // ── The toolbar's Insert ─────────────────────────────────────────────────────────

    private void InsertButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateInsertMenu(ToolbarInsertMenu, inside: true);
        ToolbarInsertMenu.PlacementTarget = InsertButton;
        ToolbarInsertMenu.Placement = PlacementMode.Bottom;
        ToolbarInsertMenu.IsOpen = true;
    }
}
