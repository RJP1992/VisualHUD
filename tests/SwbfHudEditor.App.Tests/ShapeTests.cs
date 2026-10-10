using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>A BitmapRect made square, or the texture's shape, as drawn; and a width that keeps the shape.</summary>
public class ShapeTests
{
    // A stock file: its bitmaps are stretched by the stock layout, so a square written is not drawn square.
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Bitmap("pixels")
        {
            Viewport(1)
            Bitmap("wide")
            BitmapRect(64, 64, "Left", "Top", "Pixels")
            Position(100, 100, 0)
        }

        Bitmap("shares")
        {
            Viewport(1)
            Bitmap("wide")
            BitmapRect(0.1, 0.1, "Left", "Top", "Viewport")
            Position(300, 100, 0)
        }

        Group("scaled")
        {
            Viewport(1)
            Position(500, 100, 0)
            Scale(2, 1, 1)

            Bitmap("half")
            {
                Bitmap("wide")
                TexCoords(0, 0, 0.5, 1)
                BitmapRect(32, 32, "Left", "Top", "Pixels")
                Position(0, 0, 0)
            }
        }

        """;

    // 8 x 4: twice as wide as tall.
    private static readonly Dictionary<string, byte[]> Files = new() { ["wide.tga"] = TestTextures.Tga(8, 4, 255, 255, 255, 255) };

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

    private static HudBlock RectOf(MainWindow w, string name) => Named(w, name).Children.First(c => !c.HasBody && c.Name == "BitmapRect");

    /// <summary>The bitmap's quad as drawn: its width and height in screen pixels.</summary>
    private static Vector2 Drawn(MainWindow w, string name)
    {
        var e = w.Canvas.LaidOut(Named(w, name))!;
        var q = e.Quad!.Value;
        var topLeft = Vector2.Transform(new Vector2(q.Left, q.Top), e.ToScreen);
        return new Vector2(
            Vector2.Distance(topLeft, Vector2.Transform(new Vector2(q.Right, q.Top), e.ToScreen)),
            Vector2.Distance(topLeft, Vector2.Transform(new Vector2(q.Left, q.Bottom), e.ToScreen)));
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

    /// <summary>Pick the element, and find the panel's shape button whose tip starts so.</summary>
    private static ButtonBase ShapeButton(MainWindow w, string name, string tip)
    {
        w.SelectByName(name);
        w.Properties.FlushRows();
        UiThread.Settle();
        return Descendants<ButtonBase>(w.Properties).First(b => b.ToolTip is string t && t.StartsWith(tip, StringComparison.Ordinal));
    }

    private static void Click(ButtonBase button)
    {
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        UiThread.Settle();
    }

    [Theory]
    [InlineData("pixels")]
    [InlineData("shares")]
    [InlineData("half")]
    public void Square_Draws_The_Bitmap_As_Tall_As_It_Is_Wide(string name)
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            var before = Drawn(w, name);
            Assert.True(MathF.Abs(before.X - before.Y) > 4, $"{name} was drawn square already: {before}");
            Click(ShapeButton(w, name, "Square"));
            var after = Drawn(w, name);
            Assert.Equal(before.X, after.X, 3);   // the width stays
            Assert.True(MathF.Abs(after.X - after.Y) <= 1.5f, $"{name} drawn {after.X} x {after.Y}");
        });
    }

    [Fact]
    public void The_Textures_Shape_Is_Its_Size_Over_The_Part_TexCoords_Show()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            Click(ShapeButton(w, "pixels", "The texture's shape"));
            var whole = Drawn(w, "pixels");
            Assert.True(MathF.Abs(whole.X / whole.Y - 2f) < 0.05f, $"drawn {whole.X} x {whole.Y}");

            // Half the texture's width shown: 4 x 4 texels, so square.
            Click(ShapeButton(w, "half", "The texture's shape"));
            var half = Drawn(w, "half");
            Assert.True(MathF.Abs(half.X - half.Y) <= 1.5f, $"drawn {half.X} x {half.Y}");
        });
    }

    [Fact]
    public void With_The_Lock_On_A_New_Width_Keeps_The_Drawn_Shape_In_One_Undo_Step()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            var rect = RectOf(w, "pixels");
            var before = Drawn(w, "pixels");
            var toggle = (ToggleButton)ShapeButton(w, "pixels", "Keep the shape");
            toggle.IsChecked = true;
            Click(toggle);

            var box = w.Properties.BoxFor(rect, 0)!;
            box.Text = "128";
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, box));
            UiThread.Settle();
            var after = Drawn(w, "pixels");
            Assert.Equal(before.X * 2, after.X, 3);
            Assert.True(MathF.Abs(after.X / after.Y - before.X / before.Y) < 0.03f, $"{before} became {after}");
            Assert.Equal(before.Y * 2, after.Y, 1.5f);

            // One undo step puts both back.
            System.Windows.Input.ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal("64", rect.Arguments[0].Raw);
            Assert.Equal("64", rect.Arguments[1].Raw);

            // Off again: the width alone changes.
            toggle = (ToggleButton)ShapeButton(w, "pixels", "Keep the shape");
            Assert.True(toggle.IsChecked);
            toggle.IsChecked = false;
            Click(toggle);
            box = w.Properties.BoxFor(rect, 0)!;
            box.Text = "100";
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, box));
            Assert.Equal("64", rect.Arguments[1].Raw);
        });
    }
}
