using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App.Views;

/// <summary>One argument of a line, changed from <see cref="Old"/> to <see cref="New"/>.</summary>
public sealed record ArgChange(int Index, HudArgument Old, HudArgument New);

/// <summary>
/// Fired when arguments of a line were edited in the panel (they are already changed): the host makes
/// one undoable edit of them. A colour picked is its three arguments at once. <see cref="Stepped"/>: by the
/// arrow keys or the wheel, so a run of steps on the same numbers can undo as one.
/// </summary>
public sealed record PropertyEditedEventArgs(HudBlock Leaf, IReadOnlyList<ArgChange> Changes, HudBlock? SelectedBlock, bool Stepped = false);

/// <summary>
/// Fired when a line shown for several elements was edited: each of their lines of that name is changed
/// already, every argument changed listed, for one undo step.
/// </summary>
public sealed record ManyEditedEventArgs(string LineName, IReadOnlyList<SwbfHudEditor.Core.Edits.ArgumentChange> Changes, bool Stepped);

/// <summary>Fired when the user clicks an "Add" tab button to insert a property as a child of
/// <see cref="Target"/>. Carries the already-built leaf <see cref="Property"/> so the host can
/// wrap the insert in an undo edit and add it at the correct position. <see cref="CopiedLast"/>: a
/// table row copied from the block's last row of that name.</summary>
public sealed record PropertyInsertRequestedEventArgs(HudBlock Target, HudBlock Property, bool CopiedLast = false);

/// <summary>Fired by a row's delete button: remove <see cref="Property"/> from <see cref="Target"/>.</summary>
public sealed record PropertyDeleteRequestedEventArgs(HudBlock Target, HudBlock Property);

/// <summary>Fired by the name box in the header: rename <see cref="Block"/> to <see cref="NewName"/>.</summary>
public sealed record RenameRequestedEventArgs(HudBlock Block, string NewName);

/// <summary>A unit chosen for a line whose numbers it reads: the host converts them, so nothing moves.</summary>
public sealed record UnitChangeRequestedEventArgs(HudBlock Target, HudBlock Line, RelativeMode To);

/// <summary>
/// The selected block's lines, grouped as the Add tab groups what the game reads, each argument with
/// an editor for what it takes: a name from a list, a flag, a colour, an event, a number or text.
/// </summary>
public partial class PropertyPanel : UserControl
{
    private HudBlock? _block;
    private readonly List<(HudBlock Leaf, TextBlock Label)> _nameLabels = new();
    // The editors of the rows shown, for RefreshValues, and the lines the rows show.
    private readonly List<(FrameworkElement Editor, HudBlock Leaf, int Index)> _editors = new();
    private readonly List<HudBlock> _shownLeaves = new();
    // Sections closed by the user stay closed for the session.
    private static readonly HashSet<string> ClosedSections = new(StringComparer.Ordinal);

    /// <summary>Whether a line only restates the engine's default, so saving leaves it out. Set by the host.</summary>
    public Func<HudBlock, bool>? IsDefaultLine { get; set; }

    /// <summary>The block holding a block, which tells what a template reads. Set by the host.</summary>
    public Func<HudBlock, HudBlock?>? ParentOf { get; set; }

    /// <summary>The events the HUD knows, offered for an event argument. Set by the host.</summary>
    public Func<IReadOnlyList<string>>? KnownEvents { get; set; }

    /// <summary>Called while a colour is being picked, before it is committed, to redraw the canvas.</summary>
    public Action? Preview { get; set; }

    public event EventHandler<PropertyEditedEventArgs>? PropertyEdited;
    public event EventHandler<PropertyInsertRequestedEventArgs>? PropertyInsertRequested;
    public event EventHandler<PropertyDeleteRequestedEventArgs>? PropertyDeleteRequested;
    public event EventHandler<RenameRequestedEventArgs>? RenameRequested;
    public event EventHandler<UnitChangeRequestedEventArgs>? UnitChangeRequested;

    /// <summary>The sizes the shown block's numbers are read against, for numbers typed in pixels.</summary>
    public Func<HudBlock, UnitBasis?>? BasisOf { get; set; }

    /// <summary>A BitmapRect's drawn width over height; the height for a drawn shape; the texture's shape.</summary>
    public Func<HudBlock, HudBlock, double?>? DrawnAspect { get; set; }
    public Func<HudBlock, HudBlock, double, double?>? HeightForAspect { get; set; }
    public Func<HudBlock, double?>? TextureAspect { get; set; }

    /// <summary>Keep a BitmapRect's drawn shape when its width changes; it stays as other elements are picked.</summary>
    private bool _keepShape;

    public PropertyPanel()
    {
        InitializeComponent();
        PreviewKeyUp += (_, e) =>
        {
            if (!_altStepped || e.Key != Key.System || e.SystemKey is not (Key.LeftAlt or Key.RightAlt)) return;
            _altStepped = false;
            e.Handled = true;
        };
    }

    // A number stepped with Alt held: its release must not open the menu.
    private bool _altStepped;
    // A step being committed, and the wheel's turn not yet a whole notch.
    private bool _stepping;
    private int _wheel;

    // With several elements selected: each line shown stands for the line of its name in each of them.
    private IReadOnlyList<HudBlock>? _many;
    private readonly Dictionary<HudBlock, List<(HudBlock Block, HudBlock Line)>> _proxies = new(ReferenceEqualityComparer.Instance);

    public event EventHandler<ManyEditedEventArgs>? ManyEdited;

    /// <summary>The lines shown, in order: the block's own, or for several the ones standing for theirs.</summary>
    internal IReadOnlyList<HudBlock> ShownLines => _shownLeaves;

    /// <summary>The elements shown together, or null for one.</summary>
    internal IReadOnlyList<HudBlock>? Many => _many;

    /// <summary>The lines a line shown for several elements stands for, with their elements; null for a line of one.</summary>
    internal IReadOnlyList<(HudBlock Block, HudBlock Line)>? LinesOf(HudBlock leaf) => _proxies.TryGetValue(leaf, out var lines) ? lines : null;

    /// <summary>
    /// Show the lines several elements all have, <paramref name="current"/>'s first: a value they share shows,
    /// one that differs is blank, and a change goes to each of them.
    /// </summary>
    public void SetBlocks(IReadOnlyList<HudBlock> blocks, HudBlock current)
    {
        if (blocks.Count <= 1)
        {
            SetBlock(current);
            return;
        }
        if (NameBox.IsKeyboardFocusWithin) CommitName();
        _block = current;
        _many = blocks.ToList();
        Rebuild();
    }

    /// <summary>A line of several: each argument theirs where they all have it alike, else blank.</summary>
    private static List<HudArgument> Merged(HudBlock lead, IReadOnlyList<HudBlock> lines)
    {
        var args = new List<HudArgument>();
        for (var i = 0; i < lead.Arguments.Count; i++)
        {
            var arg = lead.Arguments[i];
            args.Add(lines.All(l => i < l.Arguments.Count && l.Arguments[i] == arg) ? arg : new HudArgument(arg.Kind, string.Empty));
        }
        return args;
    }

    /// <summary>The lines all the elements shown have, by name, as the one leading has them; the game reads the last of a name.</summary>
    private List<HudBlock> SharedLeaves()
    {
        _proxies.Clear();
        var shared = new List<HudBlock>();
        if (_many is null || _block is null) return shared;
        foreach (var name in _block.Children.Where(c => !c.HasBody).Select(c => c.Name).Distinct())
        {
            var pairs = _many.Select(b => (Block: b, Line: b.Children.LastOrDefault(c => !c.HasBody && c.Name == name))).ToList();
            if (pairs.Any(p => p.Line is null)) continue;
            var lines = pairs.Select(p => p.Line!).ToList();
            var proxy = new HudBlock(name);
            proxy.Arguments.AddRange(Merged(_block.Children.Last(c => !c.HasBody && c.Name == name), lines));
            _proxies[proxy] = pairs.Select(p => (p.Block, p.Line!)).ToList();
            shared.Add(proxy);
        }
        return shared;
    }

    /// <summary>
    /// An edit made in the panel: a line of one goes to the host as it is; a line shown for several is made on
    /// each of theirs.
    /// </summary>
    private void Edited(PropertyEditedEventArgs e)
    {
        if (!_proxies.TryGetValue(e.Leaf, out var pairs))
        {
            PropertyEdited?.Invoke(this, e);
            return;
        }
        var changes = new List<SwbfHudEditor.Core.Edits.ArgumentChange>();
        foreach (var (_, line) in pairs)
            foreach (var change in e.Changes)
            {
                if (change.Index >= line.Arguments.Count || line.Arguments[change.Index] == change.New) continue;
                changes.Add(new SwbfHudEditor.Core.Edits.ArgumentChange(line, change.Index, line.Arguments[change.Index], change.New));
                line.Arguments[change.Index] = change.New;
            }
        if (changes.Count > 0) ManyEdited?.Invoke(this, new ManyEditedEventArgs(e.Leaf.Name, changes, e.Stepped));
    }

    /// <summary>Whether a box takes the wheel: the one being typed in. The tests, whose windows are never active, say so.</summary>
    internal Func<TextBox, bool> TakesWheel { get; set; } = box => box.IsKeyboardFocusWithin;

    private Brush Res(string key) => (Brush)FindResource(key);

    public void SetBlock(HudBlock? block)
    {
        // A name typed and not yet committed belongs to the block shown so far: selecting another
        // element in the tree switches the panel before the box loses focus.
        if (NameBox.IsKeyboardFocusWithin && !ReferenceEquals(block, _block)) CommitName();
        _block = block;
        _many = null;
        Rebuild();
    }

    // The rows are built a moment after a selection, so the tree and the canvas answer at once and
    // only the last of a quick run of selections is built; the Add tab only when it is open.
    private int _rowsVersion;
    private bool _addStale;

    /// <summary>Whether rows are waiting to be built.</summary>
    public bool RowsPending { get; private set; }

    private void ScheduleRows()
    {
        var version = ++_rowsVersion;
        RowsPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (version != _rowsVersion) return;
            RowsPending = false;
            RebuildEditTab();
            _addStale = true;
            if (Tabs.SelectedIndex == 1) BuildAddTabIfStale();
        }));
    }

    /// <summary>The box that edits a line's argument, once the rows are built: for tests.</summary>
    internal TextBox? BoxFor(HudBlock leaf, int index) =>
        _editors.FirstOrDefault(e => ReferenceEquals(e.Leaf, leaf) && e.Index == index).Editor as TextBox;

    /// <summary>Build the rows now, if they are waiting: for tests and for code that reads them at once.</summary>
    public void FlushRows()
    {
        if (!RowsPending) return;
        _rowsVersion++;
        RowsPending = false;
        RebuildEditTab();
        _addStale = true;
        if (Tabs.SelectedIndex == 1) BuildAddTabIfStale();
    }

    private void BuildAddTabIfStale()
    {
        if (!_addStale) return;
        _addStale = false;
        AddRows.Children.Clear();
        RebuildAddTab();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Tabs) && Tabs.SelectedIndex == 1) BuildAddTabIfStale();
    }

    /// <summary>Whether the panel shows this block.</summary>
    public bool ShowsBlock(HudBlock block) => ReferenceEquals(block, _block) || _many?.Contains(block) == true;

    /// <summary>
    /// Put the lines' current values in their editors without building the rows again, as a drag changes
    /// them; false when the block's lines are no longer the ones shown, so the rows must be built again.
    /// </summary>
    public bool RefreshValues()
    {
        if (_block is null || RowsPending) return false;
        if (_many is not null)
        {
            // Several: each line shown takes its lines' values again, as long as it stands for the same lines.
            foreach (var (proxy, pairs) in _proxies)
            {
                if (pairs.Any(p => !ReferenceEquals(p.Block.Children.LastOrDefault(c => !c.HasBody && c.Name == proxy.Name), p.Line))) return false;
                var lead = pairs.FirstOrDefault(p => ReferenceEquals(p.Block, _block)).Line ?? pairs[0].Line;
                var merged = Merged(lead, pairs.Select(p => p.Line).ToList());
                for (var i = 0; i < merged.Count && i < proxy.Arguments.Count; i++) proxy.Arguments[i] = merged[i];
            }
            foreach (var (editor, leaf, index) in _editors)
                if (!editor.IsKeyboardFocusWithin && index < leaf.Arguments.Count && editor is TextBox box && box.Text != leaf.Arguments[index].Raw)
                    box.Text = leaf.Arguments[index].Raw;
            return true;
        }
        var leaves = _block.Children.Where(c => !c.HasBody).ToList();
        if (leaves.Count != _shownLeaves.Count || leaves.Where((l, i) => !ReferenceEquals(l, _shownLeaves[i])).Any()) return false;
        foreach (var (editor, leaf, index) in _editors)
        {
            if (editor.IsKeyboardFocusWithin || index >= leaf.Arguments.Count) continue;
            var raw = leaf.Arguments[index].Raw;
            if (editor is TextBox box && box.Text != raw) box.Text = raw;
        }
        return true;
    }

    /// <summary>Mark again the rows whose line restates a default, after an edit.</summary>
    public void RefreshDefaultMarks()
    {
        foreach (var (leaf, label) in _nameLabels) MarkDefault(leaf, label);
    }

    private void MarkDefault(HudBlock leaf, TextBlock label)
    {
        var isDefault = IsDefaultLine?.Invoke(leaf) == true;
        label.Foreground = isDefault ? Res("TextMutedBrush") : Res("TextSecondaryBrush");
        label.FontStyle = isDefault ? FontStyles.Italic : FontStyles.Normal;
        var description = _block is null ? null : PropertyCatalog.Find(_block, ParentOf?.Invoke(_block), leaf.Name)?.Description;
        label.ToolTip = (description is null ? leaf.Name : $"{leaf.Name}: {description}") +
                        (isDefault ? "\n\nThis is the engine's default: saving leaves this line out." : string.Empty);
    }

    // ── Header ───────────────────────────────────────────────────────────────────────

    /// <summary>Show the block's name again, after a rename elsewhere or one that was refused.</summary>
    public void RefreshHeader()
    {
        if (_block is null) return;
        if (_many is not null)
        {
            NameRow.Visibility = Visibility.Collapsed;
            HeaderText.Visibility = Visibility.Visible;
            HeaderText.Text = $"{_many.Count} elements: the lines they all have. A value they differ in is blank; a change goes to each of them.";
            return;
        }
        var name = HudRename.NameOf(_block);
        NameRow.Visibility = name is null ? Visibility.Collapsed : Visibility.Visible;
        HeaderText.Visibility = name is null ? Visibility.Visible : Visibility.Collapsed;
        HeaderText.Text = _block.ToString();
        if (name is null) return;
        KindText.Text = _block.Name;
        NameBox.Text = name;
        var refusal = HudRename.WhyNot(_block, ParentOf?.Invoke(_block));
        NameBox.IsReadOnly = refusal is not null;
        NameBox.ToolTip = refusal ?? "The block's name. Enter renames it, and the lines that name it; Esc puts it back.";
    }

    /// <summary>Put the caret in the name box, its name selected.</summary>
    public void FocusName()
    {
        if (NameRow.Visibility != Visibility.Visible || NameBox.IsReadOnly) return;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitName();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RefreshHeader();
            e.Handled = true;
        }
    }

    private void NameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitName();

    private void CommitName()
    {
        if (_block is null || NameBox.IsReadOnly || HudRename.NameOf(_block) is not { } name || NameBox.Text == name) return;
        RenameRequested?.Invoke(this, new RenameRequestedEventArgs(_block, NameBox.Text));
    }

    private void Rebuild()
    {
        Rows.Children.Clear();
        AddRows.Children.Clear();
        _nameLabels.Clear();
        _editors.Clear();
        _shownLeaves.Clear();
        if (_block is null)
        {
            NameRow.Visibility = Visibility.Collapsed;
            HeaderText.Visibility = Visibility.Visible;
            HeaderText.Text = "Nothing selected";
            Rows.Children.Add(Hint("Select an element in the tree or on the canvas to see its properties."));
            AddRows.Children.Add(Hint("Select an element in the tree or on the canvas to see its properties."));
            return;
        }
        RefreshHeader();
        ScheduleRows();
    }

    // ── The lines ────────────────────────────────────────────────────────────────────

    private void RebuildEditTab()
    {
        if (_block is null) return;
        var parent = ParentOf?.Invoke(_block);
        var leaves = _many is not null ? SharedLeaves() : _block.Children.Where(c => !c.HasBody).ToList();
        _shownLeaves.AddRange(leaves);
        if (leaves.Count == 0)
            Rows.Children.Add(Hint(_many is not null ? "They have no lines in common: add one to all of them from the Add tab."
                : "No lines yet: add what this block should read from the Add tab."));

        var groups = leaves
            .GroupBy(l => PropertyCatalog.Find(_block, parent, l.Name)?.Group ?? "Other")
            .OrderBy(g => PropertyCatalog.Groups.ToList().IndexOf(g.Key) is var i and >= 0 ? i : int.MaxValue);
        foreach (var group in groups)
        {
            var body = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var leaf in group) body.Children.Add(BuildRow(leaf));
            var section = new Expander
            {
                Style = (Style)FindResource("SectionExpander"),
                Header = new TextBlock { Text = group.Key.ToUpperInvariant(), Style = (Style)FindResource("Caption") },
                Content = body,
                IsExpanded = !ClosedSections.Contains(group.Key),
            };
            var key = group.Key;
            section.Collapsed += (_, _) => ClosedSections.Add(key);
            section.Expanded += (_, _) => ClosedSections.Remove(key);
            Rows.Children.Add(section);
        }

        var nested = _many is not null ? 0 : _block.Children.Count(c => c.HasBody);
        if (nested > 0)
            Rows.Children.Add(Hint($"+ {nested} block{(nested == 1 ? "" : "s")} inside, in the Elements tree."));
    }

    /// <summary>A line: its name, an editor for each argument, and a button that removes it.</summary>
    private FrameworkElement BuildRow(HudBlock leaf)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = leaf.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 6, 0),
        };
        MarkDefault(leaf, label);
        _nameLabels.Add((leaf, label));
        row.Children.Add(label);

        var editors = BuildEditors(leaf);
        Grid.SetColumn(editors, 1);
        row.Children.Add(editors);

        var delete = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = new TextBlock { Text = "", Style = (Style)FindResource("Icon"), FontSize = 10 },
            MinWidth = 22,
            MinHeight = 22,
            Padding = new Thickness(3),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = $"Remove {leaf.Name} from this element",
            Tag = leaf,
            Opacity = 0.55,
        };
        delete.Click += Delete_Click;
        Grid.SetColumn(delete, 2);
        row.Children.Add(delete);
        return row;
    }

    /// <summary>The editors of a line's arguments: one that fills the row, or several side by side.</summary>
    private FrameworkElement BuildEditors(HudBlock leaf)
    {
        if (PropertyArguments.IsFlag(leaf.Name) && leaf.Arguments.Count == 1 && IsNumberLike(leaf.Arguments[0]))
            return FlagEditor(leaf);

        if (leaf.Arguments.Count == 1)
        {
            var single = ArgEditor(leaf, 0);
            single.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (single is FrameworkElement { } box) box.Width = double.NaN;
            return single;
        }

        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        var colorAt = PropertyArguments.ColorStart(leaf.Name);
        for (var i = 0; i < leaf.Arguments.Count; i++)
        {
            if (colorAt == i && i + 2 < leaf.Arguments.Count) panel.Children.Add(ColorSwatch(leaf, i));
            panel.Children.Add(ArgEditor(leaf, i));
        }
        if (UnitConversion.Of(leaf.Name) is { } unit && leaf.Arguments.Count <= unit.Mode &&
            UnitConversion.CanConvert(leaf, unit.Default))
            panel.Children.Add(ImpliedUnitEditor(leaf, unit));
        if (leaf.Name == "BitmapRect" && leaf.Arguments.Count >= 2 && _many is null) panel.Children.Add(ShapeButtons(leaf));
        return panel;
    }

    private static bool IsNumberLike(HudArgument arg) =>
        arg.Kind is HudArgKind.Integer or HudArgKind.Float ||
        arg.Kind == HudArgKind.Identifier && (arg.Raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                              arg.Raw.Equals("false", StringComparison.OrdinalIgnoreCase));

    private FrameworkElement ArgEditor(HudBlock leaf, int index)
    {
        var arg = leaf.Arguments[index];
        var tip = PropertyArguments.NameOf(leaf.Name, index) ?? $"argument {index + 1}";
        var choices = arg.Kind == HudArgKind.String ? PropertyArguments.Choices(leaf.Name, index, _block?.Name) : null;
        if (choices is null && arg.Kind == HudArgKind.String && PropertyArguments.IsEvent(leaf.Name, index) && KnownEvents is not null)
            choices = KnownEvents();
        var box = new TextBox
        {
            Style = InlineBox,
            Text = arg.Raw,
            FontFamily = Mono,
            Width = choices is not null ? 118 : arg.Kind == HudArgKind.String ? 150 : 66,
            Margin = new Thickness(0, 1, 4, 1),
            ToolTip = arg.Kind is HudArgKind.Integer or HudArgKind.Float
                ? $"{tip}: {NumberSteps.Help.ToLowerInvariant()[0]}{NumberSteps.Help[1..]}; sums such as 128/4 are worked out"
                : tip,
            Tag = (leaf, index),
        };
        box.LostFocus += (_, _) => Commit(leaf, index, box.Text);
        box.PreviewKeyDown += (_, e) => Arg_KeyDown(box, leaf, index, e);
        box.PreviewMouseWheel += (_, e) => Arg_Wheel(box, leaf, index, e);
        _editors.Add((box, leaf, index));
        if (choices is null) return box;

        // The list opens from a button at the box's right end, and is made only then.
        box.Padding = new Thickness(4, 2, 18, 2);
        var open = ChoicesButton();
        open.Click += (_, _) => OpenList(box, choices, picked =>
        {
            box.Text = picked;
            Commit(leaf, index, picked);
        });
        // The pair has the width and the box fills it, so a line of one value stretches the box with the row.
        var grid = new Grid { Width = box.Width, Margin = box.Margin };
        box.Width = double.NaN;
        box.Margin = new Thickness(0);
        grid.Children.Add(box);
        grid.Children.Add(open);
        return grid;
    }

    /// <summary>Square, the texture's shape, and a lock that keeps the drawn shape as the width changes.</summary>
    private FrameworkElement ShapeButtons(HudBlock leaf)
    {
        Button ShapeButton(string glyph, string tip, Func<double?> aspect, string refusal)
        {
            var button = new Button
            {
                Style = ToolButton,
                Content = new TextBlock { Text = glyph, Style = IconStyle, FontSize = 12 },
                MinWidth = 24,
                MinHeight = 22,
                Padding = new Thickness(3),
                Margin = new Thickness(0, 1, 2, 1),
                ToolTip = tip,
            };
            button.Click += (_, _) =>
            {
                if (_block is null) return;
                if (aspect() is not { } a || HeightForAspect?.Invoke(_block, leaf, a) is not { } h)
                {
                    ShapeRefused?.Invoke(this, refusal);
                    return;
                }
                SetHeight(leaf, h);
            };
            return button;
        }

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
        bar.Children.Add(ShapeButton("\uE739", "Square: the height that draws it as tall as wide on the screen, whatever the layout stretches",
            () => 1.0, "Square needs the element drawn on the canvas, with a width in numbers."));
        bar.Children.Add(ShapeButton("\uEB9F", "The texture's shape: the height that draws its texture, over the part TexCoords show, unstretched",
            () => _block is null ? null : TextureAspect?.Invoke(_block),
            "The texture's shape needs the element drawn on the canvas and its texture found."));
        var lockButton = new ToggleButton
        {
            Content = new TextBlock { Text = _keepShape ? "\uE72E" : "\uE785", Style = IconStyle, FontSize = 12 },
            IsChecked = _keepShape,
            MinWidth = 24,
            MinHeight = 22,
            Padding = new Thickness(3),
            Margin = new Thickness(0, 1, 2, 1),
            ToolTip = "Keep the shape: a new width changes the height with it, so the drawn shape stays",
        };
        lockButton.Click += (_, _) =>
        {
            _keepShape = lockButton.IsChecked == true;
            ((TextBlock)lockButton.Content).Text = _keepShape ? "\uE72E" : "\uE785";
        };
        bar.Children.Add(lockButton);
        return bar;
    }

    /// <summary>A shape asked for that the panel cannot work out, with why.</summary>
    public event EventHandler<string>? ShapeRefused;

    /// <summary>The BitmapRect's height, in the kind the line had it where it can.</summary>
    private void SetHeight(HudBlock leaf, double height)
    {
        var current = leaf.Arguments[1];
        var whole = Math.Abs(height - Math.Round(height)) < 1e-6;
        var changed = current.Kind == HudArgKind.Integer && whole
            ? HudArgument.FromInt((long)Math.Round(height))
            : HudArgument.FromFloat(height);
        if (changed == current) return;
        leaf.Arguments[1] = changed;
        Edited(new PropertyEditedEventArgs(leaf, new[] { new ArgChange(1, current, changed) }, _block));
        RefreshValues();
    }

    /// <summary>The unit names a line's mode takes: Frame only for a Position.</summary>
    private static IReadOnlyList<string> UnitNames(UnitConversion.ModeLine unit) =>
        unit.TakesFrame ? EngineArgs.RelativeModeNames : EngineArgs.RelativeModeNames.Take(3).ToList();

    private static RelativeMode? UnitNamed(string text) =>
        Array.FindIndex(EngineArgs.RelativeModeNames, n => string.Equals(n, text.Trim(), StringComparison.OrdinalIgnoreCase)) is var i and >= 0
            ? (RelativeMode)i
            : null;

    /// <summary>
    /// A line written without its mode: the unit the game reads its numbers in, greyed; another one
    /// converts the numbers and writes the mode.
    /// </summary>
    private FrameworkElement ImpliedUnitEditor(HudBlock leaf, UnitConversion.ModeLine unit)
    {
        var implied = UnitConversion.ModeOf(leaf).ToString();
        var box = new TextBox
        {
            Style = InlineBox,
            Text = implied,
            FontFamily = Mono,
            FontStyle = FontStyles.Italic,
            Foreground = Res("TextMutedBrush"),
            Width = 118,
            Padding = new Thickness(4, 2, 18, 2),
            ToolTip = $"The unit the game reads these numbers in: none is written, so {implied}. Another converts them so nothing moves.",
        };
        void Take(string text)
        {
            if (_block is null || UnitNamed(text) is not { } to || string.Equals(text.Trim(), implied, StringComparison.OrdinalIgnoreCase)) return;
            UnitChangeRequested?.Invoke(this, new UnitChangeRequestedEventArgs(_block, leaf, to));
        }
        box.LostFocus += (_, _) => Take(box.Text);
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Take(box.Text);
            e.Handled = true;
        };
        var open = ChoicesButton();
        open.Click += (_, _) => OpenList(box, UnitNames(unit), Take);
        var grid = new Grid { Width = box.Width, Margin = new Thickness(0, 1, 4, 1) };
        grid.Children.Add(box);
        grid.Children.Add(open);
        return grid;
    }

    private Button ChoicesButton() => new()
    {
        Style = ToolButton,
        Content = new TextBlock { Text = "\uE70D", Style = IconStyle, FontSize = 8 },
        MinWidth = 16,
        MinHeight = 16,
        Width = 16,
        Height = 18,
        Padding = new Thickness(0),
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(0, 1, 5, 1),
        Focusable = false,
        ToolTip = "Choose from the list",
    };

    private Style? _inlineBox, _toolButton, _icon;
    private FontFamily? _mono;
    private Style InlineBox => _inlineBox ??= (Style)FindResource("InlineBox");
    private Style ToolButton => _toolButton ??= (Style)FindResource("ToolButton");
    private Style IconStyle => _icon ??= (Style)FindResource("Icon");
    private FontFamily Mono => _mono ??= (FontFamily)FindResource("MonoFont");

    /// <summary>A list of the choices under the box; a click, or Enter, takes one.</summary>
    private void OpenList(TextBox box, IReadOnlyList<string> choices, Action<string> take)
    {
        var list = new ListBox
        {
            ItemsSource = choices,
            MaxHeight = 260,
            MinWidth = Math.Max(160, box.ActualWidth),
            FontFamily = Mono,
            SelectedItem = choices.FirstOrDefault(c => string.Equals(c, box.Text, StringComparison.OrdinalIgnoreCase)),
        };
        var popup = new Popup
        {
            PlacementTarget = box,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = Res("ElevatedBrush"),
                BorderBrush = Res("BorderStrongBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2),
                Child = list,
            },
        };
        void Take()
        {
            if (list.SelectedItem is not string picked) return;
            popup.IsOpen = false;
            take(picked);
        }
        list.PreviewMouseLeftButtonUp += (_, _) => Take();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Take();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                e.Handled = true;
            }
        };
        popup.IsOpen = true;
        if (list.SelectedItem is not null) list.ScrollIntoView(list.SelectedItem);
        list.Focus();
    }

    private void Arg_KeyDown(TextBox box, HudBlock leaf, int index, KeyEventArgs e)
    {
        var current = leaf.Arguments[index];
        switch (e.Key)
        {
            case Key.Enter:
                Commit(leaf, index, box.Text);
                e.Handled = true;
                break;
            case Key.Escape:
                box.Text = current.Raw;
                e.Handled = true;
                break;
        }
        // With Alt held the key comes as a system key.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.Up or Key.Down) || current.Kind is not (HudArgKind.Integer or HudArgKind.Float)) return;
        Step(box, leaf, index, key == Key.Up ? 1 : -1, Keyboard.Modifiers);
        e.Handled = true;
    }

    /// <summary>The wheel steps a number being typed in; over any other box it scrolls the panel.</summary>
    private void Arg_Wheel(TextBox box, HudBlock leaf, int index, MouseWheelEventArgs e)
    {
        if (!TakesWheel(box) || leaf.Arguments[index].Kind is not (HudArgKind.Integer or HudArgKind.Float)) return;
        e.Handled = true;
        // A fine wheel or a touchpad turns less than a notch at a time: whole notches step.
        _wheel += e.Delta;
        var notches = _wheel / NumberSteps.Notch;
        if (notches == 0) return;
        _wheel -= notches * NumberSteps.Notch;
        Step(box, leaf, index, notches, Keyboard.Modifiers);
    }

    /// <summary>
    /// Move a number by <paramref name="count"/> steps (<see cref="NumberSteps"/>): one for a whole number or
    /// a count of pixels, a hundredth otherwise. A whole count of pixels taking a tenth becomes a fraction,
    /// as a position or a size may; any other whole number stays whole.
    /// </summary>
    internal void Step(TextBox box, HudBlock leaf, int index, int count, ModifierKeys keys)
    {
        var current = leaf.Arguments[index];
        if (current.Kind is not (HudArgKind.Integer or HudArgKind.Float) ||
            !double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return;
        var unit = IsUnitNumber(leaf, index);
        var pixels = unit && UnitConversion.Of(leaf.Name) is not null && UnitConversion.ModeOf(leaf) == RelativeMode.Pixels;
        var step = NumberSteps.For(current.Kind == HudArgKind.Integer || pixels, keys);
        if (current.Kind == HudArgKind.Integer && !unit) step = Math.Max(1, Math.Round(step));
        value = NumberSteps.Move(value, count, step);
        var text = current.Kind == HudArgKind.Integer && value == Math.Round(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : HudArgument.FromFloat(value).Raw;
        box.Text = text;
        _stepping = true;
        try { Commit(leaf, index, text); }
        finally { _stepping = false; }
        box.Text = leaf.Arguments[index].Raw;
        box.CaretIndex = box.Text.Length;
        if (keys.HasFlag(ModifierKeys.Alt)) _altStepped = true;
    }

    /// <summary>A 0/1 line as a switch, written as the line had it: 1/0, 1.000000/0.000000 or true/false.</summary>
    private FrameworkElement FlagEditor(HudBlock leaf)
    {
        var arg = leaf.Arguments[0];
        var on = arg.Kind == HudArgKind.Identifier ? arg.Raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            : double.TryParse(arg.Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v != 0;
        var check = new CheckBox
        {
            IsChecked = on,
            Content = on ? "On" : "Off",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2),
        };
        check.Click += (_, _) =>
        {
            var now = check.IsChecked == true;
            check.Content = now ? "On" : "Off";
            var raw = arg.Kind switch
            {
                HudArgKind.Identifier => now ? "true" : "false",
                HudArgKind.Float => HudArgument.FromFloat(now ? 1 : 0).Raw,
                _ => now ? "1" : "0",
            };
            Commit(leaf, 0, raw);
        };
        return check;
    }

    /// <summary>A swatch of the line's colour; a click opens a picker that previews as it goes and commits once.</summary>
    private FrameworkElement ColorSwatch(HudBlock leaf, int start)
    {
        var swatch = new Button
        {
            Width = 26,
            Height = 22,
            Margin = new Thickness(0, 1, 6, 1),
            Padding = new Thickness(0),
            ToolTip = "Pick the colour",
            BorderBrush = Res("BorderStrongBrush"),
            Background = new SolidColorBrush(ColorOf(leaf, start)),
        };
        swatch.Click += (_, _) =>
        {
            var before = new[] { leaf.Arguments[start], leaf.Arguments[start + 1], leaf.Arguments[start + 2] };
            var picker = new ColorPicker();
            var popup = new Popup
            {
                PlacementTarget = swatch,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = new Border
                {
                    Background = Res("ElevatedBrush"),
                    BorderBrush = Res("BorderStrongBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 4, 0, 0),
                    Child = picker,
                },
            };
            picker.Color = ColorOf(leaf, start);
            picker.ColorChanged += (_, c) =>
            {
                SetColor(leaf, start, c);
                swatch.Background = new SolidColorBrush(c);
                Preview?.Invoke();
            };
            popup.Closed += (_, _) =>
            {
                var changes = Enumerable.Range(0, 3)
                    .Where(i => leaf.Arguments[start + i] != before[i])
                    .Select(i => new ArgChange(start + i, before[i], leaf.Arguments[start + i]))
                    .ToList();
                if (changes.Count > 0) Edited(new PropertyEditedEventArgs(leaf, changes, _block));
                Rebuild();
            };
            popup.IsOpen = true;
        };
        return swatch;
    }

    private static Color ColorOf(HudBlock leaf, int start)
    {
        byte Channel(int i) => double.TryParse(leaf.Arguments[start + i].Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? (byte)Math.Clamp(Math.Round(v), 0, 255)
            : (byte)255;
        return Color.FromRgb(Channel(0), Channel(1), Channel(2));
    }

    /// <summary>The colour's channels as whole numbers, in the kind the line had them.</summary>
    private static void SetColor(HudBlock leaf, int start, Color c)
    {
        var channels = new[] { c.R, c.G, c.B };
        for (var i = 0; i < 3; i++)
        {
            var old = leaf.Arguments[start + i];
            leaf.Arguments[start + i] = old.Kind == HudArgKind.Float
                ? HudArgument.FromFloat(channels[i])
                : HudArgument.FromInt(channels[i]);
        }
    }

    /// <summary>
    /// Change one argument, if the text is one its kind takes, and tell the host. A unit chosen for a line's
    /// mode converts its numbers instead; a sum is worked out, and a number in pixels ("960px") is put in
    /// the line's own unit.
    /// </summary>
    private void Commit(HudBlock leaf, int index, string text)
    {
        var current = leaf.Arguments[index];
        if (text == current.Raw) return;
        if (_block is not null && UnitConversion.Of(leaf.Name) is { } unit && index == unit.Mode && UnitChangeRequested is not null &&
            UnitNamed(text) is { } to && UnitConversion.CanConvert(leaf, to))
        {
            UnitChangeRequested.Invoke(this, new UnitChangeRequestedEventArgs(_block, leaf, to));
            return;
        }
        var kind = current.Kind;
        if (kind is HudArgKind.Integer or HudArgKind.Float && !IsValidForKind(text, kind))
        {
            if (WorkOut(leaf, index, text) is not { } worked) return;
            // A number of a position or a size may take a fraction; any other whole number is rounded.
            var fractional = worked != Math.Round(worked);
            if (kind == HudArgKind.Integer && fractional && IsUnitNumber(leaf, index)) kind = HudArgKind.Float;
            text = kind == HudArgKind.Integer
                ? ((long)Math.Round(worked)).ToString(CultureInfo.InvariantCulture)
                : HudArgument.FromFloat(worked).Raw;
            if (text == current.Raw && kind == current.Kind) return;
        }
        if (!IsValidForKind(text, kind)) return;
        var changed = new HudArgument(kind, text);
        // With the shape kept, a BitmapRect's new width brings its height along, in the same undo step.
        var shape = _keepShape && leaf.Name == "BitmapRect" && index == 0 && _block is not null
            ? DrawnAspect?.Invoke(_block, leaf)
            : null;
        leaf.Arguments[index] = changed;
        var changes = new List<ArgChange> { new(index, current, changed) };
        if (shape is { } aspect && HeightForAspect?.Invoke(_block!, leaf, aspect) is { } h)
        {
            var oldHeight = leaf.Arguments[1];
            var newHeight = oldHeight.Kind == HudArgKind.Integer && Math.Abs(h - Math.Round(h)) < 1e-6
                ? HudArgument.FromInt((long)Math.Round(h))
                : HudArgument.FromFloat(h);
            if (newHeight != oldHeight)
            {
                leaf.Arguments[1] = newHeight;
                changes.Add(new ArgChange(1, oldHeight, newHeight));
            }
        }
        Edited(new PropertyEditedEventArgs(leaf, changes, _block, _stepping));
        if (changes.Count > 1) RefreshValues();
    }

    private static bool IsUnitNumber(HudBlock leaf, int index) =>
        UnitConversion.Of(leaf.Name) is { } unit && (index == unit.X || index == unit.Y) || leaf.Name == "TextBox" && index < 2;

    /// <summary>A sum, or one in pixels ("(1920 - 64) / 2 px") in the line's own unit; null when it is neither.</summary>
    private double? WorkOut(HudBlock leaf, int index, string text)
    {
        var t = text.Trim();
        var pixels = t.EndsWith("px", StringComparison.OrdinalIgnoreCase);
        if (pixels) t = t[..^2];
        if (!NumberExpression.TryEvaluate(t, out var value)) return null;
        if (!pixels) return value;
        return _block is not null && BasisOf?.Invoke(_block) is { } basis &&
               UnitConversion.FromPixels(leaf, index, (float)value, basis) is { } converted
            ? Math.Round(converted, 6)
            : null;
    }

    private static bool IsValidForKind(string text, HudArgKind kind) => kind switch
    {
        HudArgKind.Integer => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        HudArgKind.Float => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        HudArgKind.String => !text.Contains('"') && !text.Contains('\n'),
        HudArgKind.Identifier => !string.IsNullOrWhiteSpace(text) && (char.IsLetter(text[0]) || text[0] == '_'),
        _ => true,
    };

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HudBlock leaf } && _block is not null)
            PropertyDeleteRequested?.Invoke(this, new PropertyDeleteRequestedEventArgs(_block, leaf));
    }

    // ── The Add tab ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fill the "Add" tab: what the game reads for this block and it does not have yet, under
    /// headings, narrowed by the filter box. Each button fires <see cref="PropertyInsertRequested"/>.
    /// </summary>
    private void RebuildAddTab()
    {
        if (_block is null) return;

        var filter = AddFilter.Text.Trim();
        var addable = PropertyCatalog.GetAddable(_block, ParentOf?.Invoke(_block))
            .Where(d => filter.Length == 0 || d.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        d.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (addable.Count == 0)
        {
            AddRows.Children.Add(Hint(filter.Length > 0
                ? $"Nothing {_block.Name} can take matches \"{filter}\"."
                : $"{_block.Name} already has everything the game reads for it, or the game reads nothing more here."));
            return;
        }

        string? group = null;
        foreach (var def in addable)
        {
            if (def.Group != group)
            {
                group = def.Group;
                AddRows.Children.Add(new TextBlock
                {
                    Text = group.ToUpperInvariant(),
                    Style = (Style)FindResource("Caption"),
                    Margin = new Thickness(2, AddRows.Children.Count == 0 ? 6 : 14, 0, 6),
                });
            }
            AddRows.Children.Add(BuildAddButton(def));
        }
    }

    private void AddFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_block is null) return;
        AddRows.Children.Clear();
        RebuildAddTab();
    }

    /// <summary>One button in the "Add" tab: the name on top, what it does below, click to add.</summary>
    private FrameworkElement BuildAddButton(PropertyCatalog.PropertyDef def)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = def.IsBlock ? def.Name + " { }" : def.Name,
            Foreground = Res("CodeBrush"),
            FontFamily = (FontFamily)FindResource("MonoFont"),
        });
        panel.Children.Add(new TextBlock
        {
            Text = def.Description,
            Foreground = Res("TextMutedBrush"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });
        var button = new Button
        {
            Content = panel,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(10, 6, 10, 7),
            Cursor = Cursors.Hand,
            ToolTip = def.IsBlock ? $"Add a {def.Name} block to the selected element." : $"Add {def.Name}(...) to the selected element.",
            Tag = def,
        };
        button.Click += AddButton_Click;
        return button;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PropertyCatalog.PropertyDef def } || _block is null) return;
        // Build a fresh instance every click: repeated Add-then-Undo-then-Add must not reuse one block.
        var newProp = def.Factory();
        // Another row of a table, such as NumberColor, starts as a copy of the table's last row.
        var last = def.Repeatable && !def.IsBlock
            ? _block.Children.LastOrDefault(c => !c.HasBody && c.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase))
            : null;
        if (last is not null) newProp = new HudBlock(last.Name, last.Arguments.ToArray());
        PropertyInsertRequested?.Invoke(this, new PropertyInsertRequestedEventArgs(_block, newProp, CopiedLast: last is not null));
    }

    private TextBlock Hint(string text) => new()
    {
        Text = text,
        Foreground = Res("TextMutedBrush"),
        FontStyle = FontStyles.Italic,
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 10, 4, 4),
    };
}
