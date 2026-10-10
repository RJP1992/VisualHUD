using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>Numbers stepped by the wheel and the arrows: how far for the keys held, a run as one undo step, events sent.</summary>
public class StepTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Bitmap("icon")
        {
            Viewport(1)
            ZOrder(5)
            Alpha(0.5)
            Position(10, 20, 0)
            BitmapRect(0.1, 0.1, "Left", "Top", "Viewport")
            EventValue("player1.healthFraction")
        }

        """;

    private static HudBlock Line(MainWindow w, string name) =>
        w.Files.SelectMany(f => f.Document.TopLevel).First(b => b.ItemName == "icon").Children.First(c => !c.HasBody && c.Name == name);

    private static TextBox Box(MainWindow w, string line, int index)
    {
        w.Properties.FlushRows();
        return w.Properties.BoxFor(Line(w, line), index)!;
    }

    private static void Wheel(TextBox box, int delta) =>
        box.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent });

    [Fact]
    public void The_Wheel_Steps_A_Number_Being_Typed_In_And_A_Run_Undoes_As_One()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.SelectByName("icon");
            var alpha = Box(w, "Alpha", 0);

            // Not being typed in: the wheel scrolls the panel and the number stays.
            w.Properties.TakesWheel = _ => false;
            Wheel(alpha, 120);
            Assert.Equal("0.5", Line(w, "Alpha").Arguments[0].Raw);

            w.Properties.TakesWheel = _ => true;
            Wheel(alpha, 120);
            Wheel(alpha, 120);
            Wheel(alpha, 60);    // half a notch: nothing yet
            Assert.Equal(0.52, double.Parse(Line(w, "Alpha").Arguments[0].Raw), 6);
            Wheel(alpha, 60);
            Wheel(alpha, -120 * 4);
            Assert.Equal(0.49, double.Parse(Line(w, "Alpha").Arguments[0].Raw), 6);
            Assert.Equal(Line(w, "Alpha").Arguments[0].Raw, alpha.Text);

            // One undo step takes the whole run back.
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal("0.5", Line(w, "Alpha").Arguments[0].Raw);
        });
    }

    [Fact]
    public void Shift_Alt_And_Ctrl_Step_Ten_Times_A_Hundred_Times_And_A_Tenth()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.SelectByName("icon");
            double Number(string line, int index) => double.Parse(Line(w, line).Arguments[index].Raw, System.Globalization.CultureInfo.InvariantCulture);

            // A fraction steps by a hundredth.
            var width = Box(w, "BitmapRect", 0);
            w.Properties.Step(width, Line(w, "BitmapRect"), 0, 1, ModifierKeys.None);
            Assert.Equal(0.11, Number("BitmapRect", 0), 6);
            w.Properties.Step(width, Line(w, "BitmapRect"), 0, 1, ModifierKeys.Shift);
            Assert.Equal(0.21, Number("BitmapRect", 0), 6);
            w.Properties.Step(width, Line(w, "BitmapRect"), 0, -1, ModifierKeys.Control);
            Assert.Equal(0.209, Number("BitmapRect", 0), 6);
            w.Properties.Step(width, Line(w, "BitmapRect"), 0, 1, ModifierKeys.Alt);
            Assert.Equal(1.209, Number("BitmapRect", 0), 6);

            // A count of pixels steps by a pixel, and takes a tenth as a fraction.
            var x = Box(w, "Position", 0);
            w.Properties.Step(x, Line(w, "Position"), 0, 2, ModifierKeys.Shift);
            Assert.Equal("30", Line(w, "Position").Arguments[0].Raw);
            w.Properties.Step(x, Line(w, "Position"), 0, 1, ModifierKeys.Control);
            Assert.Equal(HudArgKind.Float, Line(w, "Position").Arguments[0].Kind);
            Assert.Equal(30.1, Number("Position", 0), 6);

            // Any other whole number stays whole.
            var z = Box(w, "ZOrder", 0);
            w.Properties.Step(z, Line(w, "ZOrder"), 0, 1, ModifierKeys.Control);
            Assert.Equal("6", Line(w, "ZOrder").Arguments[0].Raw);
            w.Properties.Step(z, Line(w, "ZOrder"), 0, -1, ModifierKeys.Alt);
            Assert.Equal("-94", Line(w, "ZOrder").Arguments[0].Raw);
        });
    }

    [Fact]
    public void An_Events_Number_Steps_And_Is_Sent_At_Each_Step()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.ShowLeftTab("Events");
            UiThread.Settle();
            var row = w.EventsList.EventList.Items.OfType<EventRow>().Single(r => r.Name == "player1.healthFraction");
            row.ValueText = "0.5";
            Assert.True(w.EventsList.Step(row, -3, ModifierKeys.Shift));
            Assert.Equal("0.2", row.ValueText);
            Assert.Equal(0.2f, w.EventState.Sends["player1.healthFraction"].Value.Float, 5);

            // A name, a colour or a texture keeps its box's own keys.
            var texture = new EventRow { Name = "x", Type = HudEventType.Uint, Source = HudEventSource.Unknown, Details = "", Suggestions = Array.Empty<string>(), ValueText = "hud_a" };
            Assert.False(w.EventsList.Step(texture, 1, ModifierKeys.None));
        });
    }
}
