using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App.Tests;

/// <summary>Additive bitmaps add, and the Shadow style draws the game's black copy under a bitmap or a text.</summary>
public class BlendTests
{
    /// <summary>A 4 x 4 32-bit TGA, top row first, every pixel this colour.</summary>
    private static byte[] Tga(byte r, byte g, byte b, byte a)
    {
        var bytes = new byte[18 + 16 * 4];
        bytes[2] = 2;
        bytes[12] = 4;
        bytes[14] = 4;
        bytes[16] = 32;
        bytes[17] = 0x28;
        for (var i = 18; i < bytes.Length; i += 4)
        {
            bytes[i] = b;
            bytes[i + 1] = g;
            bytes[i + 2] = r;
            bytes[i + 3] = a;
        }
        return bytes;
    }

    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Bitmap("glow")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(64, 64, "Left", "Top", "Pixels")
            Position(100, 100, 0)
            BlendMode("Additive")
            Color(200, 100, 50)
        }

        Bitmap("shaded")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(64, 64, "Left", "Top", "Pixels")
            Position(300, 100, 0)
            BitmapStyle("Shadow")
            Alpha(0.8)
        }

        Text("label")
        {
            Viewport(1)
            Text("label")
            TextStyle("Shadow")
            TextScale(2, 2)
            Position(100, 300, 0)
        }

        """;

    private static HudBlock Named(MainWindow w, string name) =>
        w.Files.SelectMany(f => f.Document.TopLevel).First(b => b.HasBody && b.ItemName == name);

    private static BitmapSource ImageOf(Rectangle rect) => (BitmapSource)((ImageBrush)rect.Fill).ImageSource;

    [Fact]
    public void An_Additive_Bitmap_Is_Its_Colour_With_No_Alpha_So_It_Adds()
    {
        UiThread.WithWindow(Hud, new Dictionary<string, byte[]> { ["square.tga"] = Tga(255, 255, 255, 128) }, (w, _) =>
        {
            var rect = Assert.IsType<Rectangle>(w.Canvas.VisualOf(Named(w, "glow")));
            var image = ImageOf(rect);
            Assert.Equal(PixelFormats.Pbgra32, image.Format);
            var pixel = new byte[4];
            image.CopyPixels(new System.Windows.Int32Rect(1, 1, 1, 1), pixel, 4, 0);
            // B, G, R premultiplied by the texture's alpha (128), and no alpha of its own.
            Assert.Equal(new byte[] { 50 * 128 / 255, 100 * 128 / 255, 200 * 128 / 255, 0 }, pixel);
        });
    }

    [Fact]
    public void A_Shadow_Bitmap_Draws_A_Black_Copy_At_Half_Its_Alpha_Down_And_Right_On_The_Screen()
    {
        UiThread.WithWindow(Hud, new Dictionary<string, byte[]> { ["square.tga"] = Tga(255, 255, 255, 255) }, (w, _) =>
        {
            var pair = Assert.IsType<Canvas>(w.Canvas.VisualOf(Named(w, "shaded")));
            var shadow = (Rectangle)pair.Children[0];
            var main = (Rectangle)pair.Children[1];
            Assert.Equal(0.8, main.Opacity, 3);
            Assert.Equal(Math.Floor(0.8 * 255) / 2 / 255, shadow.Opacity, 3);
            var black = new byte[4];
            ImageOf(shadow).CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), black, 4, 0);
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, black);

            // (2, -2) in clip space at w = 640 / t: W / 554.26 right, H / 554.26 down.
            var screen = w.Canvas.ScreenSize;
            var w0 = EngineMath.InterfaceWidth / EngineMath.FrustumWidth;
            var delta = shadow.RenderTransform.Value.Transform(new System.Windows.Point(0, 0)) -
                        main.RenderTransform.Value.Transform(new System.Windows.Point(0, 0));
            Assert.Equal(screen.Width / w0, delta.X, 3);
            Assert.Equal(screen.Height / w0, delta.Y, 3);
        });
    }

    [Fact]
    public void A_Shadow_Text_Puts_Its_Shadow_The_Same_Screen_Distance_Away_Whatever_Its_Scale()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            var text = Assert.IsType<TextGlyphsVisual>(w.Canvas.VisualOf(Named(w, "label")));
            var at = Assert.NotNull(text.ShadowAt);
            var screen = w.Canvas.ScreenSize;
            var w0 = EngineMath.InterfaceWidth / EngineMath.FrustumWidth;
            var onScreen = text.RenderTransform.Value.Transform(at) - text.RenderTransform.Value.Transform(new System.Windows.Vector(0, 0));
            Assert.Equal(screen.Width / w0, onScreen.X, 3);
            Assert.Equal(screen.Height / w0, onScreen.Y, 3);
        });
    }
}
