using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>Resizing by a handle: the edges not dragged stay, a group's elements stay, Shift keeps the shape, one undo step.</summary>
public class ResizeTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Bitmap("centred")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(60, 40, "Center", "Center", "Pixels")
            Position(300, 300, 0)
        }

        Group("frame")
        {
            Viewport(1)
            Position(600, 200, 0)
            Rect(200, 100, "Left", "Top", "Pixels")

            Bitmap("held")
            {
                Bitmap("square")
                BitmapRect(20, 20, "Left", "Top", "Pixels")
                Position(30, 30, 0)
            }
        }

        Text("label")
        {
            Viewport(1)
            Text("label")
            TextBox(0.2, 0.05)
            Position(100, 500, 0)
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

    private static void Resize(MainWindow w, string name, ResizeHandle handle, Point to, ModifierKeys keys = ModifierKeys.Alt)
    {
        Assert.True(w.Canvas.BeginResize(Named(w, name), handle));
        w.Canvas.ResizeTo(to, keys);
        w.Canvas.EndResize();
    }

    [Fact]
    public void A_Bitmap_Resized_By_An_Edge_Keeps_The_Others_And_Undoes_As_One()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            w.SelectByName("centred");
            Assert.Equal(8, w.Canvas.HandleCount);
            var before = Drawn(w, "centred");
            Resize(w, "centred", ResizeHandle.Right, new Point(before.Right + 40, before.Top + before.Height / 2));
            var after = Drawn(w, "centred");
            Assert.Equal(before.Left, after.Left, 0.6);
            Assert.Equal(before.Right + 40, after.Right, 0.6);
            Assert.Equal(before.Top, after.Top, 0.6);
            Assert.Equal(before.Bottom, after.Bottom, 0.6);
            // Written in Viewport, a share of the view's 1280 pixels; its Position, moved with its middle, too.
            var size = Named(w, "centred").Children.Single(c => c.Name == "BitmapRect");
            Assert.Equal("BitmapRect(0.078125, 0.055556, \"Center\", \"Center\", \"Viewport\")", HudWriter.Header(size));
            Assert.Equal("Viewport", Named(w, "centred").Children.Single(c => c.Name == "Position").Arguments[3].Raw);

            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(before, Drawn(w, "centred"));
            Assert.Equal("BitmapRect(60, 40, \"Center\", \"Center\", \"Pixels\")", HudWriter.Header(size));
            Assert.Equal("Position(300, 300, 0)", HudWriter.Header(Named(w, "centred").Children.Single(c => c.Name == "Position")));
        });
    }

    [Fact]
    public void A_Groups_Rect_Resized_From_Its_Corner_Leaves_What_It_Holds_In_Place()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            w.SelectByName("frame");
            var frame = Drawn(w, "frame");
            var held = Drawn(w, "held");
            Resize(w, "frame", ResizeHandle.TopLeft, new Point(frame.Left - 50, frame.Top - 20));
            var after = Drawn(w, "frame");
            Assert.Equal(frame.Left - 50, after.Left, 0.6);
            Assert.Equal(frame.Top - 20, after.Top, 0.6);
            Assert.Equal(frame.Right, after.Right, 0.6);
            Assert.Equal(frame.Bottom, after.Bottom, 0.6);
            var nowHeld = Drawn(w, "held");
            Assert.Equal(held.Left, nowHeld.Left, 0.6);
            Assert.Equal(held.Top, nowHeld.Top, 0.6);
        });
    }

    [Fact]
    public void Shift_Keeps_The_Shape_And_A_TextBox_Resizes_Too()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            w.SelectByName("centred");
            var before = Drawn(w, "centred");
            Resize(w, "centred", ResizeHandle.BottomRight, new Point(before.Right + 60, before.Bottom + 5), ModifierKeys.Shift | ModifierKeys.Alt);
            var after = Drawn(w, "centred");
            Assert.Equal(before.Width / before.Height, after.Width / after.Height, 2);
            Assert.True(after.Width > before.Width + 50);

            w.SelectByName("label");
            var box = Drawn(w, "label");
            Resize(w, "label", ResizeHandle.Bottom, new Point(box.Left + box.Width / 2, box.Bottom + 36));
            var grown = Drawn(w, "label");
            Assert.Equal(box.Top, grown.Top, 0.6);
            Assert.True(grown.Height > box.Height + 25, $"{box} became {grown}");
        });
    }
}
