using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App;

/// <summary>Lining the selection up as it is drawn on the screen: align, space out, the same size.</summary>
public partial class MainWindow
{
    public static readonly RoutedCommand ArrangeCommand = new("Arrange", typeof(MainWindow));

    /// <summary>How the selection is lined up; the menus pass these names.</summary>
    internal enum Arrangement { Left, Centre, Right, Top, Middle, Bottom, Across, Down, Width, Height, Size }

    private void SetUpArrange()
    {
        CommandBindings.Add(new CommandBinding(ArrangeCommand, Arrange_Executed, Arrange_CanExecute));
        BuildArrangeMenu(EditArrange.Items);
        BuildArrangeMenu(TreeArrange.Items);
        var canvasMenu = new ContextMenu();
        BuildStructureMenu(canvasMenu.Items);
        canvasMenu.Items.Add(new MenuItem { Header = "Re_peat...", Command = RepeatCommand, InputGestureText = "Ctrl+Shift+D" });
        canvasMenu.Items.Add(new Separator());
        BuildArrangeMenu(canvasMenu.Items);
        Canvas.ContextMenu = canvasMenu;
        BuildStructureMenu(EditStructure.Items);
        BuildStructureMenu(TreeStructure.Items);
    }

    /// <summary>Group, ungroup and fit a group's Rect: nothing moves on the screen.</summary>
    private static void BuildStructureMenu(ItemCollection items)
    {
        items.Add(new MenuItem
        {
            Header = "_Group", Command = GroupCommand, InputGestureText = "Ctrl+G",
            ToolTip = "Put the selected elements in a new Group, its Rect fitted to them; nothing moves.",
        });
        items.Add(new MenuItem
        {
            Header = "_Ungroup", Command = UngroupCommand, InputGestureText = "Ctrl+Shift+G",
            ToolTip = "Take the selected Group's elements out into its place, its Position and Scale going into theirs; nothing moves.",
        });
        items.Add(new MenuItem
        {
            Header = "_Fit Rect to contents", Command = FitRectCommand,
            ToolTip = "Make the selected Group's Rect the box round what it holds, its Position moved so nothing moves: its frame, the pivot of its alignment and the outline it is picked by.",
        });
    }

    private static void BuildArrangeMenu(ItemCollection items)
    {
        void Add(string header, Arrangement how, string tip) => items.Add(new MenuItem
        {
            Header = header,
            Command = ArrangeCommand,
            CommandParameter = how.ToString(),
            ToolTip = tip,
        });
        const string edges = "With one element, the screen's; with several, the box round them all.";
        Add("Align _left", Arrangement.Left, $"Left edges in line. {edges}");
        Add("Align _centres", Arrangement.Centre, $"Centres in line, one above another. {edges}");
        Add("Align _right", Arrangement.Right, $"Right edges in line. {edges}");
        items.Add(new Separator());
        Add("Align _top", Arrangement.Top, $"Top edges in line. {edges}");
        Add("Align _middles", Arrangement.Middle, $"Middles in line, side by side. {edges}");
        Add("Align _bottom", Arrangement.Bottom, $"Bottom edges in line. {edges}");
        items.Add(new Separator());
        Add("Space out _across", Arrangement.Across, "The same gap between each and the next, left to right; the outermost two stay. Three or more.");
        Add("Space out _down", Arrangement.Down, "The same gap between each and the next, top to bottom; the outermost two stay. Three or more.");
        items.Add(new Separator());
        const string key = "the one marked with the cross, picked last";
        Add("Match _width", Arrangement.Width, $"The drawn width of {key}, for each BitmapRect or Rect.");
        Add("Match _height", Arrangement.Height, $"The drawn height of {key}, for each BitmapRect or Rect.");
        Add("Match _size", Arrangement.Size, $"The drawn width and height of {key}, for each BitmapRect or Rect.");
    }

    private static int MinimumFor(Arrangement how) => how switch
    {
        Arrangement.Across or Arrangement.Down => 3,
        Arrangement.Width or Arrangement.Height or Arrangement.Size => 2,
        _ => 1,
    };

    private void Arrange_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = e.Parameter is string name && Enum.TryParse<Arrangement>(name, out var how) &&
                       SelectedItems.OfType<HudBlock>().Count() >= MinimumFor(how);

    private void Arrange_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string name && Enum.TryParse<Arrangement>(name, out var how)) Arrange(how);
    }

    private static string Describe(Arrangement how) => how switch
    {
        Arrangement.Left => "align left",
        Arrangement.Centre => "align centres",
        Arrangement.Right => "align right",
        Arrangement.Top => "align top",
        Arrangement.Middle => "align middles",
        Arrangement.Bottom => "align bottom",
        Arrangement.Across => "space out across",
        Arrangement.Down => "space out down",
        Arrangement.Width => "match width",
        Arrangement.Height => "match height",
        _ => "match size",
    };

    /// <summary>Line the selection up, in one undo step: how many numbers changed.</summary>
    internal int Arrange(Arrangement how)
    {
        var (changes, left) = how switch
        {
            <= Arrangement.Bottom => AlignChanges(how),
            Arrangement.Across or Arrangement.Down => SpaceChanges(how == Arrangement.Across),
            _ => SizeChanges(how != Arrangement.Height, how != Arrangement.Width),
        };
        if (changes.Count == 0)
        {
            StatusText.Text = left ?? $"Nothing to {Describe(how)}: the selection is lined up already.";
            return 0;
        }
        var shown = Tree.SelectedItem as HudBlock;
        var edit = new ArgumentsEdit(changes, Describe(how), shown);
        edit.Apply();
        // A Position, BitmapRect or Rect it wrote in pixels is written in Viewport now, in the same undo step.
        var units = Canvas.ScaleUnits(Views.HudCanvas.Changed(how <= Arrangement.Down
            ? Canvas.MovedLines(SelectedItems.OfType<HudBlock>())
            : SizedLines(), changes));
        _undo.Push(units.Count == 0 ? edit : new CompositeEdit(units.Prepend(edit), Describe(how)));
        Canvas.Refresh();
        if (shown is not null && (!Properties.ShowsBlock(shown) || !Properties.RefreshValues())) Properties.SetBlock(shown);
        var lines = changes.Select(c => c.Line).Distinct().Count();
        StatusText.Text = $"{char.ToUpperInvariant(Describe(how)[0])}{Describe(how)[1..]}: {lines} line{(lines == 1 ? "" : "s")} changed, in one undo step." +
                          (left is null ? string.Empty : $" {left}");
        return changes.Count;
    }

    /// <summary>The selected elements that move on their own (not inside another selected one), where they are drawn.</summary>
    private List<(HudBlock Block, Rect Bounds)> Placed()
    {
        var blocks = SelectedItems.OfType<HudBlock>().ToList();
        var placed = new List<(HudBlock, Rect)>();
        foreach (var block in blocks)
        {
            var path = Locate(block)?.Path;
            if (path is not null && blocks.Any(other => !ReferenceEquals(other, block) && path.Contains(other))) continue;
            if (Canvas.ScreenBoundsOf(block) is { } bounds) placed.Add((block, bounds));
        }
        return placed;
    }

    /// <summary>The changes that move one element by (dx, dy) on the screen, if it has a Position line.</summary>
    private List<ArgumentChange>? Moved(HudBlock block, double dx, double dy)
    {
        if (Math.Abs(dx) < 1e-4 && Math.Abs(dy) < 1e-4) return new List<ArgumentChange>();
        var changes = Canvas.MoveChanges(new[] { block }, dx, dy);
        return changes.Count == 0 ? null : changes.Where(c => c.New != c.Old).ToList();
    }

    private static string? Stuck(int count) => count == 0 ? null
        : $"{count} element{(count == 1 ? " has" : "s have")} no Position line to move (add one from the Add tab).";

    private (List<ArgumentChange>, string?) AlignChanges(Arrangement how)
    {
        var placed = Placed();
        if (placed.Count == 0) return (new(), "Select elements drawn on the canvas to line them up.");
        var screen = Canvas.ScreenSize;
        var area = Rect.Empty;
        if (placed.Count == 1) area = new Rect(0, 0, screen.Width, screen.Height);
        else foreach (var p in placed) area.Union(p.Bounds);

        var changes = new List<ArgumentChange>();
        var stuck = 0;
        foreach (var (block, b) in placed)
        {
            var (dx, dy) = how switch
            {
                Arrangement.Left => (area.Left - b.Left, 0.0),
                Arrangement.Centre => (area.Left + area.Width / 2 - (b.Left + b.Width / 2), 0.0),
                Arrangement.Right => (area.Right - b.Right, 0.0),
                Arrangement.Top => (0.0, area.Top - b.Top),
                Arrangement.Middle => (0.0, area.Top + area.Height / 2 - (b.Top + b.Height / 2)),
                _ => (0.0, area.Bottom - b.Bottom),
            };
            if (Moved(block, dx, dy) is { } moved) changes.AddRange(moved);
            else stuck++;
        }
        return (changes, Stuck(stuck));
    }

    private (List<ArgumentChange>, string?) SpaceChanges(bool across)
    {
        var placed = Placed();
        if (placed.Count < 3) return (new(), "Select three or more elements drawn on the canvas to space them out.");
        double Start(Rect r) => across ? r.Left : r.Top;
        double Length(Rect r) => across ? r.Width : r.Height;
        var line = placed.OrderBy(p => Start(p.Bounds) + Length(p.Bounds) / 2).ToList();
        var first = line[0].Bounds;
        var last = line[^1].Bounds;
        var gap = (Start(last) + Length(last) - Start(first) - line.Sum(p => Length(p.Bounds))) / (line.Count - 1);

        var changes = new List<ArgumentChange>();
        var stuck = 0;
        var at = Start(first) + Length(first) + gap;
        foreach (var (block, b) in line.Skip(1).Take(line.Count - 2))
        {
            var shift = at - Start(b);
            if (Moved(block, across ? shift : 0, across ? 0 : shift) is { } moved) changes.AddRange(moved);
            else stuck++;
            at += Length(b) + gap;
        }
        return (changes, Stuck(stuck));
    }

    /// <summary>The lines a match of sizes writes: each selected element's but the one whose size they take.</summary>
    private List<(HudBlock Line, LineRole Role, LaidOutElement Element)> SizedLines()
    {
        var lines = new List<(HudBlock, LineRole, LaidOutElement)>();
        foreach (var block in SelectedItems.OfType<HudBlock>().Where(b => !ReferenceEquals(b, Tree.SelectedItem)))
            if (Canvas.LaidOut(block) is { } e && SizeLine(block, e) is { } line)
                lines.Add((line, LineRole.Own, e));
        return lines;
    }

    /// <summary>The line that sizes an element: a bitmap's last BitmapRect, a group's last Rect.</summary>
    private HudBlock? SizeLine(HudBlock block, LaidOutElement e)
    {
        var name = ElementKinds.IsBitmapBased(e.Kind) ? "BitmapRect" : ElementKinds.IsGroupBased(e.Kind) ? "Rect" : null;
        return name is null ? null
            : block.Children.LastOrDefault(c => !c.HasBody && c.Name == name && c.Arguments.Count >= 2 &&
                                                EngineArgs.IsNumber(c.Arguments[0]) && EngineArgs.IsNumber(c.Arguments[1]));
    }

    /// <summary>An element's size as drawn, along its own axes, in screen pixels: its full quad, or its Rect.</summary>
    private (double Width, double Height)? DrawnSize(HudBlock block)
    {
        if (Canvas.LaidOut(block) is not { } e || DrawnScale(block) is not { } scale || SizeLine(block, e) is null) return null;
        if (ElementKinds.IsBitmapBased(e.Kind) && (e.FullQuad ?? e.Quad) is { } q)
            return (q.Width * scale.Across, q.Height * scale.Down);
        if (ElementKinds.IsGroupBased(e.Kind)) return (e.RectWidthPixels * scale.Across, e.RectHeightPixels * scale.Down);
        return null;
    }

    private (List<ArgumentChange>, string?) SizeChanges(bool width, bool height)
    {
        const string needsKey = "Pick the element whose size the others take last, so it has the cross: it needs a BitmapRect or a Rect.";
        if (Tree.SelectedItem is not HudBlock key || DrawnSize(key) is not { } size) return (new(), needsKey);
        var changes = new List<ArgumentChange>();
        var skipped = 0;
        foreach (var block in SelectedItems.OfType<HudBlock>().Where(b => !ReferenceEquals(b, key)))
        {
            if (Canvas.LaidOut(block) is not { } e || SizeLine(block, e) is not { } line ||
                DrawnScale(block) is not { } scale || BasisFor(block) is not { } basis)
            {
                skipped++;
                continue;
            }
            var mode = UnitConversion.ModeOf(line);
            if (width) Resize(line, 0, basis.ToRelative(mode, (float)(size.Width / scale.Across), false));
            if (height) Resize(line, 1, basis.ToRelative(mode, (float)(size.Height / scale.Down), true));
        }
        return (changes, skipped == 0 ? null
            : $"{skipped} element{(skipped == 1 ? " has" : "s have")} no BitmapRect or Rect to size: text and models keep their own.");

        void Resize(HudBlock line, int index, double value)
        {
            value = Math.Round(value, 6);
            var old = line.Arguments[index];
            var changed = old.Kind == HudArgKind.Integer && Math.Abs(value - Math.Round(value)) < 1e-6
                ? HudArgument.FromInt((long)Math.Round(value))
                : HudArgument.FromFloat(value);
            if (changed != old) changes.Add(new ArgumentChange(line, index, old, changed));
        }
    }
}
