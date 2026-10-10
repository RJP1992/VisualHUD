using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>The canvas keeps what did not change while values change, and draws what did.</summary>
public class CanvasTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Text("ammo")
        {
            Viewport(1)
            EventNumber("player1.weapon1.ammo")
            Position(0.5, 0.5, 0, "Viewport")
        }

        Text("label")
        {
            Viewport(1)
            Text("label")
            Position(0.5, 0.6, 0, "Viewport")
        }

        """;

    private static HudBlock Named(MainWindow w, string name) =>
        w.Files.SelectMany(f => f.Document.TopLevel).First(b => b.HasBody && b.ItemName == name);

    [Fact]
    public void A_Send_Or_An_Edit_Redraws_Only_What_It_Changed()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.EventState.Send("player1.weapon1.ammo", HudEventValue.FromInt(10));
            UiThread.Settle();
            var ammo = w.Canvas.VisualOf(Named(w, "ammo"));
            var label = w.Canvas.VisualOf(Named(w, "label"));
            Assert.NotNull(ammo);
            Assert.NotNull(label);

            w.EventState.Send("player1.weapon1.ammo", HudEventValue.FromInt(9));
            UiThread.Settle();
            Assert.NotSame(ammo, w.Canvas.VisualOf(Named(w, "ammo")));
            Assert.Same(label, w.Canvas.VisualOf(Named(w, "label")));

            // The clock moving changes nothing here: both keep their visuals.
            ammo = w.Canvas.VisualOf(Named(w, "ammo"));
            w.EventState.SetTime(1.0);
            UiThread.Settle();
            Assert.Same(ammo, w.Canvas.VisualOf(Named(w, "ammo")));

            // A refresh with nothing changed keeps it; an edit to its own lines draws it again.
            w.Canvas.Refresh();
            Assert.Same(label, w.Canvas.VisualOf(Named(w, "label")));
            var text = Named(w, "label").Children.First(c => c.Name == "Text");
            text.Arguments[0] = HudArgument.FromString("other");
            w.Canvas.Refresh();
            Assert.NotSame(label, w.Canvas.VisualOf(Named(w, "label")));
        });
    }

    [Fact]
    public void A_Wipe_Shows_The_Reference_Left_Of_Its_Divider()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            var pixels = new byte[64 * 36 * 4];
            w.Canvas.ReferenceImage = BitmapSource.Create(64, 36, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
            w.Canvas.ReferenceShown = true;
            w.Canvas.ReferenceWipeBox.IsChecked = true;
            UiThread.Settle();
            var surface = w.Canvas.CanvasSurface;
            var image = surface.Children.OfType<Image>().Single();
            var width = image.Width;
            Assert.Equal(new Rect(0, 0, width / 2, image.Height), ((RectangleGeometry)image.Clip).Rect);
            var divider = surface.Children.OfType<Grid>().Single(g => g.Cursor == System.Windows.Input.Cursors.SizeWE);
            Assert.Equal(width / 2 - divider.Width / 2, Canvas.GetLeft(divider));

            w.Canvas.ReferenceWipeBox.IsChecked = false;
            UiThread.Settle();
            Assert.Null(surface.Children.OfType<Image>().Single().Clip);
        });
    }
}
