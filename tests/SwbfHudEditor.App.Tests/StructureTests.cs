using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>A group's Rect fitted, elements grouped and ungrouped, rows moved to another parent: nothing moves on the screen.</summary>
public class StructureTests
{
    // A stock file: its band and its bitmaps' stretch are in every matrix.
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Group("holder")
        {
            Viewport(1)
            Position(100, 100, 0)
            Rect(0.5, 0.5, "Center", "Center", "Viewport")

            Bitmap("inside")
            {
                Bitmap("square")
                BitmapRect(32, 32, "Left", "Top", "Pixels")
                Position(10, 10, 0)
            }

            Bitmap("frame")
            {
                Bitmap("square")
                BitmapRect(0.05, 0.05, "Left", "Top", "Viewport")
                Position(0.75, 0.25, 0, "Frame")
            }
        }

        Group("scaled")
        {
            Viewport(1)
            Position(500, 300, 0)
            Scale(2, 2, 1)

            Bitmap("big")
            {
                Bitmap("square")
                BitmapRect(20, 10, "Left", "Top", "Pixels")
                Position(5, 5, 0)
            }
        }

        Bitmap("loose1")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(20, 20, "Left", "Top", "Pixels")
            Position(800, 400, 0)
        }

        Bitmap("loose2")
        {
            Viewport(1)
            Bitmap("square")
            BitmapRect(20, 20, "Left", "Top", "Pixels")
            Position(900, 450, 0)
        }

        MultilineText("messages")
        {
            Viewport(1)
            Rect(0.3, 0.2, "Left", "Top", "Viewport")
            NumLines(2)
        }

        """;

    private static readonly Dictionary<string, byte[]> Files = new() { ["square.tga"] = TestTextures.Tga(4, 4, 255, 255, 255, 255) };

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

    private static Rect Drawn(MainWindow w, string name) => w.Canvas.ScreenBoundsOf(Named(w, name))!.Value;

    private static void AssertSame(Rect before, Rect after, string what)
    {
        Assert.True(Math.Abs(before.Left - after.Left) < 0.6 && Math.Abs(before.Top - after.Top) < 0.6 &&
                    Math.Abs(before.Right - after.Right) < 0.6 && Math.Abs(before.Bottom - after.Bottom) < 0.6, $"{what}: {before} became {after}");
    }

    [Fact]
    public void Fitting_A_Groups_Rect_Moves_Nothing_And_Outlines_What_It_Holds()
    {
        UiThread.WithWindow(Hud, Files, (w, path) =>
        {
            var original = System.IO.File.ReadAllText(path).Replace("\r\n", "\n");
            var inside = Drawn(w, "inside");
            var frame = Drawn(w, "frame");
            Assert.True(w.FitRect(Named(w, "holder")));
            AssertSame(inside, Drawn(w, "inside"), "inside");
            AssertSame(frame, Drawn(w, "frame"), "frame");
            var both = Rect.Union(inside, frame);
            AssertSame(both, Drawn(w, "holder"), "the group's outline");
            // Its Frame child still says Frame.
            Assert.Equal("Frame", Named(w, "frame").Children.Single(c => c.Name == "Position").Arguments[3].Raw);

            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(original, HudWriter.Write(w.Files.Single(f => f.FilePath == path).Document).Replace("\r\n", "\n"));

            // Only a plain group's Rect is its frame alone.
            Assert.False(w.FitRect(Named(w, "messages")));
        });
    }

    [Fact]
    public void Grouping_And_Ungrouping_Move_Nothing_And_Undo_As_One()
    {
        UiThread.WithWindow(Hud, Files, (w, path) =>
        {
            var original = System.IO.File.ReadAllText(path).Replace("\r\n", "\n");
            var one = Drawn(w, "loose1");
            var two = Drawn(w, "loose2");
            w.SelectByName("loose1");
            w.TogglePick(Named(w, "loose2"));
            var group = w.GroupSelection()!;
            Assert.Equal(new[] { "loose1", "loose2" }, group.Children.Where(c => c.HasBody).Select(c => c.ItemName));
            Assert.Contains(group.Children, c => c.Name == "Viewport");
            AssertSame(one, Drawn(w, "loose1"), "loose1");
            AssertSame(two, Drawn(w, "loose2"), "loose2");
            AssertSame(Rect.Union(one, two), Drawn(w, group.ItemName), "the new group's outline");
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(original, HudWriter.Write(w.Files.Single(f => f.FilePath == path).Document).Replace("\r\n", "\n"));

            // Out of a scaled group: its scale goes into the element's, and it takes the group's viewport.
            var big = Drawn(w, "big");
            Assert.True(w.Ungroup(Named(w, "scaled")));
            AssertSame(big, Drawn(w, "big"), "big");
            Assert.Equal("Scale(2, 2, 1)", HudWriter.Header(Named(w, "big").Children.Single(c => c.Name == "Scale")));
            Assert.Contains(Named(w, "big").Children, c => c.Name == "Viewport");
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(original, HudWriter.Write(w.Files.Single(f => f.FilePath == path).Document).Replace("\r\n", "\n"));
        });
    }

    [Fact]
    public void A_Row_Moved_Into_Another_Group_Stays_Where_It_Is_Unless_Its_Numbers_Are_Kept()
    {
        UiThread.WithWindow(Hud, Files, (w, _) =>
        {
            var inside = Drawn(w, "inside");
            var scaled = Named(w, "scaled");
            Assert.True(w.MoveBlocks(new[] { Named(w, "inside") }, scaled.Children, scaled.Children.Count));
            AssertSame(inside, Drawn(w, "inside"), "inside, moved");
            ApplicationCommands.Undo.Execute(null, w);
            AssertSame(inside, Drawn(w, "inside"), "inside, back");

            // Its numbers kept, it moves with its new parent.
            Assert.True(w.MoveBlocks(new[] { Named(w, "inside") }, scaled.Children, scaled.Children.Count, keepPlace: false));
            Assert.Equal("Position(10, 10, 0)", HudWriter.Header(Named(w, "inside").Children.Single(c => c.Name == "Position")));
            Assert.NotEqual(inside.Left, Drawn(w, "inside").Left, 1);
        });
    }
}
