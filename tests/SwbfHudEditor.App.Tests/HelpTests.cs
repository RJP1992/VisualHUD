using System.Windows;
using System.Windows.Documents;
using SwbfHudEditor.App.Views;

namespace SwbfHudEditor.App.Tests;

/// <summary>Help: the guide built into the editor and read as a document, and About with the version.</summary>
public class HelpTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        """;

    private static string TextOf(Block block) => new TextRange(block.ContentStart, block.ContentEnd).Text;

    [Fact]
    public void The_Guide_Is_Built_In_And_Opens_As_A_Document()
    {
        Assert.StartsWith("# VisualHUD guide", MainWindow.GuideText());
        UiThread.WithWindow(Hud, (w, _) =>
        {
            var guide = w.ShowGuide();
            try
            {
                var blocks = guide.Viewer.Document.Blocks.ToList();
                Assert.Equal("VisualHUD guide", TextOf(blocks[0]));
                Assert.Contains(blocks, b => TextOf(b) == "Part 2: How a HUD is put together");
                Assert.Contains(blocks, b => b is List);
                // Asked again, the same window comes forward.
                Assert.Same(guide, w.ShowGuide());
            }
            finally
            {
                guide.Close();
            }
        });
    }

    [Fact]
    public void Markdown_Turns_Into_Headings_Paragraphs_Lists_Code_And_Links()
    {
        const string markdown = """
            # Title

            Some `code`, **bold** and a [link](https://example.com), and a [file](README.md)
            carried on.

            - one
              carried on
              - inside
            - two

            1. first
            2. second

            ```
            Group("x")
            ```

            ---
            """;
        UiThread.WithWindow(Hud, (w, _) =>
        {
            var blocks = MarkdownFlow.Build(markdown, w).Blocks.ToList();
            Assert.Equal(6, blocks.Count);
            Assert.Equal("Title", TextOf(blocks[0]));
            var paragraph = (Paragraph)blocks[1];
            Assert.Equal("Some code, bold and a link, and a file carried on.", TextOf(paragraph));
            Assert.Single(paragraph.Inlines.OfType<Hyperlink>());
            Assert.Single(paragraph.Inlines.OfType<Bold>());

            var bullets = (List)blocks[2];
            Assert.Equal(TextMarkerStyle.Disc, bullets.MarkerStyle);
            Assert.Equal(2, bullets.ListItems.Count);
            var first = bullets.ListItems.First();
            Assert.EndsWith("one carried on", TextOf(first.Blocks.First()));   // after its bullet
            Assert.IsType<List>(first.Blocks.LastBlock);

            Assert.Equal(TextMarkerStyle.Decimal, ((List)blocks[3]).MarkerStyle);
            Assert.Equal("Group(\"x\")", TextOf(blocks[4]));
        });
    }

    [Fact]
    public void About_Gives_The_Version()
    {
        UiThread.WithWindow(Hud, (_, _) =>
        {
            var about = new AboutDialog();
            Assert.Equal($"Version {AboutDialog.Version}", about.VersionText.Text);
            Assert.Matches(@"^\d+\.\d+\.\d+$", AboutDialog.Version);
            about.Close();
        });
    }
}
