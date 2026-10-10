using System.Numerics;
using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>A Text's box fitted to its text: the box shrinks round the lines, the text stays where it is.</summary>
public class FitTextTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Text("label")
        {
            Viewport(1)
            Text("some label")
            TextBox(0.5, 0.3)
            TextAlignment("Center", "Center")
            Position(100, 100, 0)
        }

        """;

    private static HudBlock Named(MainWindow w) => w.Files.SelectMany(f => f.Document.TopLevel).First(b => b.HasBody && b.ItemName == "label");

    /// <summary>The text's lines on the screen.</summary>
    private static Rect OnScreen(MainWindow w)
    {
        var block = Named(w);
        var e = w.Canvas.LaidOut(block)!;
        var t = w.Canvas.TextExtentOf(block)!.Value;
        var a = Vector2.Transform(new Vector2((float)t.Left, (float)t.Top), e.ToScreen);
        var b = Vector2.Transform(new Vector2((float)t.Right, (float)t.Bottom), e.ToScreen);
        return new Rect(new Point(a.X, a.Y), new Point(b.X, b.Y));
    }

    [Fact]
    public void The_Box_Shrinks_Round_The_Text_And_The_Text_Stays()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            var before = OnScreen(w);
            var box = w.Canvas.LaidOut(Named(w))!;
            Assert.True(box.TextBoxWidth > before.Width + 50);

            Assert.True(w.FitTextBox(Named(w)));
            var after = OnScreen(w);
            Assert.Equal(before.Left, after.Left, 1.0);
            Assert.Equal(before.Top, after.Top, 1.0);
            var fitted = w.Canvas.LaidOut(Named(w))!;
            Assert.InRange(fitted.TextBoxWidth - before.Width, 0, 3);

            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal("TextBox(0.5, 0.3)", HudWriter.Header(Named(w).Children.Single(c => c.Name == "TextBox")));
            Assert.Equal("Position(100, 100, 0)", HudWriter.Header(Named(w).Children.Single(c => c.Name == "Position")));
        });
    }
}
