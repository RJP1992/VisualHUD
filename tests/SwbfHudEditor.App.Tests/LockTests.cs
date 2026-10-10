using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>A locked element, or one in a locked group or file, is not picked on the canvas: what is under it is.</summary>
public class LockTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Bitmap("backdrop")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(400, 300, "Left", "Top", "Pixels")
            Position(100, 100, 0)
        }

        Group("cover")
        {
            Viewport(1)
            ZOrder(10)

            Bitmap("over")
            {
                Bitmap("square")
                BitmapRect(400, 300, "Left", "Top", "Pixels")
                Position(100, 100, 0)
            }
        }

        """;

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

    [Fact]
    public void A_Locked_Element_Is_Not_Picked_And_What_Is_Under_It_Is()
    {
        UiThread.WithWindow(Hud, new Dictionary<string, byte[]> { ["square.tga"] = TestTextures.Tga(4, 4, 255, 255, 255, 255) }, (w, path) =>
        {
            var at = w.Canvas.ScreenBoundsOf(Named(w, "backdrop"))!.Value;
            var middle = new Point(at.Left + at.Width / 2, at.Top + at.Height / 2);
            Assert.Equal(new[] { "over", "backdrop" }, w.Canvas.BlocksAt(middle).Select(b => b.ItemName));

            // Locked by its group.
            Named(w, "cover").IsLocked = true;
            Assert.Equal(new[] { "backdrop" }, w.Canvas.BlocksAt(middle).Select(b => b.ItemName));

            // L on the selection unlocks it, as all of it is locked; Alt+L unlocks everything.
            w.SelectByName("cover");
            MainWindow.ToggleLockCommand.Execute(null, w);
            Assert.False(Named(w, "cover").IsLocked);
            Named(w, "backdrop").IsLocked = true;
            Named(w, "over").IsLocked = true;
            Assert.Empty(w.Canvas.BlocksAt(middle));
            MainWindow.UnlockAllCommand.Execute(null, w);
            Assert.Equal(2, w.Canvas.BlocksAt(middle).Count);

            // A locked file locks all of its elements.
            w.Files.Single(f => f.FilePath == path).IsLocked = true;
            Assert.Empty(w.Canvas.BlocksAt(middle));
            // Locking is the editor's: the file is not changed.
            Assert.False(w.Files.Single(f => f.FilePath == path).IsModified);
        });
    }
}
