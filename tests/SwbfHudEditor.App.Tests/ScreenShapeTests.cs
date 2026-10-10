using System.IO;
using System.Numerics;
using System.Windows.Controls;
using System.Windows.Input;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>A file rewritten for a screen shape draws as before there, in one undo step; the dialog offers the shapes.</summary>
public class ScreenShapeTests
{
    // A stock file: squashed into the band, its bitmaps stretched, at 16:9.
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Group("group")
        {
            Viewport(1)
            Position(0.5, 0.5, 0, "Viewport")
            Rect(0.25, 0.2, "Center", "Center", "Viewport")

            Bitmap("icon")
            {
                Position(10, 20, 0)
                BitmapRect(32, 32, "Left", "Top", "Pixels")
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

    private static Vector2[] Drawn(MainWindow w, string name)
    {
        var e = w.Canvas.LaidOut(Named(w, name))!;
        var q = e.FullQuad ?? e.Quad ?? new Core.Layout.QuadRect(0, 0, e.RectWidthPixels, e.RectHeightPixels);
        return new[] { Vector2.Zero, new Vector2(q.Left, q.Top), new Vector2(q.Right, q.Bottom) }
            .Select(p => Vector2.Transform(p, e.ToScreen)).ToArray();
    }

    [Fact]
    public void Rewriting_For_The_Canvas_Shape_Changes_Nothing_On_The_Canvas_And_Undoes_As_One()
    {
        UiThread.WithWindow(Hud, (w, path) =>
        {
            var screen = w.Canvas.ScreenSize;
            Assert.Equal(16 * screen.Height, 9 * screen.Width);
            var file = w.Files.Single(f => f.FilePath == path);
            var original = HudWriter.Write(file.Document);
            var group = Drawn(w, "group");
            var icon = Drawn(w, "icon");

            var result = w.RewriteForShape(file, 16, 9)!;
            Assert.Equal(screen, result.Reference);
            Assert.True(result.Lines >= 4);
            for (var i = 0; i < 3; i++)
            {
                Assert.True(Vector2.Distance(group[i], Drawn(w, "group")[i]) < 0.6f, $"group point {i}");
                Assert.True(Vector2.Distance(icon[i], Drawn(w, "icon")[i]) < 0.6f, $"icon point {i}");
            }
            Assert.Contains("AuthoredRatio(16, 9)", HudWriter.Write(file.Document));
            Assert.True(file.IsModified);

            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(original, HudWriter.Write(file.Document));
            Assert.False(file.IsModified);
        });
    }

    [Fact]
    public void The_Dialog_Starts_On_The_Files_Shape_And_Says_Where_It_Keeps_The_Look()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            var dialog = new ScreenShapeDialog("test.hud", 1080, (64, 27));
            try
            {
                var picked = dialog.ShapeList.Children.OfType<RadioButton>().Single(b => b.IsChecked == true);
                Assert.StartsWith("21:9, 2560", (string)picked.Content);
                Assert.Contains("2560×1080", dialog.ReferenceText.Text);

                // A shape typed that AuthoredRatio does not take cannot be chosen.
                dialog.OtherWidth.Text = "9";
                dialog.OtherHeight.Text = "16";
                Assert.False(dialog.RewriteButton.IsEnabled);
                dialog.OtherWidth.Text = "18";
                dialog.OtherHeight.Text = "9";
                Assert.True(dialog.RewriteButton.IsEnabled);
                Assert.Contains("2160×1080", dialog.ReferenceText.Text);
            }
            finally { dialog.Close(); }
        });
    }
}
