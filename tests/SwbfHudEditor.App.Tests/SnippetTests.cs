using System.IO;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>Blocks saved under a name and inserted again, renamed where their names are taken.</summary>
public class SnippetTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Group("holder")
        {
            Viewport(1)

            Bitmap("icon")
            {
                BitmapRect(32, 32, "Left", "Top", "Pixels")
                Position(10, 20, 0)
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

    [Fact]
    public void A_Saved_Snippet_Inserts_Into_Any_HUD_With_Names_Of_Its_Own()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            // The tests keep their snippets beside their own settings, never the user's.
            Assert.StartsWith(Path.GetTempPath(), MainWindow.SnippetFolder, StringComparison.OrdinalIgnoreCase);
            w.SelectByName("icon");
            var path = w.SaveSnippet("my icon")!;
            Assert.True(File.Exists(path));
            Assert.Contains("my icon", MainWindow.SnippetNames());
            Assert.Null(w.SaveSnippet("bad/name"));

            w.SelectByName("holder");
            Assert.True(w.InsertSnippet("my icon"));
            var holder = All(w.Files.SelectMany(f => f.Document.TopLevel)).First(b => b.ItemName == "holder");
            Assert.Equal(new[] { "icon", "icon_1" }, holder.Children.Where(c => c.HasBody).Select(c => c.ItemName));
            File.Delete(path);
        });
    }
}
