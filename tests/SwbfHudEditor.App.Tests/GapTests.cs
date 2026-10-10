using System.Globalization;
using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>Distances: from what is dragged to its neighbours and the screen's edges, and with Alt between two elements.</summary>
public class GapTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Group("box")
        {
            Viewport(1)
            Position(80, 80, 0)
            Rect(400, 300, "Left", "Top", "Pixels")

            Bitmap("a")
            {
                Bitmap("square")
                BitmapRect(50, 50, "Left", "Top", "Pixels")
                Position(20, 30, 0)
            }
        }

        Bitmap("b")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(50, 50, "Left", "Top", "Pixels")
            Position(600, 110, 0)
        }

        """;

    private static readonly Dictionary<string, byte[]> Files = new() { ["square.tga"] = TestTextures.Tga(4, 4, 255, 255, 255, 255) };

    private static HudBlock Named(MainWindow w, string name) =>
        All(w.Files.SelectMany(f => f.Document.TopLevel)).First(b => b.HasBody && b.ItemName == name);

    private static IEnumerable<HudBlock> All(IEnumerable<HudBlock> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            foreach (var inner in All(b.Children)) yield return inner;
        }
    }

    private static Rect Drawn(MainWindow w, string name) => w.Canvas.ScreenBoundsOf(Named(w, name))!.Value;

    private static double Label(double gap) => double.Parse(gap.ToString("0.#", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    [Fact]
    public void A_Drag_Shows_The_Gaps_To_The_Nearest_Elements_Facing_It()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            var b = Drawn(w, "b");
            w.SelectByName("b");
            w.Canvas.BeginDrag(Named(w, "b"));
            w.Canvas.DragBy(-40, 0, ModifierKeys.Alt);
            var moved = Drawn(w, "b");
            var box = Drawn(w, "box");
            var labels = w.Canvas.Gaps.Select(g => double.Parse(g.Label, CultureInfo.InvariantCulture)).ToList();
            // To the group on its left, the screen's right edge, and the screen's top and bottom.
            Assert.Contains(Label(moved.Left - box.Right), labels);
            Assert.Contains(Label(w.Canvas.ScreenSize.Width - moved.Right), labels);
            Assert.Contains(Label(moved.Top), labels);
            Assert.Equal(4, w.Canvas.Gaps.Count);
            w.Canvas.EndDrag();
            Assert.Empty(w.Canvas.Gaps);
            Assert.True(b.Left - moved.Left > 39);
        });
    }

    [Fact]
    public void Alt_Over_An_Element_Shows_The_Gaps_To_It_Or_Inside_It()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            var a = Drawn(w, "a");
            var b = Drawn(w, "b");
            var box = Drawn(w, "box");
            w.SelectByName("a");

            // Beside it: the gap between them, across.
            w.Canvas.MeasureAt(new Point(b.Left + 5, b.Top + 5));
            var gap = Assert.Single(w.Canvas.Gaps);
            Assert.Equal(Label(b.Left - a.Right), double.Parse(gap.Label, CultureInfo.InvariantCulture));

            // Round it: from it to each of the group's edges.
            w.Canvas.MeasureAt(new Point(box.Right - 5, box.Bottom - 5));
            var inside = w.Canvas.Gaps.Select(g => double.Parse(g.Label, CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(4, inside.Count);
            Assert.Contains(Label(a.Left - box.Left), inside);
            Assert.Contains(Label(box.Bottom - a.Bottom), inside);
        });
    }
}
