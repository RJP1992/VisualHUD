using System.IO;
using System.Windows.Media.Imaging;

namespace SwbfHudEditor.App.Tests;

/// <summary>The HUD exported as a PNG: the screen's size, without the guides and the selection, the canvas as it was after.</summary>
public class ExportTests
{
    // Laid out by GameExt on the wider canvas: the guides are its thirds, at x 320, 480 and 800 of 1280.
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
        }

        """;

    private static byte[] Pixel(BitmapSource image, int x, int y)
    {
        var bytes = new byte[4];
        new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), bytes, 4, 0);
        return bytes;
    }

    [Fact]
    public void The_Export_Is_The_HUD_Alone_And_The_Canvas_Is_As_It_Was_After()
    {
        UiThread.WithWindow(Hud, new Dictionary<string, byte[]> { ["square.tga"] = TestTextures.Tga(4, 4, 255, 255, 255, 255) }, (w, folder) =>
        {
            var screen = w.Canvas.ScreenSize;
            Assert.Equal((1280, 720), (screen.Width, screen.Height));
            w.SelectByName("icon");
            Assert.NotEmpty(w.Canvas.GuideLines);

            var path = Path.Combine(Path.GetDirectoryName(folder)!, "export.png");
            w.ExportImage(path);
            var image = BitmapFrame.Create(new MemoryStream(File.ReadAllBytes(path)));
            Assert.Equal(1280, image.PixelWidth);
            Assert.Equal(720, image.PixelHeight);

            // Where a guide's line and band would be: the background alone.
            var background = Pixel(image, 10, 700);
            Assert.Equal(background, Pixel(image, 480, 360));
            Assert.Equal(background, Pixel(image, 400, 360));
            // The bitmap is there.
            Assert.NotEqual(background, Pixel(image, 116, 116));

            // The canvas draws its guides and the selection again.
            Assert.NotEmpty(w.Canvas.GuideLines);
            Assert.True(w.Canvas.GuidesBox.IsChecked);
        });
    }
}
