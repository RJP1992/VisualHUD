using System.Collections.ObjectModel;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;
using static SwbfHudEditor.Core.Edits.HudPlacement;

namespace SwbfHudEditor.App;

/// <summary>
/// What holds what, changed with nothing moving on the screen: a group's Rect fitted to what it holds, the
/// selection put in a group of its own or a group's elements taken out of it, rows dragged to another parent.
/// </summary>
public partial class MainWindow
{
    public static readonly RoutedCommand GroupCommand = new("Group", typeof(MainWindow));
    public static readonly RoutedCommand UngroupCommand = new("Ungroup", typeof(MainWindow));
    public static readonly RoutedCommand FitRectCommand = new("FitRect", typeof(MainWindow));

    private static readonly float[] AlignShares = { 0f, 0.5f, 1f };

    /// <summary>The layout the canvas draws, at the load state: what written numbers put where.</summary>
    private LayoutOptions? KeepPlaceOptions =>
        Canvas.LayoutOptions is { } options ? options with { Simulation = null, ShowByEvents = false, BarValues = null } : null;

    private void SetUpStructure()
    {
        Properties.FitTextBoxRequested += (_, block) => FitTextBox(block);
        CommandBindings.Add(new CommandBinding(GroupCommand, (_, _) => GroupSelection(), (_, e) =>
            e.CanExecute = !TypingInBox && TopSelectedBlocks().Any(IsElement)));
        CommandBindings.Add(new CommandBinding(UngroupCommand, (_, _) => UngroupSelection(), (_, e) =>
            e.CanExecute = !TypingInBox && Tree.SelectedItem is HudBlock { Name: "Group", HasBody: true }));
        CommandBindings.Add(new CommandBinding(FitRectCommand, (_, _) => FitSelectedRect(), (_, e) =>
            e.CanExecute = !TypingInBox && Tree.SelectedItem is HudBlock { Name: "Group", HasBody: true }));
        InputBindings.Add(new KeyBinding(GroupCommand, Key.G, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(UngroupCommand, Key.G, ModifierKeys.Control | ModifierKeys.Shift));
    }

    private static bool IsElement(HudBlock block) => ElementKinds.TryGet(block, out _);

    /// <summary>One undo step of edits already made, and the canvas and panels following.</summary>
    private void Commit(List<IHudEdit> edits, string description, HudBlock? show)
    {
        _undo.Push(edits.Count == 1 ? edits[0] : new CompositeEdit(edits, description));
        _knownEvents = null;
        AfterBlocksChanged(show);
    }

    // ---- Fit a group's Rect to what it holds ---------------------------------------------------------------

    private void FitSelectedRect()
    {
        if (Tree.SelectedItem is HudBlock group) FitRect(group);
    }

    /// <summary>
    /// A plain group's Rect made the box round what its elements draw, in its own pixels: its Position moved so
    /// the box's corner is its origin (the Rect's alignment taken into account), its elements moved back by as
    /// much, so nothing moves. Its Rect is its frame, the pivot of its alignment and the outline it is picked
    /// and lined up by. A group GameExt slides keeps sliding to the same edge.
    /// </summary>
    internal bool FitRect(HudBlock group)
    {
        if (Canvas.LaidOut(group) is not { } g)
        {
            StatusText.Text = "Fit the Rect of a group drawn on the canvas.";
            return false;
        }
        if (g.Kind != ElementKind.Group)
        {
            StatusText.Text = $"A {group.Name}'s Rect is part of what it draws (its text, box, ring or markers): only a plain Group's is fitted to what it holds.";
            return false;
        }
        if (ContentsBounds(g) is not { } box || KeepPlaceOptions is not { } options)
        {
            StatusText.Text = "Nothing in the group is drawn to fit its Rect to.";
            return false;
        }
        var notes = new List<string>();
        var edits = FitRectEdits(group, g, box, options, notes);
        if (edits.Count == 0)
        {
            StatusText.Text = "The group's Rect fits what it holds already.";
            return false;
        }
        Commit(edits, $"fit the Rect of {group.Name} \"{group.ItemName}\"", group);
        StatusText.Text = $"Fitted the Rect of {group.Name} \"{group.ItemName}\" to what it holds, {box.Width:0.#}×{box.Height:0.#} of its pixels; nothing moved." +
                          (notes.Count > 0 ? " " + string.Join(" ", notes) : string.Empty);
        return true;
    }

    /// <summary>The box round what a group's elements draw, in the group's own pixels.</summary>
    private Rect? ContentsBounds(LaidOutElement g)
    {
        if (!Matrix4x4.Invert(g.ToScreen, out var toOwn)) return null;
        var bounds = Rect.Empty;
        foreach (var d in g.SelfAndDescendants().Skip(1))
        {
            var b = Canvas.ScreenBounds(d);
            if (b.IsEmpty) continue;
            foreach (var corner in new[] { b.TopLeft, b.TopRight, b.BottomLeft, b.BottomRight })
            {
                var p = Vector2.Transform(new Vector2((float)corner.X, (float)corner.Y), toOwn);
                bounds.Union(new Point(p.X, p.Y));
            }
        }
        return bounds.IsEmpty ? null : bounds;
    }

    private List<IHudEdit> FitRectEdits(HudBlock group, LaidOutElement g, Rect box, LayoutOptions options, List<string> notes)
    {
        var edits = new List<IHudEdit>();
        void Apply(IHudEdit edit)
        {
            edit.Apply();
            edits.Add(edit);
        }
        var keep = HudKeepPlace.Capture(HudLayout.Lay(Documents, options), group.Children.Where(IsElement).ToList());
        var spec = g.Spec;
        var w = (float)box.Width;
        var h = (float)box.Height;
        var lines = group.Children.TakeWhile(c => !c.HasBody).Count();

        // The Rect: the box's size, in the Rect's own unit (Viewport for a new one), its alignment kept.
        var rect = Last(group, "Rect");
        var mode = rect is null ? RelativeMode.Viewport : spec.RectMode;
        var rectArgs = new List<HudArgument>
        {
            Number(EngineMath.ToRelative(mode, w, g.FrameWidth, g.ViewWidth, g.ScreenWidth), rect?.Arguments[0]),
            Number(EngineMath.ToRelative(mode, h, g.FrameHeight, g.ViewHeight, g.ScreenHeight), rect?.Arguments[1]),
            rect is not null && EngineArgs.At(rect, 2) is { } ha ? ha : HudArgument.FromString("Left"),
            rect is not null && EngineArgs.At(rect, 3) is { } va ? va : HudArgument.FromString("Top"),
            HudArgument.FromString(EngineArgs.RelativeModeNames[(int)mode]),
        };
        if (rect is null) Apply(new InsertBlockEdit(group.Children, lines++, new HudBlock("Rect", rectArgs.ToArray()), "add Rect", affected: group));
        else if (!rectArgs.SequenceEqual(rect.Arguments)) Apply(new ReplaceArgumentsEdit(rect, rectArgs, group));

        // The Position: the box's corner is the new origin, which the Rect's alignment puts its share of the
        // Rect before the point the Position gives, in the parent's pixels.
        var corner = Vector2.Transform(new Vector2((float)box.Left, (float)box.Top), g.Local);
        var point = corner + new Vector2(AlignShares[(int)spec.RectHAlign] * w, AlignShares[(int)spec.RectVAlign] * h);
        var position = Last(group, "Position");
        var pmode = spec.PositionMode;
        var positionArgs = new List<HudArgument>
        {
            Number(EngineMath.ToRelative(pmode, point.X, g.FrameWidth, g.ViewWidth, g.ScreenWidth), position?.Arguments[0]),
            Number(EngineMath.ToRelative(pmode, point.Y, g.FrameHeight, g.ViewHeight, g.ScreenHeight), position?.Arguments[1]),
            (position is null ? null : EngineArgs.At(position, 2)) ?? HudArgument.FromInt(0),
        };
        if (position is not null) positionArgs.AddRange(position.Arguments.Skip(3));
        if (position is null) Apply(new InsertBlockEdit(group.Children, lines++, new HudBlock("Position", positionArgs.ToArray()), "add Position", affected: group));
        else if (!positionArgs.SequenceEqual(position.Arguments)) Apply(new ReplaceArgumentsEdit(position, positionArgs, group));

        // GameExt sorts a piece by the third of the layout its origin is in: it keeps its edge.
        if (g.Role == PieceRole.Piece && g.FileLayout.Opted && spec.ScreenAnchorShare is null && g.Third is { } third &&
            TrueWidescreen.AnchorFor(corner.X, g.FileLayout.LayoutWidth) != third)
        {
            Apply(new InsertBlockEdit(group.Children, lines, new HudBlock("ScreenAnchor", HudArgument.FromString(TrueWidescreen.AnchorName(third))),
                "add ScreenAnchor", affected: group));
            notes.Add($"It keeps to the {TrueWidescreen.AnchorName(third).ToLowerInvariant()} of a wider screen with a ScreenAnchor, as its origin is now in another third.");
        }

        if (edits.Count > 0) edits.AddRange(keep.Restore(HudLayout.Lay(Documents, options), notes));
        return edits;
    }

    // ---- A Text's box fitted to its text --------------------------------------------------------------------

    /// <summary>
    /// A Text's TextBox made the size of its lines as they show now (the text, an example or a long text, as
    /// the canvas lays it out), with a pixel to spare so it wraps as it does; its Position moved so the text
    /// stays where it is. With its text on the baseline, only its width.
    /// </summary>
    internal bool FitTextBox(HudBlock block)
    {
        if (Canvas.LaidOut(block) is not { Kind: ElementKind.Text } e || Canvas.TextExtentOf(block) is not { } text)
        {
            StatusText.Text = "Fit the box of a Text whose text shows on the canvas.";
            return false;
        }
        var baseline = e.Spec.TextVAlign == TextVAlign.Baseline;
        var w = (float)Math.Ceiling(text.Width) + 1;
        var h = baseline ? e.TextBoxHeight : (float)Math.Ceiling(text.Height) + 1;
        var shift = new Vector2((float)text.Left, baseline ? 0 : (float)text.Top);

        var edits = new List<IHudEdit>();
        void Apply(IHudEdit edit)
        {
            edit.Apply();
            edits.Add(edit);
        }
        var lines = block.Children.TakeWhile(c => !c.HasBody).Count();
        var box = Last(block, "TextBox");
        var boxArgs = new List<HudArgument>
        {
            Number((w + 0.0001) / e.ViewWidth, box?.Arguments[0]),
            Number((h + 0.0001) / e.ViewHeight, box?.Arguments[1]),
        };
        if (box is not null) boxArgs.AddRange(box.Arguments.Skip(2));
        if (box is null) Apply(new InsertBlockEdit(block.Children, lines++, new HudBlock("TextBox", boxArgs.ToArray()), "add TextBox", affected: block));
        else if (!boxArgs.SequenceEqual(box.Arguments)) Apply(new ReplaceArgumentsEdit(box, boxArgs, block));

        // The box's corner where the text's is: its origin moves by as much, in its parent's pixels.
        var moved = Vector2.TransformNormal(shift, e.Local);
        if (moved.LengthSquared() > 1e-8f)
        {
            var position = Last(block, "Position");
            var mode = e.Spec.PositionMode;
            var x = (position is null ? 0 : EngineArgs.Float(position.Arguments[0])) + EngineMath.ToRelative(mode, moved.X, e.FrameWidth, e.ViewWidth, e.ScreenWidth);
            var y = (position is null ? 0 : EngineArgs.Float(position.Arguments[1])) + EngineMath.ToRelative(mode, moved.Y, e.FrameHeight, e.ViewHeight, e.ScreenHeight);
            var args = new List<HudArgument> { Number(x, position?.Arguments[0]), Number(y, position?.Arguments[1]),
                (position is null ? null : EngineArgs.At(position, 2)) ?? HudArgument.FromInt(0) };
            if (position is not null) args.AddRange(position.Arguments.Skip(3));
            if (position is null) Apply(new InsertBlockEdit(block.Children, lines, new HudBlock("Position", args.ToArray()), "add Position", affected: block));
            else if (!args.SequenceEqual(position.Arguments)) Apply(new ReplaceArgumentsEdit(position, args, block));
        }
        if (edits.Count == 0)
        {
            StatusText.Text = "The box fits its text already.";
            return false;
        }
        Commit(edits, $"fit the box of {block.Name} \"{block.ItemName}\"", block);
        StatusText.Text = $"Fitted the box of {block.Name} \"{block.ItemName}\" to its text, {w:0}×{h:0} pixels; the text stayed where it was.";
        return true;
    }

    private void FitTextBox_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is HudBlock block) FitTextBox(block);
    }

    // ---- Group and ungroup ---------------------------------------------------------------------------------

    /// <summary>
    /// The selected elements, all in one place, put in a new Group in the place of the first, nothing moving.
    /// At the top of a file it takes their viewport; its Rect is fitted to them, except at the top of a file
    /// GameExt lays out, where a group with a Position would slide as one piece and they slide each on its own.
    /// </summary>
    internal HudBlock? GroupSelection()
    {
        var blocks = TopSelectedBlocks().Where(IsElement).ToList();
        if (blocks.Count == 0)
        {
            StatusText.Text = "Select elements to put them in a group.";
            return null;
        }
        if (Locate(blocks[0]) is not { } at || at.File.Document.FindContainer(blocks[0]) is not { } where ||
            blocks.Any(b => at.File.Document.FindContainer(b) is not { } c || !ReferenceEquals(c.Container, where.Container)))
        {
            StatusText.Text = "Pick elements in the same group, or all at the top of one file, to group them.";
            return null;
        }
        if (KeepPlaceOptions is not { } options) return null;
        var list = where.Container;
        blocks = blocks.OrderBy(list.IndexOf).ToList();
        var notes = new List<string>();
        var edits = new List<IHudEdit>();
        void Apply(IHudEdit edit)
        {
            edit.Apply();
            edits.Add(edit);
        }
        var keep = HudKeepPlace.Capture(HudLayout.Lay(Documents, options), blocks);
        var top = IsTopList(list);
        var viewport = top ? Canvas.LaidOut(blocks[0])?.Viewport ?? 0 : 0;

        var group = new HudBlock("Group", HudArgument.FromString(HudClipboard.Unique("group", IsNameTaken()))) { HasBody = true };
        if (viewport > 0) group.Children.Add(new HudBlock("Viewport", HudArgument.FromInt(viewport)));
        Apply(new InsertBlockEdit(list, list.IndexOf(blocks[0]), group, "add Group", affected: group));
        foreach (var block in blocks)
        {
            ForgetIndentation(block);
            Apply(new MoveBlockEdit(list, list.IndexOf(block), group.Children, group.Children.Count, block));
        }
        var after = HudLayout.Lay(Documents, options);
        edits.AddRange(keep.Restore(after, notes));

        // Its Rect fitted to them, unless it would turn them from pieces sliding each on its own into one.
        if (after.Find(group) is { } g && ContentsBounds(g) is { } box)
        {
            if (top && g.FileLayout.Opted) notes.Add("Its Rect is not fitted: at the top of a file GameExt lays out, a group with a Position slides as one piece, and they slide each on its own.");
            else edits.AddRange(FitRectEdits(group, g, box, options, notes));
        }
        Commit(edits, $"group {blocks.Count} element{(blocks.Count == 1 ? "" : "s")}", group);
        SelectBlocks(new[] { group });
        Properties.FocusName();
        StatusText.Text = $"Put {Describe(blocks.Cast<object>().ToList())} in Group \"{group.ItemName}\"; nothing moved." +
                          (notes.Count > 0 ? " " + string.Join(" ", notes) : string.Empty);
        return group;
    }

    private void UngroupSelection()
    {
        if (Tree.SelectedItem is HudBlock group) Ungroup(group);
    }

    /// <summary>
    /// A plain group's elements taken out into its place, nothing moving: what the group's matrix did, its
    /// Position and Scale, goes into theirs. Its own lines go with it; those that showed, hid or tinted them are named.
    /// </summary>
    internal bool Ungroup(HudBlock group)
    {
        if (group.Name != "Group" || !group.HasBody || Locate(group) is not { } at || at.File.Document.FindContainer(group) is not { } where)
        {
            StatusText.Text = "Select a Group to take its elements out of it.";
            return false;
        }
        var children = group.Children.Where(IsElement).ToList();
        if (children.Count == 0 || KeepPlaceOptions is not { } options)
        {
            StatusText.Text = "The group holds no elements to take out.";
            return false;
        }
        var notes = new List<string>();
        var edits = new List<IHudEdit>();
        void Apply(IHudEdit edit)
        {
            edit.Apply();
            edits.Add(edit);
        }
        var keep = HudKeepPlace.Capture(HudLayout.Lay(Documents, options), children);
        var list = where.Container;
        var top = IsTopList(list);
        var viewport = top ? Canvas.LaidOut(group)?.Viewport ?? 0 : 0;
        var index = list.IndexOf(group);
        foreach (var child in children)
        {
            ForgetIndentation(child);
            Apply(new MoveBlockEdit(group.Children, group.Children.IndexOf(child), list, ++index, child));
            // At the top of a file an element shows in its own viewport, which the group gave it before.
            if (viewport > 0 && !child.Children.Any(c => !c.HasBody && c.Name == "Viewport"))
                Apply(new InsertBlockEdit(child.Children, 0, new HudBlock("Viewport", HudArgument.FromInt(viewport)), "add Viewport", affected: child));
        }
        var dropped = group.Children.Where(c => !c.HasBody && c.Name is not ("Position" or "Rect" or "Viewport" or "Scale" or "Rotation"))
            .Select(c => c.Name).Distinct().ToList();
        Apply(new DeleteBlockEdit(list, list.IndexOf(group), group, null));
        edits.AddRange(keep.Restore(HudLayout.Lay(Documents, options), notes));
        Commit(edits, $"ungroup {group.Name} \"{group.ItemName}\"", null);
        SelectBlocks(children);
        StatusText.Text = $"Took {children.Count} element{(children.Count == 1 ? "" : "s")} out of Group \"{group.ItemName}\"; nothing moved." +
                          (dropped.Count > 0 ? $" Its own {string.Join(", ", dropped)} went with it." : string.Empty) +
                          (notes.Count > 0 ? " " + string.Join(" ", notes) : string.Empty);
        return true;
    }

    /// <summary>A list at the top of a file or of a ViewPort, where an element's own Viewport counts.</summary>
    private bool IsTopList(ObservableCollection<HudBlock> list) =>
        _files.Any(f => ReferenceEquals(f.Document.TopLevel, list) ||
                        AllBlocks(f.Document.TopLevel).Any(b => b.HasBody && b.Name.Equals("ViewPort", StringComparison.OrdinalIgnoreCase) && ReferenceEquals(b.Children, list)));
}
