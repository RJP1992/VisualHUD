using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Views;

/// <summary>One argument of a line, changed from <see cref="Old"/> to <see cref="New"/>.</summary>
public sealed record ArgChange(int Index, HudArgument Old, HudArgument New);

/// <summary>
/// Fired when arguments of a line were edited in the panel (they are already changed): the host makes
/// one undoable edit of them. A colour picked is its three arguments at once.
/// </summary>
public sealed record PropertyEditedEventArgs(HudBlock Leaf, IReadOnlyList<ArgChange> Changes, HudBlock? SelectedBlock);

/// <summary>Fired when the user clicks an "Add" tab button to insert a property as a child of
/// <see cref="Target"/>. Carries the already-built leaf <see cref="Property"/> so the host can
/// wrap the insert in an undo edit and add it at the correct position. <see cref="CopiedLast"/>: a
/// table row copied from the block's last row of that name.</summary>
public sealed record PropertyInsertRequestedEventArgs(HudBlock Target, HudBlock Property, bool CopiedLast = false);

/// <summary>Fired by a row's delete button: remove <see cref="Property"/> from <see cref="Target"/>.</summary>
public sealed record PropertyDeleteRequestedEventArgs(HudBlock Target, HudBlock Property);

/// <summary>Fired by the name box in the header: rename <see cref="Block"/> to <see cref="NewName"/>.</summary>
public sealed record RenameRequestedEventArgs(HudBlock Block, string NewName);

/// <summary>
/// The selected block's lines, grouped as the Add tab groups what the game reads, each argument with
/// an editor for what it takes: a name from a list, a flag, a colour, an event, a number or text.
/// </summary>
public partial class PropertyPanel : UserControl
{
    private HudBlock? _block;
    private readonly List<(HudBlock Leaf, TextBlock Label)> _nameLabels = new();
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

    public PropertyPanel()
    {
        InitializeComponent();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    public void SetBlock(HudBlock? block)
    {
        // A name typed and not yet committed belongs to the block shown so far: selecting another
        // element in the tree switches the panel before the box loses focus.
        if (NameBox.IsKeyboardFocusWithin && !ReferenceEquals(block, _block)) CommitName();
        _block = block;
        Rebuild();
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
        RebuildEditTab();
        RebuildAddTab();
    }

    // ── The lines ────────────────────────────────────────────────────────────────────

    private void RebuildEditTab()
    {
        if (_block is null) return;
        var parent = ParentOf?.Invoke(_block);
        var leaves = _block.Children.Where(c => !c.HasBody).ToList();
        if (leaves.Count == 0) Rows.Children.Add(Hint("No lines yet: add what this block should read from the Add tab."));

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

        var nested = _block.Children.Count(c => c.HasBody);
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
        if (choices is not null)
        {
            var combo = new ComboBox
            {
                IsEditable = true,
                ItemsSource = choices,
                Text = arg.Raw,
                Width = 118,
                Margin = new Thickness(0, 1, 4, 1),
                ToolTip = tip,
                Tag = (leaf, index),
                FontFamily = (FontFamily)FindResource("MonoFont"),
                IsTextSearchEnabled = true,
                StaysOpenOnEdit = true,
            };
            combo.SelectionChanged += (_, e) =>
            {
                if (e.AddedItems.Count > 0 && e.AddedItems[0] is string picked) Commit(leaf, index, picked);
            };
            combo.LostKeyboardFocus += (_, _) =>
            {
                if (!combo.IsKeyboardFocusWithin) Commit(leaf, index, combo.Text);
            };
            combo.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit(leaf, index, combo.Text);
                    e.Handled = true;
                }
            };
            return combo;
        }

        var box = new TextBox
        {
            Text = arg.Raw,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            Width = arg.Kind == HudArgKind.String ? 150 : 66,
            Margin = new Thickness(0, 1, 4, 1),
            ToolTip = arg.Kind is HudArgKind.Integer or HudArgKind.Float ? $"{tip}: Up and Down change it (Shift by more, Ctrl by less)" : tip,
            Tag = (leaf, index),
        };
        box.LostFocus += (_, _) => Commit(leaf, index, box.Text);
        box.PreviewKeyDown += (_, e) => Arg_KeyDown(box, leaf, index, e);
        return box;
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
            case Key.Up or Key.Down when current.Kind is HudArgKind.Integer or HudArgKind.Float &&
                                         double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value):
                var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                var step = current.Kind == HudArgKind.Integer ? (shift ? 10 : 1) : shift ? 0.1 : ctrl ? 0.001 : 0.01;
                value += e.Key == Key.Up ? step : -step;
                box.Text = current.Kind == HudArgKind.Integer
                    ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
                    : HudArgument.FromFloat(value).Raw;
                Commit(leaf, index, box.Text);
                box.CaretIndex = box.Text.Length;
                e.Handled = true;
                break;
        }
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
                if (changes.Count > 0) PropertyEdited?.Invoke(this, new PropertyEditedEventArgs(leaf, changes, _block));
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

    /// <summary>Change one argument, if the text is one its kind takes, and tell the host.</summary>
    private void Commit(HudBlock leaf, int index, string text)
    {
        var current = leaf.Arguments[index];
        if (text == current.Raw) return;
        if (!IsValidForKind(text, current.Kind)) return;
        var changed = current with { Raw = text };
        leaf.Arguments[index] = changed;
        PropertyEdited?.Invoke(this, new PropertyEditedEventArgs(leaf, new[] { new ArgChange(index, current, changed) }, _block));
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
