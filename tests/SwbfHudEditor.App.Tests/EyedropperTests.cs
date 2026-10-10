using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>A colour taken off the canvas: the reference image's where it shows, else the HUD's as drawn; one undo puts it back.</summary>
public class EyedropperTests
{
    // Laid out by GameExt on the wider canvas, as the export's test is: the square is at 100 to 132.
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
            AuthoredRatio(4, 3)
        }

        Bitmap("icon")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(32, 32, "Left", "Top", "Pixels")
            Position(100, 100, 0)
            Color(255, 255, 255, 255)
        }

        """;

    private static readonly Color Left = Color.FromRgb(10, 20, 30);
    private static readonly Color Right = Color.FromRgb(200, 100, 50);

    /// <summary>Two pixels side by side, stretched across the screen: one colour left of the middle, another right of it.</summary>
    private static BitmapSource Reference()
    {
        var image = new WriteableBitmap(2, 1, 96, 96, PixelFormats.Bgra32, null);
        image.WritePixels(new Int32Rect(0, 0, 2, 1), new byte[] { Left.B, Left.G, Left.R, 255, Right.B, Right.G, Right.R, 255 }, 8, 0);
        return image;
    }

    private static HudBlock ColorLine(MainWindow w) =>
        w.Files.SelectMany(f => f.Document.TopLevel).First(b => b.ItemName == "icon").Children.Single(c => c.Name == "Color");

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    private static Color Pixel(BitmapSource image, int x, int y)
    {
        var bytes = new byte[4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0);
        return Color.FromRgb(bytes[2], bytes[1], bytes[0]);
    }

    [Fact]
    public void The_Panels_Eyedropper_Takes_The_Reference_Images_Colour_In_One_Undo()
    {
        UiThread.WithWindow(Hud, new Dictionary<string, byte[]> { ["square.tga"] = TestTextures.Tga(4, 4, 255, 255, 255, 255) }, (w, _) =>
        {
            w.Canvas.ReferenceImage = Reference();
            w.SelectByName("icon");
            w.Properties.FlushRows();
            UiThread.Settle();
            var dropper = Descendants<ButtonBase>(w.Properties)
                .Single(b => b.ToolTip is string t && t.StartsWith("Take the colour off the canvas", StringComparison.Ordinal));

            dropper.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, dropper));
            Assert.True(w.Canvas.PickingColor);
            Assert.Equal(Left, w.Canvas.PickColorAt(new Point(100, 360)));
            Assert.False(w.Canvas.PickingColor);
            Assert.Equal("Color(10, 20, 30, 255)", HudWriter.Header(ColorLine(w)));

            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal("Color(255, 255, 255, 255)", HudWriter.Header(ColorLine(w)));
        });
    }

    [Fact]
    public void Where_The_Reference_Does_Not_Show_The_HUDs_Own_Colour_Is_Taken()
    {
        UiThread.WithWindow(Hud, new Dictionary<string, byte[]> { ["square.tga"] = TestTextures.Tga(4, 4, 255, 255, 255, 255) }, (w, _) =>
        {
            var canvas = w.Canvas;
            canvas.ReferenceImage = Reference();
            Color? picked = null;

            // Wiped across the screen: the reference left of the divider, the HUD right of it.
            canvas.ReferenceWipeBox.IsChecked = true;
            canvas.BeginPickColor(c => picked = c);
            Assert.Equal(Left, canvas.PickColorAt(new Point(100, 360)));
            canvas.BeginPickColor(c => picked = c);
            var hud = canvas.PickColorAt(new Point(1000, 360));
            Assert.Equal(Pixel(canvas.RenderForExport(), 1000, 360), hud);
            Assert.NotEqual(Right, hud);
            Assert.Equal(hud, picked);

            // The reference hidden: the white square as drawn.
            canvas.ReferenceWipeBox.IsChecked = false;
            canvas.ReferenceBox.IsChecked = false;
            canvas.BeginPickColor(c => picked = c);
            Assert.Equal(Colors.White, canvas.PickColorAt(new Point(116, 116)));

            // Esc lets it go: the next click is the canvas's own again.
            canvas.BeginPickColor(c => picked = c);
            var source = PresentationSource.FromVisual(canvas)!;
            canvas.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.False(canvas.PickingColor);
            Assert.Null(canvas.PickColorAt(new Point(116, 116)));
        });
    }
}
