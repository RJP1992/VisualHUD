using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>The canvas keeps what did not change while values change, and draws what did.</summary>
public class CanvasTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Text("ammo")
        {
            Viewport(1)
            EventNumber("player1.weapon1.ammo")
            Position(0.5, 0.5, 0, "Viewport")
        }

        Text("label")
        {
            Viewport(1)
            Text("label")
            Position(0.5, 0.6, 0, "Viewport")
        }

        """;

    private static HudBlock Named(MainWindow w, string name) =>
        w.Files.SelectMany(f => f.Document.TopLevel).First(b => b.HasBody && b.ItemName == name);

    [Fact]
    public void A_Send_Redraws_Only_What_It_Changed_And_An_Edit_Redraws_Everything()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.EventState.Send("player1.weapon1.ammo", HudEventValue.FromInt(10));
            UiThread.Settle();
            var ammo = w.Canvas.VisualOf(Named(w, "ammo"));
            var label = w.Canvas.VisualOf(Named(w, "label"));
            Assert.NotNull(ammo);
            Assert.NotNull(label);

            w.EventState.Send("player1.weapon1.ammo", HudEventValue.FromInt(9));
            UiThread.Settle();
            Assert.NotSame(ammo, w.Canvas.VisualOf(Named(w, "ammo")));
            Assert.Same(label, w.Canvas.VisualOf(Named(w, "label")));

            // The clock moving changes nothing here: both keep their visuals.
            ammo = w.Canvas.VisualOf(Named(w, "ammo"));
            w.EventState.SetTime(1.0);
            UiThread.Settle();
            Assert.Same(ammo, w.Canvas.VisualOf(Named(w, "ammo")));

            w.Canvas.Refresh();
            Assert.NotSame(label, w.Canvas.VisualOf(Named(w, "label")));
        });
    }
}
