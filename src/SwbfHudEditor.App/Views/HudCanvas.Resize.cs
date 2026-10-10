using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App.Views;

/// <summary>The handles a selected element is resized by: a corner or an edge of its quad, its Rect or its TextBox.</summary>
public enum ResizeHandle { TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }

/// <summary>
/// Resizing on the canvas: a handle of the element with the cross dragged changes its size line in its own
/// unit, its Position so the edges not dragged stay, and for a group, what it holds stays where it is. Shift
/// keeps its shape; the edges dragged snap as a move does, unless Alt is held.
/// </summary>
public partial class HudCanvas
{
    private enum SizeKind { BitmapRect, Rect, TextBox }

    private sealed class ResizeState
    {
        public required HudBlock Block { get; init; }
        public required LaidOutElement Element { get; init; }
        public required ResizeHandle Handle { get; init; }
        public required SizeKind Kind { get; init; }
        public required QuadRect Start { get; init; }
        public required Matrix4x4 ToOwn { get; init; }
        public required HudBlock SizeLine { get; init; }
        public required HudBlock? Position { get; init; }
        /// <summary>A group's elements placed from its origin, not its frame's shares: they stay as its origin moves.</summary>
        public required List<(HudBlock Line, LaidOutElement Child)> Children { get; init; }
        /// <summary>Every argument a resize may change, as it was.</summary>
        public required List<ArgumentChange> Originals { get; init; }
    }

    private ResizeState? _resize;
    private readonly List<FrameworkElement> _handles = new();

    /// <summary>What a selected element is resized by, in its own pixels, or null when it has nothing to size.</summary>
    private static (SizeKind Kind, HudBlock Line, QuadRect Rect)? Sizing(LaidOutElement e)
    {
        HudBlock? Line(string name) => e.Block.Children.LastOrDefault(c => !c.HasBody && c.Name == name && c.Arguments.Count >= 2 &&
                                                                         EngineArgs.IsNumber(c.Arguments[0]) && EngineArgs.IsNumber(c.Arguments[1]));
        if (e.Piece is not null) return null;
        if (ElementKinds.IsBitmapBased(e.Kind) && Line("BitmapRect") is { } bitmapRect && (e.FullQuad ?? e.Quad) is { } quad)
            return (SizeKind.BitmapRect, bitmapRect, quad);
        if (ElementKinds.IsGroupBased(e.Kind) && Line("Rect") is { } rect)
            return (SizeKind.Rect, rect, new QuadRect(0, 0, e.RectWidthPixels, e.RectHeightPixels));
        if (e.Kind == ElementKind.Text && Line("TextBox") is { } box)
            return (SizeKind.TextBox, box, new QuadRect(0, 0, e.TextBoxWidth, e.TextBoxHeight));
        return null;
    }

    private static Point HandlePoint(QuadRect r, ResizeHandle h) => h switch
    {
        ResizeHandle.TopLeft => new Point(r.Left, r.Top),
        ResizeHandle.Top => new Point((r.Left + r.Right) / 2, r.Top),
        ResizeHandle.TopRight => new Point(r.Right, r.Top),
        ResizeHandle.Right => new Point(r.Right, (r.Top + r.Bottom) / 2),
        ResizeHandle.BottomRight => new Point(r.Right, r.Bottom),
        ResizeHandle.Bottom => new Point((r.Left + r.Right) / 2, r.Bottom),
        ResizeHandle.BottomLeft => new Point(r.Left, r.Bottom),
        _ => new Point(r.Left, (r.Top + r.Bottom) / 2),
    };

    private static Cursor HandleCursor(ResizeHandle h) => h switch
    {
        ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
        ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
        ResizeHandle.Left or ResizeHandle.Right => Cursors.SizeWE,
        _ => Cursors.SizeNS,
    };

    /// <summary>The handles of the element with the cross, when it is the one selected and has a size to drag.</summary>
    private void DrawHandles(LaidOutElement e)
    {
        _handles.Clear();
        if (_selection.Count > 1 || Sizing(e) is not { } sizing) return;
        var size = 7 / Math.Max(ZoomTransform.ScaleX, 0.01);
        foreach (var handle in Enum.GetValues<ResizeHandle>())
        {
            var p = HandlePoint(sizing.Rect, handle);
            var at = ToScreen(e.ToScreen, (float)p.X, (float)p.Y);
            var square = new Rectangle
            {
                Width = size,
                Height = size,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(0x1f, 0x6f, 0xeb)),
                StrokeThickness = 1 / Math.Max(ZoomTransform.ScaleX, 0.01),
                Cursor = HandleCursor(handle),
                ToolTip = "Drag to resize: Shift keeps its shape, Alt does not snap",
            };
            System.Windows.Controls.Canvas.SetLeft(square, at.X - size / 2);
            System.Windows.Controls.Canvas.SetTop(square, at.Y - size / 2);
            var role = handle;
            square.MouseLeftButtonDown += (_, args) =>
            {
                if (BeginResize(e.Block, role))
                {
                    CanvasSurface.CaptureMouse();
                    args.Handled = true;
                }
            };
            AddHighlight(square);
            _handles.Add(square);
        }
    }

    /// <summary>The handles drawn now: the tests' way to see them.</summary>
    internal int HandleCount => _handles.Count;

    /// <summary>Start resizing an element by a handle, as a press on it does.</summary>
    internal bool BeginResize(HudBlock block, ResizeHandle handle)
    {
        if (_layout?.Find(block) is not { } e || Sizing(e) is not { } sizing || !Matrix4x4.Invert(e.ToScreen, out var toOwn)) return false;
        var position = e.Block.Children.LastOrDefault(c => !c.HasBody && c.Name == "Position" && c.Arguments.Count >= 2 &&
                                                           EngineArgs.IsNumber(c.Arguments[0]) && EngineArgs.IsNumber(c.Arguments[1]));
        var originals = new List<ArgumentChange>();
        void Keep(HudBlock line, int count)
        {
            for (var i = 0; i < count; i++) originals.Add(new ArgumentChange(line, i, line.Arguments[i], line.Arguments[i]));
        }
        Keep(sizing.Line, 2);
        if (position is not null) Keep(position, 2);
        var children = new List<(HudBlock, LaidOutElement)>();
        if (sizing.Kind == SizeKind.Rect)
            foreach (var child in e.Children.Where(c => c.Piece is null))
                if (child.Block.Children.LastOrDefault(c => !c.HasBody && c.Name == "Position" && c.Arguments.Count >= 2 &&
                                                            EngineArgs.IsNumber(c.Arguments[0]) && EngineArgs.IsNumber(c.Arguments[1])) is { } line &&
                    EngineArgs.PositionMode(EngineArgs.At(line, 3)) != RelativeMode.Frame)
                {
                    children.Add((line, child));
                    Keep(line, 2);
                }

        _resize = new ResizeState
        {
            Block = block,
            Element = e,
            Handle = handle,
            Kind = sizing.Kind,
            Start = sizing.Rect,
            ToOwn = toOwn,
            SizeLine = sizing.Line,
            Position = position,
            Children = children,
            Originals = originals,
        };
        _altInDrag = false;
        BuildSnapLines(new[] { e });
        return true;
    }

    /// <summary>Resize to where the mouse is, in screen pixels, with these keys held.</summary>
    internal void ResizeTo(Point screen, ModifierKeys keys)
    {
        if (_resize is not { } r) return;
        var p = Vector2.Transform(new Vector2((float)screen.X, (float)screen.Y), r.ToOwn);
        var start = r.Start;
        double left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
        var h = r.Handle;
        var moveLeft = h is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        var moveRight = h is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;
        var moveTop = h is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
        var moveBottom = h is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;
        if (moveLeft) left = Math.Min(p.X, right - 1);
        if (moveRight) right = Math.Max(p.X, left + 1);
        if (moveTop) top = Math.Min(p.Y, bottom - 1);
        if (moveBottom) bottom = Math.Max(p.Y, top + 1);

        // The edges dragged meet the lines within reach, where the element is not turned.
        (double? X, double? Y) met = (null, null);
        var m = r.Element.ToScreen;
        if ((keys & ModifierKeys.Alt) != 0) _altInDrag = true;
        else if (MathF.Abs(m.M12) < 1e-4f && MathF.Abs(m.M21) < 1e-4f && m.M11 > 0 && m.M22 > 0)
        {
            double Snapped(double own, bool across, ref double? line)
            {
                var onScreen = across ? own * m.M11 + m.M41 : own * m.M22 + m.M42;
                if (Nearest(new[] { onScreen }, 0, across ? _snapXs : _snapYs, SnapReach) is not { } hit) return own;
                line = hit.Line;
                return across ? (hit.Line - m.M41) / m.M11 : (hit.Line - m.M42) / m.M22;
            }
            double? lineX = null, lineY = null;
            if (moveLeft) left = Math.Min(Snapped(left, true, ref lineX), right - 1);
            if (moveRight) right = Math.Max(Snapped(right, true, ref lineX), left + 1);
            if (moveTop) top = Math.Min(Snapped(top, false, ref lineY), bottom - 1);
            if (moveBottom) bottom = Math.Max(Snapped(bottom, false, ref lineY), top + 1);
            met = (lineX, lineY);
        }

        // Shift keeps the shape: a corner by the side moved most, an edge growing the other side round its middle.
        if ((keys & ModifierKeys.Shift) != 0 && start.Width > 0 && start.Height > 0)
        {
            var aspect = start.Width / (double)start.Height;
            var w = right - left;
            var hgt = bottom - top;
            if ((moveLeft || moveRight) && (moveTop || moveBottom))
            {
                if (w / start.Width >= hgt / start.Height) hgt = w / aspect;
                else w = hgt * aspect;
                if (moveLeft) left = right - w; else right = left + w;
                if (moveTop) top = bottom - hgt; else bottom = top + hgt;
            }
            else if (moveLeft || moveRight)
            {
                var middle = (start.Top + start.Bottom) / 2.0;
                hgt = w / aspect;
                (top, bottom) = (middle - hgt / 2, middle + hgt / 2);
            }
            else
            {
                var middle = (start.Left + start.Right) / 2.0;
                w = hgt * aspect;
                (left, right) = (middle - w / 2, middle + w / 2);
            }
            met = (null, null);
        }

        ApplyResize(r, left, top, right, bottom);
        _reuseVisuals = true;
        Render();
        DrawGuides(met.X, met.Y);
        BlockDragged?.Invoke(this, r.Block);
    }

    /// <summary>The lines for a new box in the element's own pixels, from the numbers as they were.</summary>
    private static void ApplyResize(ResizeState r, double left, double top, double right, double bottom)
    {
        foreach (var o in r.Originals) o.Line.Arguments[o.Index] = o.Old;
        var e = r.Element;
        var w = (float)(right - left);
        var h = (float)(bottom - top);

        // The size, in the line's own unit: a BitmapRect's and a Rect's mode, a TextBox's shares of the view.
        var (sw, sh) = r.Kind switch
        {
            SizeKind.BitmapRect => (Relative(EngineArgs.BitmapRectMode(r.SizeLine), w, e.FrameWidth, e.ViewWidth, e.ScreenWidth),
                                    Relative(EngineArgs.BitmapRectMode(r.SizeLine), h, e.FrameHeight, e.ViewHeight, e.ScreenHeight)),
            SizeKind.Rect => (Relative(e.Spec.RectMode, w, e.FrameWidth, e.ViewWidth, e.ScreenWidth),
                              Relative(e.Spec.RectMode, h, e.FrameHeight, e.ViewHeight, e.ScreenHeight)),
            _ => ((w + 0.0001) / e.ViewWidth, (h + 0.0001) / e.ViewHeight),
        };
        r.SizeLine.Arguments[0] = HudPlacement.Number(sw, r.SizeLine.Arguments[0]);
        r.SizeLine.Arguments[1] = HudPlacement.Number(sh, r.SizeLine.Arguments[1]);

        // Where the origin goes, in its own pixels from where it was: a bitmap's origin is its alignment's share
        // into its quad; a group's and a text's, the box's corner, a group's Position adding its alignment's
        // share of the Rect in its parent's pixels.
        var (ah, av) = r.Kind == SizeKind.BitmapRect
            ? (AlignOf(e.Spec.BitmapHAlign), AlignOf(e.Spec.BitmapVAlign))
            : r.Kind == SizeKind.Rect ? (AlignOf(e.Spec.RectHAlign), AlignOf(e.Spec.RectVAlign)) : (0f, 0f);
        var origin = r.Kind == SizeKind.BitmapRect
            ? new Vector2((float)left + ah * w, (float)top + av * h)
            : new Vector2((float)left, (float)top);
        var moved = Vector2.TransformNormal(origin, e.Local);
        if (r.Kind == SizeKind.Rect) moved += new Vector2(ah * (w - r.Start.Width), av * (h - r.Start.Height));
        if (r.Position is { } position && moved.LengthSquared() > 1e-10f)
        {
            var mode = EngineArgs.PositionMode(EngineArgs.At(position, 3));
            position.Arguments[0] = HudPlacement.Number(EngineArgs.Float(position.Arguments[0]) +
                Relative(mode, moved.X, e.FrameWidth, e.ViewWidth, e.ScreenWidth), position.Arguments[0]);
            position.Arguments[1] = HudPlacement.Number(EngineArgs.Float(position.Arguments[1]) +
                Relative(mode, moved.Y, e.FrameHeight, e.ViewHeight, e.ScreenHeight), position.Arguments[1]);
        }

        // A group's elements are placed from its origin: as it moves, they move back to stay.
        if (r.Kind == SizeKind.Rect && (left != r.Start.Left || top != r.Start.Top))
            foreach (var (line, child) in r.Children)
            {
                var mode = EngineArgs.PositionMode(EngineArgs.At(line, 3));
                line.Arguments[0] = HudPlacement.Number(EngineArgs.Float(line.Arguments[0]) -
                    Relative(mode, (float)left - r.Start.Left, child.FrameWidth, child.ViewWidth, child.ScreenWidth), line.Arguments[0]);
                line.Arguments[1] = HudPlacement.Number(EngineArgs.Float(line.Arguments[1]) -
                    Relative(mode, (float)top - r.Start.Top, child.FrameHeight, child.ViewHeight, child.ScreenHeight), line.Arguments[1]);
            }
    }

    private static float AlignOf<T>(T align) where T : Enum => Convert.ToInt32(align) switch { 1 => 0.5f, 2 => 1f, _ => 0f };

    private static double Relative(RelativeMode mode, float pixels, float frame, float view, float screen) =>
        EngineMath.ToRelative(mode, pixels, frame, view, screen);

    /// <summary>End a resize: what it changed goes to the undo history as one step.</summary>
    internal void EndResize()
    {
        if (_resize is not { } r) return;
        _resize = null;
        DrawGuides(null, null);
        var changes = r.Originals.Select(o => o with { New = o.Line.Arguments[o.Index] }).Where(c => c.New != c.Old).ToList();
        if (changes.Count > 0) BlockDragCommitted?.Invoke(this, new ArgumentsEdit(changes, $"resize {r.Block.Name}", r.Block));
    }

    /// <summary>The lines a drag or a resize snaps to: every other element drawn, and the screen's edges and centre.</summary>
    private void BuildSnapLines(IReadOnlyList<LaidOutElement> moving)
    {
        _snapXs.Clear();
        _snapYs.Clear();
        _otherBounds.Clear();
        _snapXs.AddRange(new[] { 0, _screen.Width / 2.0, _screen.Width });
        _snapYs.AddRange(new[] { 0, _screen.Height / 2.0, _screen.Height });
        if (_layout is null) return;
        foreach (var other in _layout.All)
        {
            // What is drawn, and the frames of groups with a Rect written.
            var framed = ElementKinds.IsGroupBased(other.Kind) && other.Piece is null && HasRectLine(other);
            if (!_visuals.ContainsKey(other.Block) && !framed || moving.Any(m => ReferenceEquals(m, other) || IsInside(other, m))) continue;
            var b = ScreenBounds(other);
            if (b.IsEmpty) continue;
            _snapXs.AddRange(new[] { b.Left, b.Left + b.Width / 2, b.Right });
            _snapYs.AddRange(new[] { b.Top, b.Top + b.Height / 2, b.Bottom });
            _otherBounds.Add(b);
        }
    }
}
