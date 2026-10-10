using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App.Tests;

/// <summary>Units converted so nothing moves, and sums and pixels typed into the Properties panel.</summary>
public class UnitsTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Group("group")
        {
            Viewport(1)
            Position(100, 50, 0)
            Rect(0.5, 0.5, "Left", "Top", "Viewport")

            Bitmap("icon")
            {
                Position(64, 32, 0)
                BitmapRect(64, 32, "Center", "Center", "Pixels")
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

    private static (Vector2 Origin, Vector2 Corner) Placed(MainWindow w, string name)
    {
        var e = w.Canvas.LaidOut(Named(w, name))!;
        var q = e.Quad ?? new QuadRect(0, 0, e.RectWidthPixels, e.RectHeightPixels);
        return (Vector2.Transform(new Vector2(q.Left, q.Top), e.ToScreen), Vector2.Transform(new Vector2(q.Right, q.Bottom), e.ToScreen));
    }

    private static void Commit(TextBox box, string text)
    {
        box.Text = text;
        box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, box));
        UiThread.Settle();
    }

    [Fact]
    public void Convert_Units_Rewrites_A_Group_And_Everything_In_It_Without_Moving_Them()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            var group = Placed(w, "group");
            var icon = Placed(w, "icon");
            w.SelectByName("group");
            Assert.Equal(3, w.ConvertUnits(w.SelectedItems, RelativeMode.Viewport));

            Assert.Equal("Viewport", LineOf(Named(w, "group"), "Position").Arguments[3].Raw);
            Assert.Equal("Viewport", LineOf(Named(w, "icon"), "BitmapRect").Arguments[4].Raw);
            var screen = w.Canvas.ScreenSize;
            Assert.Equal(64f / screen.Width, float.Parse(LineOf(Named(w, "icon"), "Position").Arguments[0].Raw), 5);
            // The same place and size on the canvas.
            Assert.True(Vector2.Distance(group.Origin, Placed(w, "group").Origin) < 0.01f);
            Assert.True(Vector2.Distance(icon.Origin, Placed(w, "icon").Origin) < 0.6f);
            Assert.True(Vector2.Distance(icon.Corner, Placed(w, "icon").Corner) < 0.6f);

            // One undo step puts all three back.
            System.Windows.Input.ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(3, LineOf(Named(w, "group"), "Position").Arguments.Count);
            Assert.Equal("Pixels", LineOf(Named(w, "icon"), "BitmapRect").Arguments[4].Raw);
        });
    }

    [Fact]
    public void The_Panel_Works_Out_Sums_And_Pixels_And_Converts_A_Line_Given_A_Unit()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.SelectByName("icon");
            w.Properties.FlushRows();
            var icon = Named(w, "icon");
            var position = LineOf(icon, "Position");

            Commit(w.Properties.BoxFor(position, 0)!, "128/4");
            Assert.Equal("32", position.Arguments[0].Raw);
            Commit(w.Properties.BoxFor(position, 1)!, "5.12*2");
            Assert.Equal(HudArgKind.Float, position.Arguments[1].Kind);
            Assert.Equal(10.24, double.Parse(position.Arguments[1].Raw), 6);

            // The Position has no mode: the panel shows Pixels, and choosing Viewport converts it.
            var before = Placed(w, "icon").Origin;
            w.Properties.FlushRows();
            var implied = Descendants<TextBox>(w.Properties).First(b => b.FontStyle == FontStyles.Italic && b.Text == "Pixels");
            Commit(implied, "Viewport");
            Assert.Equal("Viewport", position.Arguments[3].Raw);
            Assert.True(Vector2.Distance(before, Placed(w, "icon").Origin) < 0.01f);

            // Pixels typed into a Viewport number are put in Viewport.
            w.Properties.FlushRows();
            var screen = w.Canvas.ScreenSize;
            Commit(w.Properties.BoxFor(position, 0)!, $"{screen.Width / 4}px");
            Assert.Equal(0.25, double.Parse(position.Arguments[0].Raw), 6);
        });
    }

    [Fact]
    public void A_Drag_Refreshes_The_Panels_Values_Without_Building_It_Again()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.SelectByName("icon");
            w.Properties.FlushRows();
            var position = LineOf(Named(w, "icon"), "Position");
            var box = w.Properties.BoxFor(position, 0)!;
            position.Arguments[0] = HudArgument.FromInt(80);
            Assert.True(w.Properties.RefreshValues());
            Assert.Same(box, w.Properties.BoxFor(position, 0));
            Assert.Equal("80", box.Text);
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }
}
