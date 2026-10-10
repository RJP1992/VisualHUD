using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>Several elements picked: the panel shows the lines they share, blank where they differ, and edits all.</summary>
public class ManyTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Bitmap("one")
        {
            Viewport(1)
            Alpha(0.5)
            ZOrder(4)
            Position(10, 20, 0)
        }

        Bitmap("two")
        {
            Viewport(1)
            Alpha(0.8)
            Position(10, 60, 0)
        }

        """;

    private static HudBlock Named(MainWindow w, string name) =>
        w.Files.SelectMany(f => f.Document.TopLevel).First(b => b.HasBody && b.ItemName == name);

    private static HudBlock Line(MainWindow w, string element, string name) => Named(w, element).Children.Last(c => !c.HasBody && c.Name == name);

    private static void Commit(TextBox box, string text)
    {
        box.Text = text;
        box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, box));
        UiThread.Settle();
    }

    [Fact]
    public void The_Lines_They_Share_Show_Blank_Where_They_Differ_And_Edit_All_In_One_Step()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.SelectByName("one");
            w.TogglePick(Named(w, "two"));
            w.Properties.FlushRows();
            Assert.NotNull(w.Properties.Many);

            var shown = w.Properties.ShownLines;
            // ZOrder is only the first's.
            Assert.Equal(new[] { "Viewport", "Alpha", "Position" }, shown.Select(l => l.Name));
            var alpha = shown.Single(l => l.Name == "Alpha");
            Assert.Equal("", alpha.Arguments[0].Raw);
            var position = shown.Single(l => l.Name == "Position");
            Assert.Equal(new[] { "10", "", "0" }, position.Arguments.Select(a => a.Raw));

            Commit(w.Properties.BoxFor(alpha, 0)!, "0.25");
            Assert.Equal("0.25", Line(w, "one", "Alpha").Arguments[0].Raw);
            Assert.Equal("0.25", Line(w, "two", "Alpha").Arguments[0].Raw);
            // The other numbers of a line stay each its own.
            Commit(w.Properties.BoxFor(position, 0)!, "30");
            Assert.Equal("Position(30, 20, 0)", HudWriter.Header(Line(w, "one", "Position")));
            Assert.Equal("Position(30, 60, 0)", HudWriter.Header(Line(w, "two", "Position")));

            ApplicationCommands.Undo.Execute(null, w);
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal("0.5", Line(w, "one", "Alpha").Arguments[0].Raw);
            Assert.Equal("0.8", Line(w, "two", "Alpha").Arguments[0].Raw);
            Assert.Equal("Position(10, 60, 0)", HudWriter.Header(Line(w, "two", "Position")));
        });
    }
}
