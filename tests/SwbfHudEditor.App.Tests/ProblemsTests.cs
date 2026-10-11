using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Problems;

namespace SwbfHudEditor.App.Tests;

/// <summary>The Problems tab: warnings shown, notes on request, a pick shows where, and a fix clears it.</summary>
public class ProblemsTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Group("holder")
        {
            Viewport(1)
            BlendMode("Additive")

            Bitmap("icon")
            {
                Position(10, 20, 0, "Viewpor")
                BitmapRect(0.025, 0.044444, "Left", "Top", "Viewport")
            }

            Model3D("shape")
            {
                BlendMode("Additive")
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
    public void The_Tab_Lists_What_The_Game_Reads_Otherwise_And_Shows_Where()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.ShowLeftTab("Problems");
            UiThread.Settle();
            // The unit that names nothing, and the group's BlendMode before its children; the model's BlendMode is a note.
            var shown = w.ProblemList.Shown;
            Assert.Equal(2, shown.Count(r => r.Problem.Level == ProblemLevel.Warning));
            Assert.All(shown, r => Assert.NotEqual(ProblemLevel.Note, r.Problem.Level));
            Assert.Contains("1 note hidden", w.ProblemList.SummaryText.Text);
            w.ProblemList.NotesBox.IsChecked = true;
            Assert.Single(w.ProblemList.Shown, r => r.Problem.Level == ProblemLevel.Note);
            shown = w.ProblemList.Shown;
            var unit = shown.Single(r => r.Problem.Line?.Name == "Position");
            Assert.StartsWith("Position(10, 20, 0, \"Viewpor\")", unit.Line);

            // A pick shows the element, with the tab kept in front.
            w.ProblemList.ProblemList.SelectedItem = unit;
            UiThread.Settle();
            Assert.Same(Named(w, "icon"), w.Tree.SelectedItem);
            Assert.True(w.ProblemsTab.IsSelected);

            // Fixed, it goes.
            w.FindList.FindBox.Text = "Viewpor";
            w.FindList.WholeValueBox.IsChecked = true;
            w.FindList.ReplaceBox.Text = "Viewport";
            Assert.Equal(1, w.ReplaceAll());
            UiThread.Settle();
            Assert.DoesNotContain(w.ProblemList.Shown, r => r.Problem.Line?.Name == "Position");
        });
    }
}
