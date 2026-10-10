using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Views;

public sealed record PropertyEditedEventArgs(HudBlock Leaf, int ArgIndex, HudArgument OldArg, HudArgument NewArg, HudBlock? SelectedBlock);

/// <summary>Fired when the user clicks an "Add" tab button to insert a property as a child of
/// <see cref="Target"/>. Carries the already-built leaf <see cref="Property"/> so the host can
/// wrap the insert in an undo edit and add it at the correct position. <see cref="CopiedLast"/>: a
/// table row copied from the block's last row of that name.</summary>
public sealed record PropertyInsertRequestedEventArgs(HudBlock Target, HudBlock Property, bool CopiedLast = false);

/// <summary>Fired by a row's delete button: remove <see cref="Property"/> from <see cref="Target"/>.</summary>
public sealed record PropertyDeleteRequestedEventArgs(HudBlock Target, HudBlock Property);

/// <summary>Fired by the name box in the header: rename <see cref="Block"/> to <see cref="NewName"/>.</summary>
public sealed record RenameRequestedEventArgs(HudBlock Block, string NewName);

public partial class PropertyPanel : UserControl
{
    private static readonly FontFamily MonoFont = new("Consolas, Cascadia Mono, Menlo, monospace");
    private static readonly Brush NameBrush = new SolidColorBrush(Color.FromRgb(180, 200, 240));
    private static readonly Brush DefaultLineBrush = new SolidColorBrush(Color.FromRgb(120, 128, 140));
    private HudBlock? _block;
    private readonly List<(HudBlock Leaf, TextBlock Label)> _nameLabels = new();

    /// <summary>Whether a line only restates the engine's default, so saving leaves it out. Set by the host.</summary>
    public Func<HudBlock, bool>? IsDefaultLine { get; set; }

    /// <summary>The block holding a block, which tells what a template reads. Set by the host.</summary>
    public Func<HudBlock, HudBlock?>? ParentOf { get; set; }

    public event EventHandler<PropertyEditedEventArgs>? PropertyEdited;
    public event EventHandler<PropertyInsertRequestedEventArgs>? PropertyInsertRequested;
    public event EventHandler<PropertyDeleteRequestedEventArgs>? PropertyDeleteRequested;
    public event EventHandler<RenameRequestedEventArgs>? RenameRequested;

    public PropertyPanel()
    {
        InitializeComponent();
    }

    public void SetBlock(HudBlock? block)
    {
        // A name typed and not yet committed belongs to the block shown so far: selecting another
        // element in the tree switches the panel before the box loses focus.
        if (NameBox.IsKeyboardFocusWithin && !ReferenceEquals(block, _block)) CommitName();
        _block = block;
        Rebuild();
    }

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

    /// <summary>Mark again the rows whose line restates a default, after an edit.</summary>
    public void RefreshDefaultMarks()
    {
        foreach (var (leaf, label) in _nameLabels) MarkDefault(leaf, label);
    }

    private void MarkDefault(HudBlock leaf, TextBlock label)
    {
        var isDefault = IsDefaultLine?.Invoke(leaf) == true;
        label.Foreground = isDefault ? DefaultLineBrush : NameBrush;
        label.FontStyle = isDefault ? FontStyles.Italic : FontStyles.Normal;
        label.ToolTip = isDefault ? $"{leaf.Name} here is the engine's default: saving leaves this line out." : leaf.Name;
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
            HeaderText.Text = "(no selection)";
            AddRows.Children.Add(HintText("Select an element in the tree or canvas to see its properties."));
            return;
        }

        RefreshHeader();

        RebuildEditTab();
        RebuildAddTab();
    }

    private void RebuildEditTab()
    {
        if (_block is null) return;

        var leaves = _block.Children.Where(c => !c.HasBody).ToList();
        if (leaves.Count == 0)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = "(no editable properties on this element)",
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(4, 8, 4, 4),
            });
        }
        else
        {
            foreach (var leaf in leaves)
                Rows.Children.Add(BuildRow(leaf));
        }

        var nested = _block.Children.Count(c => c.HasBody);
        if (nested > 0)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = $"+ {nested} nested block{(nested == 1 ? "" : "s")} (see the Elements tree)",
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(4, 12, 4, 0),
                FontSize = 11,
            });
        }
    }

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
            AddRows.Children.Add(HintText(filter.Length > 0
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
                    Foreground = new SolidColorBrush(Color.FromRgb(143, 184, 222)),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(2, AddRows.Children.Count == 0 ? 2 : 10, 0, 4),
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

    /// <summary>One button in the "Add" tab: bold name on top, gray description below, click to add.</summary>
    private FrameworkElement BuildAddButton(PropertyCatalog.PropertyDef def)
    {
        var title = new TextBlock
        {
            Text = def.IsBlock ? def.Name + " { }" : def.Name,
            Foreground = new SolidColorBrush(Color.FromRgb(212, 232, 255)),
            FontFamily = MonoFont,
            FontWeight = FontWeights.SemiBold,
        };
        var desc = new TextBlock
        {
            Text = def.Description,
            Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(desc);

        var btn = new Button
        {
            Content = panel,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(44, 52, 64)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = def.IsBlock ? $"Add a {def.Name} block to the selected element." : $"Add {def.Name}(...) to the selected element.",
            Tag = def,
        };
        btn.Click += AddButton_Click;
        return btn;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PropertyCatalog.PropertyDef def) return;
        if (_block is null) return;

        // Build a fresh instance every click — the factory returns a new HudBlock so repeated
        // Add-then-Undo-then-Add cycles don't reuse the same object (which would confuse undo).
        var newProp = def.Factory();
        // Another row of a table, such as NumberColor, starts as a copy of the table's last row.
        var last = def.Repeatable && !def.IsBlock
            ? _block.Children.LastOrDefault(c => !c.HasBody && c.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase))
            : null;
        if (last is not null) newProp = new HudBlock(last.Name, last.Arguments.ToArray());
        PropertyInsertRequested?.Invoke(this, new PropertyInsertRequestedEventArgs(_block, newProp, CopiedLast: last is not null));
    }

    private static TextBlock HintText(string text) => new()
    {
        Text = text,
        Foreground = Brushes.Gray,
        FontStyle = FontStyles.Italic,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 8, 4, 4),
    };

    private FrameworkElement BuildRow(HudBlock leaf)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        var delete = new Button
        {
            Content = "×",
            Width = 18,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(44, 52, 64)),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 120, 120)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64)),
            ToolTip = $"Remove {leaf.Name} from this element",
            Tag = leaf,
        };
        delete.Click += Delete_Click;
        Grid.SetColumn(delete, 0);
        row.Children.Add(delete);

        var nameLabel = new TextBlock
        {
            Text = leaf.Name,
            FontFamily = MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 4, 4, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        MarkDefault(leaf, nameLabel);
        _nameLabels.Add((leaf, nameLabel));
        Grid.SetColumn(nameLabel, 1);
        row.Children.Add(nameLabel);

        var argPanel = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < leaf.Arguments.Count; i++)
        {
            var index = i;
            var arg = leaf.Arguments[index];
            var box = new TextBox
            {
                Text = arg.Raw,
                FontFamily = MonoFont,
                Width = arg.Kind == HudArgKind.String ? 140 : 70,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(3, 2, 3, 2),
                Background = ColorForKind(arg.Kind),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                Tag = (leaf, index),
            };
            box.LostFocus += Arg_LostFocus;
            box.KeyDown += Arg_KeyDown;
            argPanel.Children.Add(box);
        }
        Grid.SetColumn(argPanel, 2);
        row.Children.Add(argPanel);

        return row;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HudBlock leaf } && _block is not null)
            PropertyDeleteRequested?.Invoke(this, new PropertyDeleteRequestedEventArgs(_block, leaf));
    }

    private void Arg_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            CommitEdit(box);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && sender is TextBox b2 && b2.Tag is ValueTuple<HudBlock, int> t)
        {
            b2.Text = t.Item1.Arguments[t.Item2].Raw;
            e.Handled = true;
        }
    }

    private void Arg_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitEdit(box);
    }

    private void CommitEdit(TextBox box)
    {
        if (box.Tag is not ValueTuple<HudBlock, int> tag) return;
        var (leaf, index) = tag;
        var current = leaf.Arguments[index];
        if (box.Text == current.Raw) return;

        if (!IsValidForKind(box.Text, current.Kind))
        {
            box.Text = current.Raw;
            return;
        }

        var newArg = current with { Raw = box.Text };
        leaf.Arguments[index] = newArg;
        PropertyEdited?.Invoke(this, new PropertyEditedEventArgs(leaf, index, current, newArg, _block));
    }

    private static bool IsValidForKind(string text, HudArgKind kind) => kind switch
    {
        HudArgKind.Integer => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        HudArgKind.Float => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        HudArgKind.String => true,
        HudArgKind.Identifier => !string.IsNullOrWhiteSpace(text) && (char.IsLetter(text[0]) || text[0] == '_'),
        _ => true,
    };

    private static Brush ColorForKind(HudArgKind kind) => kind switch
    {
        HudArgKind.String => new SolidColorBrush(Color.FromRgb(40, 40, 50)),
        HudArgKind.Integer => new SolidColorBrush(Color.FromRgb(40, 50, 40)),
        HudArgKind.Float => new SolidColorBrush(Color.FromRgb(50, 45, 40)),
        HudArgKind.Identifier => new SolidColorBrush(Color.FromRgb(50, 40, 50)),
        _ => Brushes.Black,
    };
}
