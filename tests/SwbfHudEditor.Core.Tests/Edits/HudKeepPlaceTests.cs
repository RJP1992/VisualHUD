using System.Numerics;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Edits;

/// <summary>Elements moved to another parent stay where they are drawn, and as large.</summary>
public class HudKeepPlaceTests
{
    private const string Text = """
        Group("scaled")
        {
            Viewport(1)
            Position(100, 100, 0)
            Scale(2, 2, 1)

            Bitmap("icon")
            {
                BitmapRect(20, 10, "Center", "Center", "Pixels")
                Position(10, 15, 0)
            }

            Text("label")
            {
                TextBox(0.1, 0.05)
            }
        }

        Group("other")
        {
            Viewport(1)
            Position(0.5, 0.25, 0, "Viewport")
        }

        """;

    private static readonly LayoutOptions Options = new() { Screen = new HudScreen(1920, 1080), Target = HudTarget.GameExt, ShowByEvents = false };

    private static HudBlock Named(HudDocument doc, string name)
    {
        IEnumerable<HudBlock> All(IEnumerable<HudBlock> blocks) => blocks.SelectMany(b => new[] { b }.Concat(All(b.Children)));
        return All(doc.TopLevel).First(b => b.HasBody && b.ItemName == name);
    }

    private static Vector2[] Corners(HudDocument doc, string name)
    {
        var e = HudLayout.Lay(doc, Options).Find(Named(doc, name))!;
        var q = e.Quad ?? new QuadRect(0, 0, e.TextBoxWidth, e.TextBoxHeight);
        return new[] { Vector2.Zero, new Vector2(q.Left, q.Top), new Vector2(q.Right, q.Bottom) }
            .Select(p => Vector2.Transform(p, e.ToScreen)).ToArray();
    }

    private static void Move(HudDocument doc, string name, string to)
    {
        var block = Named(doc, name);
        doc.FindContainer(block)!.Value.Container.Remove(block);
        Named(doc, to).Children.Add(block);
    }

    [Fact]
    public void An_Element_Moved_Out_Of_A_Scaled_Group_Stays_Where_And_As_Large_As_It_Was()
    {
        var doc = HudParser.Parse(Text);
        var icon = Corners(doc, "icon");
        var label = Corners(doc, "label");
        var keep = HudKeepPlace.Capture(HudLayout.Lay(doc, Options), new[] { Named(doc, "icon"), Named(doc, "label") });

        Move(doc, "icon", "other");
        Move(doc, "label", "other");
        var notes = new List<string>();
        var edits = keep.Restore(HudLayout.Lay(doc, Options), notes);

        Assert.Empty(notes);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Vector2.Distance(icon[i], Corners(doc, "icon")[i]) < 0.01f, $"icon point {i}");
            Assert.True(Vector2.Distance(label[i], Corners(doc, "label")[i]) < 0.6f, $"label point {i}");
        }
        // The icon takes the group's scale as its own; the label, at its old parent's origin, gets a Position.
        Assert.Equal("Scale(2, 2, 1)", HudWriter.Header(Named(doc, "icon").Children.Single(c => c.Name == "Scale")));
        Assert.Single(Named(doc, "label").Children, c => c.Name == "Position");
        // In a unit that scales: the icon's pixels, and the label's new line, in Viewport.
        Assert.Equal("Viewport", Named(doc, "icon").Children.Single(c => c.Name == "Position").Arguments[3].Raw);
        Assert.Equal("Viewport", Named(doc, "label").Children.Single(c => c.Name == "Position").Arguments[3].Raw);

        // Undone, the lines are as they were.
        new CompositeEdit(edits, "x").Revert();
        Assert.DoesNotContain(Named(doc, "icon").Children, c => c.Name == "Scale");
        Assert.Equal("Position(10, 15, 0)", HudWriter.Header(Named(doc, "icon").Children.Single(c => c.Name == "Position")));
    }
}
