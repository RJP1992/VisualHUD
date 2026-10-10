using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App.Views;

/// <summary>
/// Distances on the canvas, in screen pixels: while dragging, the gap from what is dragged to the nearest
/// element on each side (or the screen's edge); with Alt held over an element, the gaps between it and the one
/// with the cross, or from the one inside to the one round it.
/// </summary>
public partial class HudCanvas
{
    private readonly List<FrameworkElement> _measures = new();
    private readonly List<Rect> _otherBounds = new();
    private static readonly Brush GapBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xff, 0x8a, 0x3d)));

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>The gaps drawn now, from one edge to the other, with their labels: the tests' way to read them.</summary>
    internal List<(Point From, Point To, string Label)> Gaps { get; } = new();

    private void ClearGaps()
    {
        foreach (var m in _measures) CanvasSurface.Children.Remove(m);
        _measures.Clear();
        Gaps.Clear();
    }

    /// <summary>The gap from what is dragged to the nearest element facing it on each side, or to the screen's edge.</summary>
    private void DrawDragGaps(Rect moving)
    {
        ClearGaps();
        if (moving.IsEmpty) return;
        // Facing: sharing some of the other axis.
        var across = _otherBounds.Where(o => o.Top < moving.Bottom && moving.Top < o.Bottom).Cast<Rect?>().ToList();
        var down = _otherBounds.Where(o => o.Left < moving.Right && moving.Left < o.Right).Cast<Rect?>().ToList();
        var left = across.Where(o => o!.Value.Right <= moving.Left).OrderByDescending(o => o!.Value.Right).FirstOrDefault();
        var right = across.Where(o => o!.Value.Left >= moving.Right).OrderBy(o => o!.Value.Left).FirstOrDefault();
        var above = down.Where(o => o!.Value.Bottom <= moving.Top).OrderByDescending(o => o!.Value.Bottom).FirstOrDefault();
        var below = down.Where(o => o!.Value.Top >= moving.Bottom).OrderBy(o => o!.Value.Top).FirstOrDefault();

        // At the middle of what the two share, or of what is dragged when it faces the screen's edge.
        double Y(Rect? o) => o is { } r ? (Math.Max(r.Top, moving.Top) + Math.Min(r.Bottom, moving.Bottom)) / 2 : moving.Top + moving.Height / 2;
        double X(Rect? o) => o is { } r ? (Math.Max(r.Left, moving.Left) + Math.Min(r.Right, moving.Right)) / 2 : moving.Left + moving.Width / 2;
        Gap(new Point(left?.Right ?? 0, Y(left)), new Point(moving.Left, Y(left)));
        Gap(new Point(moving.Right, Y(right)), new Point(right?.Left ?? _screen.Width, Y(right)));
        Gap(new Point(X(above), above?.Bottom ?? 0), new Point(X(above), moving.Top));
        Gap(new Point(X(below), moving.Bottom), new Point(X(below), below?.Top ?? _screen.Height));
    }

    /// <summary>
    /// With Alt held at a point: the gaps between the element with the cross and the element under the mouse,
    /// from the inside one to the edges of the one round it, or from the element with the cross to the screen's
    /// edges over nothing.
    /// </summary>
    internal void MeasureAt(Point pos)
    {
        ClearGaps();
        if (_currentSelection is not { } current || ScreenBoundsOf(current) is not { } a) return;
        // The smallest element whose outline the mouse is in: a bitmap over its group, a group over nothing drawn.
        var hovered = _layout?.All
            .Where(e => e.Piece is null && !ReferenceEquals(e.Block, current) && (_visuals.ContainsKey(e.Block) ||
                        ElementKinds.IsGroupBased(e.Kind) && HasRectLine(e)))
            .Select(e => ScreenBounds(e))
            .Where(r => !r.IsEmpty && r.Contains(pos))
            .OrderBy(r => r.Width * r.Height)
            .Cast<Rect?>()
            .FirstOrDefault();
        var b = hovered ?? new Rect(0, 0, _screen.Width, _screen.Height);
        var midY = a.Top + a.Height / 2;
        var midX = a.Left + a.Width / 2;
        if (b.Contains(a) || a.Contains(b))
        {
            var (inner, outer) = b.Contains(a) ? (a, b) : (b, a);
            var y = inner.Top + inner.Height / 2;
            var x = inner.Left + inner.Width / 2;
            Gap(new Point(outer.Left, y), new Point(inner.Left, y));
            Gap(new Point(inner.Right, y), new Point(outer.Right, y));
            Gap(new Point(x, outer.Top), new Point(x, inner.Top));
            Gap(new Point(x, inner.Bottom), new Point(x, outer.Bottom));
        }
        else
        {
            if (b.Left >= a.Right) Gap(new Point(a.Right, midY), new Point(b.Left, midY));
            else if (b.Right <= a.Left) Gap(new Point(b.Right, midY), new Point(a.Left, midY));
            if (b.Top >= a.Bottom) Gap(new Point(midX, a.Bottom), new Point(midX, b.Top));
            else if (b.Bottom <= a.Top) Gap(new Point(midX, b.Bottom), new Point(midX, a.Top));
        }
        _altInDrag = true;   // Alt's release must not open the menu
    }

    /// <summary>One gap: a line between two edges and its length, if it has one.</summary>
    private void Gap(Point from, Point to)
    {
        var length = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
        if (length < 0.5) return;
        var scale = 1 / Math.Max(ZoomTransform.ScaleX, 0.01);
        var line = new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = GapBrush, StrokeThickness = scale, IsHitTestVisible = false };
        var label = string.Create(CultureInfo.InvariantCulture, $"{length:0.#}");
        var text = new Border
        {
            Background = GapBrush,
            CornerRadius = new CornerRadius(2 * scale),
            Padding = new Thickness(3 * scale, 0, 3 * scale, 0),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = label, Foreground = Brushes.Black, FontSize = 11 * scale, FontWeight = FontWeights.SemiBold },
        };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(text, (from.X + to.X) / 2 - text.DesiredSize.Width / 2);
        Canvas.SetTop(text, (from.Y + to.Y) / 2 - text.DesiredSize.Height / 2);
        foreach (var visual in new FrameworkElement[] { line, text })
        {
            Panel.SetZIndex(visual, int.MaxValue);
            CanvasSurface.Children.Add(visual);
            _measures.Add(visual);
        }
        Gaps.Add((from, to, label));
    }

    /// <summary>The mouse over the canvas: with Alt held, the distances; without, none.</summary>
    private void MeasureOnHover(MouseEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) MeasureAt(e.GetPosition(CanvasSurface));
        else if (_measures.Count > 0) ClearGaps();
    }
}
