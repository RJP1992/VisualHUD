using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App.Tests;

/// <summary>
/// What the editor writes is never in pixels, which stay the same number at every resolution: a fitted Rect keeps
/// what it holds in place at 1080p as at 720p, and a move, a nudge, a resize or a copy writes in Viewport, in the
/// change's own undo step.
/// </summary>
public class NoPixelsTests
{
    // A team bar before its Rect was fitted: its groups at their parent's origin, its bitmap sized in Viewport.
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
            AuthoredRatio(16, 9)
        }

        Group("teams")
        {
            Viewport(1)
            Position(0.5, 0.075, 0, "Viewport")

            Group("background")
            {
                Bitmap("bar")
                {
                    Bitmap("square")
                    BitmapRect(0.4, 0.15, "Center", "Center", "Viewport")
                }
            }
        }

        Bitmap("old")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(32, 32, "Left", "Top", "Pixels")
            Position(100, 200, 0)
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

    private static string Line(MainWindow w, string element, string name) =>
        HudWriter.Header(Named(w, element).Children.Last(c => !c.HasBody && c.Name == name));

    /// <summary>Where an element is drawn, in shares of the screen.</summary>
    private static Rect Share(MainWindow w, string name)
    {
        var r = w.Canvas.ScreenBoundsOf(Named(w, name))!.Value;
        var s = w.Canvas.ScreenSize;
        return new Rect(r.X / s.Width, r.Y / s.Height, r.Width / s.Width, r.Height / s.Height);
    }

    private static void AssertClose(Rect expected, Rect actual)
    {
        Assert.True(Math.Abs(expected.Left - actual.Left) < 1e-3 && Math.Abs(expected.Top - actual.Top) < 1e-3 &&
                    Math.Abs(expected.Width - actual.Width) < 1e-3 && Math.Abs(expected.Height - actual.Height) < 1e-3,
            $"{expected} became {actual}");
    }

    private static void Screen(MainWindow w, int width, int height) =>
        w.Canvas.Configure(HudTarget.GameExt, new HudScreen(width, height), null, 1, true, true);

    [Fact]
    public void A_Fitted_Rect_Keeps_What_It_Holds_In_Place_At_Every_Resolution()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            Screen(w, 1920, 1080);
            var at1080 = Share(w, "bar");
            Screen(w, 1280, 720);
            var at720 = Share(w, "bar");

            Assert.True(w.FitRect(Named(w, "teams")));
            AssertClose(at720, Share(w, "bar"));
            // What keeps the bar where it was is written in Viewport, not pixels...
            Assert.EndsWith("\"Viewport\")", Line(w, "background", "Position"));
            // ...so at another resolution it is where it was too.
            Screen(w, 1920, 1080);
            AssertClose(at1080, Share(w, "bar"));
        });
    }

    [Fact]
    public void A_Move_A_Nudge_And_A_Resize_Write_Pixels_In_Viewport_In_Their_Own_Undo_Step()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            const string position = "Position(100, 200, 0)";
            const string size = "BitmapRect(32, 32, \"Left\", \"Top\", \"Pixels\")";
            var start = w.Canvas.ScreenBoundsOf(Named(w, "old"))!.Value;

            // A drag.
            w.SelectByName("old");
            w.Canvas.BeginDrag(Named(w, "old"));
            w.Canvas.DragBy(10, 0, ModifierKeys.Alt);
            w.Canvas.EndDrag();
            Assert.Equal(start.Left + 10, w.Canvas.ScreenBoundsOf(Named(w, "old"))!.Value.Left, 0.01);
            Assert.EndsWith("\"Viewport\")", Line(w, "old", "Position"));
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(position, Line(w, "old", "Position"));

            // A run of nudges: one undo step, the unit with it.
            w.Nudge(1, 0);
            w.Nudge(1, 0);
            Assert.Equal(start.Left + 2, w.Canvas.ScreenBoundsOf(Named(w, "old"))!.Value.Left, 0.01);
            Assert.EndsWith("\"Viewport\")", Line(w, "old", "Position"));
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(position, Line(w, "old", "Position"));

            // A resize by the right edge: its size in Viewport; its Position, which did not change, as it was.
            Assert.True(w.Canvas.BeginResize(Named(w, "old"), ResizeHandle.Right));
            w.Canvas.ResizeTo(new Point(start.Right + 20, start.Top + start.Height / 2), ModifierKeys.Alt);
            w.Canvas.EndResize();
            Assert.Equal(start.Width + 20, w.Canvas.ScreenBoundsOf(Named(w, "old"))!.Value.Width, 0.6);
            Assert.EndsWith("\"Viewport\")", Line(w, "old", "BitmapRect"));
            Assert.Equal(position, Line(w, "old", "Position"));
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(size, Line(w, "old", "BitmapRect"));
        });
    }

    [Fact]
    public void Copies_In_A_Row_Are_Placed_In_Viewport_And_The_Original_Is_Left_As_It_Is()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            var start = w.Canvas.ScreenBoundsOf(Named(w, "old"))!.Value;
            w.SelectByName("old");
            Assert.Equal(2, w.RepeatSelection(2, 50, 0));
            Assert.Equal("Position(100, 200, 0)", Line(w, "old", "Position"));
            foreach (var (name, n) in new[] { ("old_1", 1), ("old_2", 2) })
            {
                Assert.EndsWith("\"Viewport\")", Line(w, name, "Position"));
                Assert.Equal(start.Left + 50 * n, w.Canvas.ScreenBoundsOf(Named(w, name))!.Value.Left, 0.01);
            }
        });
    }
}
