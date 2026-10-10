using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwbfHudEditor.App.Views;

namespace SwbfHudEditor.App.Tests;

/// <summary>The guides' lines show over a reference image, under the HUD or over it, on top or wiped.</summary>
public class GuideLayerTests
{
    // Laid out by GameExt on a wider screen: the guides are its thirds.
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
            AuthoredRatio(4, 3)
        }

        Bitmap("icon")
        {
            Viewport(1)
            BitmapRect(32, 32, "Left", "Top", "Pixels")
            Position(100, 100, 0)
        }

        """;

    [Fact]
    public void The_Thirds_Lie_Over_The_Reference_Image_Wherever_It_Is()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            var canvas = w.Canvas;
            canvas.ReferenceImage = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
            int ImageLayer() => Panel.GetZIndex(canvas.CanvasSurface.Children.OfType<Image>().Single());

            // Under the HUD: the lines over the image, under the HUD.
            canvas.ReferenceOnTop = false;
            Assert.NotEmpty(canvas.GuideLines);
            Assert.All(canvas.GuideLines, l => Assert.True(Panel.GetZIndex(l) > ImageLayer()));
            Assert.All(canvas.GuideLines, l => Assert.True(Panel.GetZIndex(l) < 0));

            // On top of the HUD: the lines over the image still.
            canvas.ReferenceOnTop = true;
            Assert.Equal(HudCanvas.ReferenceOverLayer, ImageLayer());
            Assert.All(canvas.GuideLines, l => Assert.True(Panel.GetZIndex(l) > ImageLayer()));

            // Wiped across it: the same.
            canvas.ReferenceOnTop = false;
            canvas.ReferenceWipeBox.IsChecked = true;
            Assert.All(canvas.GuideLines, l => Assert.True(Panel.GetZIndex(l) > ImageLayer()));
        });
    }
}
