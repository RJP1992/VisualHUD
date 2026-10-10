using System.Windows.Input;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>The Find tab: matches listed with what they become, unticked ones kept, one undo step for a replace.</summary>
public class FindTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Group("player1weapon")
        {
            Viewport(1)
            EventEnable("player1.weapon1.ammo")
            Position(10, 20, 0)

            Text("player1ammo")
            {
                EventText("player1.weapon1.ammo")
                Position(10, 0, 0)
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

    private static HudBlock LineOf(HudBlock block, string name) => block.Children.First(c => !c.HasBody && c.Name == name);

    private static void Find(MainWindow w, string text, string replacement)
    {
        ApplicationCommands.Find.Execute(null, w);
        w.FindList.FindBox.Text = text;
        w.FindList.ReplaceBox.Text = replacement;
        UiThread.Settle();
    }

    [Fact]
    public void Replace_Changes_The_Ticked_Matches_In_One_Undo_Step()
    {
        UiThread.WithWindow(Hud, (w, path) =>
        {
            Find(w, "player1", "player2");
            Assert.True(w.FindTab.IsSelected);
            var rows = w.FindList.Rows;
            Assert.Equal(new[] { "player1weapon", "player1.weapon1.ammo", "player1ammo", "player1.weapon1.ammo" }, rows.Select(r => r.Match.Value.Raw));
            Assert.Equal("→ \"player2.weapon1.ammo\"", rows[1].After);
            Assert.Equal("EventEnable(\"player1.weapon1.ammo\")", string.Concat(rows[1].Segments.Select(s => s.Text)));
            Assert.Equal(new[] { "player1" }, rows[1].Segments.Where(s => s.Marked).Select(s => s.Text));
            Assert.StartsWith("Found in 4 arguments, in 1 file.", w.FindList.SummaryText.Text);
            Assert.Equal("Replace 4", w.FindList.ReplaceButton.Content);

            // Unticked: left out, and kept unticked as the replacement is typed.
            rows[2].Include = false;
            Assert.Equal("Replace 3", w.FindList.ReplaceButton.Content);
            w.FindList.ReplaceBox.Text = "player3";
            Assert.False(w.FindList.Rows[2].Include);

            Assert.Equal(3, w.ReplaceAll());
            Assert.Equal("player3weapon", Named(w, "player3weapon").ItemName);
            Assert.Equal("player1ammo", Named(w, "player1ammo").ItemName);
            Assert.Equal("player3.weapon1.ammo", LineOf(Named(w, "player1ammo"), "EventText").Arguments[0].Raw);
            Assert.True(w.Files.Single(f => f.FilePath == path).IsModified);
            // What is left to find: the unticked one.
            Assert.Single(w.FindList.Rows);

            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal("player1.weapon1.ammo", LineOf(Named(w, "player1weapon"), "EventEnable").Arguments[0].Raw);
            Assert.False(w.Files.Single(f => f.FilePath == path).IsModified);
            Assert.Equal(4, w.FindList.Rows.Count);
        });
    }

    [Fact]
    public void Numbers_Are_Found_With_Numbers_And_Words_And_Stay_Numbers()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            Find(w, "10", "x");
            Assert.Empty(w.FindList.Rows);
            w.FindList.AllValuesBox.IsChecked = true;
            UiThread.Settle();
            Assert.Equal(2, w.FindList.Rows.Count);
            Assert.All(w.FindList.Rows, r => Assert.True(r.Refused));
            Assert.Contains("2 would stay as they are", w.FindList.SummaryText.Text);
            Assert.False(w.FindList.ReplaceButton.IsEnabled);

            w.FindList.WholeValueBox.IsChecked = true;
            w.FindList.ReplaceBox.Text = "12.5";
            Assert.Equal(2, w.ReplaceAll());
            var position = LineOf(Named(w, "player1ammo"), "Position");
            Assert.Equal(new HudArgument(HudArgKind.Float, "12.5"), position.Arguments[0]);
        });
    }

    [Fact]
    public void Picking_A_Match_Selects_Its_Element()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            Find(w, "weapon1.ammo", "");
            w.FindList.MatchList.SelectedItem = w.FindList.Rows[1];
            UiThread.Settle();
            Assert.Same(Named(w, "player1ammo"), w.Tree.SelectedItem);
            Assert.True(w.FindTab.IsSelected);

            // So does a pick made on the canvas with the Events tab in front.
            w.ShowLeftTab("Events");
            Assert.True(w.SelectByName("player1weapon"));
            Assert.Same(Named(w, "player1weapon"), w.Tree.SelectedItem);
            Assert.False(w.FindTab.IsSelected);
            Assert.False(w.ElementsTab.IsSelected);
        });
    }
}
