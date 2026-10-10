using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>Align, space out and match size on the screen as drawn; a drag that snaps, keeps to an axis, and takes the selection.</summary>
public class ArrangeTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Bitmap("a")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(40, 20, "Left", "Top", "Pixels")
            Position(100, 100, 0)
        }

        Bitmap("b")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(100, 30, "Left", "Top", "Pixels")
            Position(300, 200, 0)
        }

        Bitmap("c")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(0.05, 0.1, "Center", "Center", "Viewport")
            Position(0.7, 0.5, 0, "Viewport")
        }

        """;

    private static readonly Dictionary<string, byte[]> Files = new() { ["square.tga"] = TestTextures.Tga(4, 4, 255, 255, 255, 255) };

    private static HudBlock Named(MainWindow w, string name) =>
        w.Files.SelectMany(f => f.Document.TopLevel).First(b => b.HasBody && b.ItemName == name);

    private static Rect Bounds(MainWindow w, string name) => w.Canvas.ScreenBoundsOf(Named(w, name))!.Value;

    private static void Pick(MainWindow w, params string[] names)
    {
        w.SelectByName(names[0]);
        foreach (var name in names.Skip(1)) w.TogglePick(Named(w, name));
    }

    [Fact]
    public void Align_Puts_Edges_And_Centres_In_Line_In_One_Undo_Step()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            var before = new[] { Bounds(w, "a"), Bounds(w, "b"), Bounds(w, "c") };
            Pick(w, "a", "b", "c");
            Assert.True(w.Arrange(MainWindow.Arrangement.Left) > 0);
            var left = before.Min(b => b.Left);
            foreach (var name in new[] { "a", "b", "c" }) Assert.Equal(left, Bounds(w, name).Left, 0.01);

            w.Arrange(MainWindow.Arrangement.Bottom);
            var bottom = before.Max(b => b.Bottom);
            foreach (var name in new[] { "a", "b", "c" }) Assert.Equal(bottom, Bounds(w, name).Bottom, 0.01);
            // Their sizes stay.
            Assert.Equal(before[1].Width, Bounds(w, "b").Width, 0.01);

            ApplicationCommands.Undo.Execute(null, w);
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(before[0], Bounds(w, "a"));
            Assert.Equal(before[2], Bounds(w, "c"));

            // One element lines up with the screen.
            w.SelectByName("b");
            w.Arrange(MainWindow.Arrangement.Centre);
            var b = Bounds(w, "b");
            Assert.Equal(w.Canvas.ScreenSize.Width / 2.0, b.Left + b.Width / 2, 0.01);
        });
    }

    [Fact]
    public void Space_Out_Leaves_The_Same_Gap_Between_Each()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            var a = Bounds(w, "a");
            var c = Bounds(w, "c");
            Pick(w, "a", "b", "c");
            Assert.True(w.Arrange(MainWindow.Arrangement.Across) > 0);
            var b = Bounds(w, "b");
            Assert.Equal(b.Left - a.Right, c.Left - b.Right, 0.01);
            // The outermost two stay.
            Assert.Equal(a, Bounds(w, "a"));
            Assert.Equal(c, Bounds(w, "c"));
        });
    }

    [Fact]
    public void Match_Size_Gives_Each_The_Drawn_Size_Of_The_One_Picked_Last()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            Pick(w, "a", "c", "b");
            Assert.True(w.Arrange(MainWindow.Arrangement.Size) > 0);
            var b = Bounds(w, "b");
            foreach (var name in new[] { "a", "c" })
            {
                var r = Bounds(w, name);
                Assert.Equal(b.Width, r.Width, 1.0);
                Assert.Equal(b.Height, r.Height, 1.0);
            }
            // In each line's own unit.
            var rect = Named(w, "c").Children.First(l => l.Name == "BitmapRect");
            Assert.Equal("Viewport", rect.Arguments[4].Raw);
        });
    }

    [Fact]
    public void A_Drag_Snaps_To_Another_Elements_Edge_Unless_Alt_Is_Held()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            var a0 = Bounds(w, "a");
            var b0 = Bounds(w, "b");
            var reach = w.Canvas.SnapReach;
            w.SelectByName("a");
            w.Canvas.BeginDrag(Named(w, "a"));
            // Its left edge 2 pixels short of b's: nearer than any other edge or centre is to a line.
            Assert.True(reach > 2);
            var dx = b0.Left - a0.Left + 2;

            w.Canvas.DragBy(dx, 0, ModifierKeys.None);
            Assert.Equal(b0.Left, w.Canvas.Guides.X);
            Assert.Equal(b0.Left, Bounds(w, "a").Left, 0.6);

            w.Canvas.DragBy(dx, 0, ModifierKeys.Alt);
            Assert.Null(w.Canvas.Guides.X);
            Assert.Equal(a0.Left + dx, Bounds(w, "a").Left, 0.6);

            // Shift keeps to the axis moved along most.
            w.Canvas.DragBy(dx, 7, ModifierKeys.Shift | ModifierKeys.Alt);
            Assert.Equal(a0.Top, Bounds(w, "a").Top, 0.01);

            w.Canvas.EndDrag();
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(a0, Bounds(w, "a"));
        });
    }

    [Fact]
    public void Dragging_One_Of_Several_Picked_Moves_Them_All_As_One_Step()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            var a0 = Bounds(w, "a");
            var c0 = Bounds(w, "c");
            Pick(w, "a", "c");
            w.Canvas.BeginDrag(Named(w, "a"));
            w.Canvas.DragBy(37, 0, ModifierKeys.Alt);
            w.Canvas.EndDrag();
            Assert.Equal(a0.Left + 37, Bounds(w, "a").Left, 0.6);
            Assert.Equal(c0.Left + 37, Bounds(w, "c").Left, 0.6);

            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(a0, Bounds(w, "a"));
            Assert.Equal(c0, Bounds(w, "c"));
        });
    }
}
