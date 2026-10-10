using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>Copies in a row: each a step further on the screen, named on, one undo step.</summary>
public class RepeatTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Group("row")
        {
            Viewport(1)
            Position(0.1, 0.2, 0, "Viewport")
            Scale(2, 2, 1)

            Bitmap("slot")
            {
                Bitmap("square")
                BitmapRect(20, 20, "Left", "Top", "Pixels")
                Position(5, 5, 0)
            }
        }

        """;

    private static IEnumerable<HudBlock> All(IEnumerable<HudBlock> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            foreach (var inner in All(b.Children)) yield return inner;
        }
    }

    private static HudBlock Named(MainWindow w, string name) =>
        All(w.Files.SelectMany(f => f.Document.TopLevel)).First(b => b.HasBody && b.ItemName == name);

    [Fact]
    public void Each_Copy_Is_A_Step_Further_On_The_Screen()
    {
        UiThread.WithWindow(Hud, new Dictionary<string, byte[]> { ["square.tga"] = TestTextures.Tga(4, 4, 255, 255, 255, 255) }, (w, _) =>
        {
            var slot = w.Canvas.ScreenBoundsOf(Named(w, "slot"))!.Value;
            w.SelectByName("slot");
            Assert.Equal(3, w.RepeatSelection(3, 50, 10));
            var row = Named(w, "row").Children.Where(c => c.HasBody).Select(c => c.ItemName).ToList();
            Assert.Equal(new[] { "slot", "slot_1", "slot_2", "slot_3" }, row);
            for (var n = 1; n <= 3; n++)
            {
                var copy = w.Canvas.ScreenBoundsOf(Named(w, $"slot_{n}"))!.Value;
                Assert.Equal(slot.Left + 50 * n, copy.Left, 0.6);
                Assert.Equal(slot.Top + 10 * n, copy.Top, 0.6);
            }

            ApplicationCommands.Undo.Execute(null, w);
            Assert.Single(Named(w, "row").Children, c => c.HasBody);
        });
    }
}
