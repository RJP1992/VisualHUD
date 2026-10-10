using System.Windows.Controls;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>The Properties panel's boxes: a line of one value fills its row, with a list to pick from or not.</summary>
public class PropertyPanelTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Text("label")
        {
            Viewport(1)
            TextFont("gamefont_tiny")
            EventText("player1.weapon1.name")
            Position(10, 20, 0, "Viewport")
        }

        """;

    private static HudBlock Line(MainWindow w, string name) =>
        w.Files.SelectMany(f => f.Document.TopLevel).First(b => b.ItemName == "label").Children.First(c => !c.HasBody && c.Name == name);

    [Fact]
    public void A_Line_Of_One_Value_With_A_List_Fills_Its_Row_Like_One_Without()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.SelectByName("label");
            w.Properties.FlushRows();
            UiThread.Settle();
            var plain = w.Properties.BoxFor(Line(w, "Viewport"), 0)!;
            var eventBox = w.Properties.BoxFor(Line(w, "EventText"), 0)!;
            var fontBox = w.Properties.BoxFor(Line(w, "TextFont"), 0)!;
            Assert.True(plain.ActualWidth > 150, $"the plain box is {plain.ActualWidth} wide");
            Assert.Equal(plain.ActualWidth, eventBox.ActualWidth, 0.5);
            Assert.Equal(plain.ActualWidth, fontBox.ActualWidth, 0.5);
            // The list's button sits at the box's right end, in the same cell.
            var cell = Assert.IsType<Grid>(eventBox.Parent);
            Assert.Equal(cell.ActualWidth, eventBox.ActualWidth, 0.5);

            // In a line of several values the list's box keeps its size beside the others.
            var mode = w.Properties.BoxFor(Line(w, "Position"), 3)!;
            Assert.Equal(118, mode.ActualWidth, 0.5);
        });
    }
}
