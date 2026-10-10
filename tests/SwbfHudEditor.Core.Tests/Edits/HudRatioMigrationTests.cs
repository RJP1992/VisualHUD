using System.Numerics;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Edits;

/// <summary>A file rewritten for AuthoredRatio(w, h) draws as it did, at a screen of that shape.</summary>
public class HudRatioMigrationTests
{
    private const string Body = """

        Bitmap("pix")
        {
            Viewport(1)
            Bitmap("a")
            BitmapRect(64, 64, "Left", "Top", "Pixels")
            Position(100, 100, 0)
        }

        Group("grp")
        {
            Viewport(1)
            Position(0.5, 0.5, 0, "Viewport")
            Rect(0.2, 0.1, "Center", "Center", "Viewport")

            Bitmap("child")
            {
                BitmapRect(0.05, 0.05, "Center", "Center")
                Position(0.25, 0.75, 0, "Frame")
            }

            Text("label")
            {
                Position(10, 20, 0)
                TextBox(0.1, 0.05)
            }
        }

        Group("scaled")
        {
            Viewport(1)
            Position(300, 500, 0)
            Scale(2, 1, 1)

            Bitmap("inner")
            {
                BitmapRect(20, 10)
                Position(5, 5, 0)
            }
        }

        Model3D("model")
        {
            Viewport(1)
            Position(1000, 300, 0)
            MeshInfo("hud_mesh")
            {
                Position(40, 50, 0)
            }
        }

        VehicleSeating("seats")
        {
            Viewport(1)
            Position(900, 600, 0)
            Rect(0.1, 0.1)
        }

        Bitmap("unplaced")
        {
            Viewport(1)
            BitmapRect(10, 10)
        }

        BarSegmented("ring")
        {
            AngleStart(-135.000000)
            AngleEnd(135.000000)
            NumSegments(30)
            Rect(0.087831, 0.111404, "Center", "Center", "Viewport")
            Viewport(1)
            Position(0.300000, 0.300000, 0.000000, "Viewport")
            Segment("Segment")
            {
                Bitmap("hud_clipbar_segment")
                BitmapRect(0.003122, 0.014606, "Center", "Center", "Viewport")
            }
        }

        Target("targets")
        {
            Viewport(1)
            ObjectiveOnScreen("ObjectiveOnScreen")
            {
                Bitmap("hud_objective_icon")
                BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
            }
        }

        """;

    private static readonly LayoutOptions Options = new() { Target = HudTarget.GameExt, Rounding = false, ShowByEvents = false };

    /// <summary>
    /// What is drawn, in layout order: each element's origin and outline on the screen, each segment's outline,
    /// and the size each copy of a template is drawn at (code places those by the screen, not by the file).
    /// </summary>
    private static List<(string Label, Vector2[] Points, bool Size)> Drawn(HudDocument file, HudScreen screen)
    {
        var layout = HudLayout.Lay(file, Options with { Screen = screen });
        var drawn = new List<(string, Vector2[], bool)>();
        Vector2[] Corners(QuadRect q, Matrix4x4 m) =>
            new[] { new Vector2(q.Left, q.Top), new Vector2(q.Right, q.Top), new Vector2(q.Left, q.Bottom), new Vector2(q.Right, q.Bottom) }
                .Select(p => Vector2.Transform(p, m)).ToArray();
        foreach (var e in layout.All)
        {
            var label = $"{e.Block.Name} \"{e.Block.ItemName}\"" + (e.Piece?.Template is { } t ? $" copy of {t.Name}" : "");
            var r = e.FullQuad ?? e.Quad;
            if (e.Piece is not null)
            {
                if (r is { } pq)
                {
                    var c = Corners(pq, e.ToScreen);
                    drawn.Add((label, new[] { new Vector2(Vector2.Distance(c[0], c[1]), Vector2.Distance(c[0], c[2])) }, true));
                }
                continue;
            }
            // A ring of segments draws only its segments: its Rect is sized for the ring, its origin follows.
            if (e.Kind == ElementKind.BarSegmented && e.Spec.Circular)
            {
                // Each segment's middle, and their size unturned, along the bar's axes: a segment turned under the
                // stock band was skewed, each its own way.
                var segmentQuad = e.SegmentQuad!.Value;
                var b = Corners(segmentQuad, e.ToScreen);
                drawn.Add(($"{label} segments' size", new[] { new Vector2(Vector2.Distance(b[0], b[1]), Vector2.Distance(b[0], b[2])) }, true));
                foreach (var segment in e.Segments.Where(x => x.Lit))
                {
                    var c = Corners(segmentQuad, segment.ToScreen);
                    drawn.Add(($"{label} segment {segment.Index}", new[] { (c[0] + c[3]) / 2 }, true));
                }
                continue;
            }
            // A target is placed by code where the game projects what it marks: its copies' sizes count.
            if (e.Kind == ElementKind.Target) continue;
            // A group's Rect, where one is written: without, it is the whole view, which is not drawn.
            if (r is null && ElementKinds.IsGroupBased(e.Kind) && e.Block.Children.Any(c => !c.HasBody && c.Name == "Rect"))
                r = new QuadRect(0, 0, e.RectWidthPixels, e.RectHeightPixels);
            var points = new List<Vector2> { Vector2.Transform(Vector2.Zero, e.ToScreen) };
            if (r is { } q) points.AddRange(Corners(q, e.ToScreen));
            drawn.Add((label, points.ToArray(), false));
            if (e.SegmentQuad is { } sq)
                foreach (var segment in e.Segments)
                    drawn.Add(($"{label} segment {segment.Index}", Corners(sq, segment.ToScreen), true));
        }
        return drawn;
    }

    private static void AssertSameDrawing(List<(string Label, Vector2[] Points, bool Size)> before, List<(string Label, Vector2[] Points, bool Size)> after)
    {
        Assert.Equal(before.Select(b => b.Label), after.Select(a => a.Label));
        Assert.Contains(before, b => b.Label.Contains(" segment "));
        Assert.Contains(before, b => b.Label.Contains(" copy of ObjectiveOnScreen"));
        for (var n = 0; n < before.Count; n++)
        {
            var (label, points, size) = before[n];
            var now = after[n].Points;
            for (var i = 0; i < points.Length; i++)
            {
                var exact = i == 0 && !size;   // an origin is placed exactly; an edge is rounded to a pixel
                Assert.True(Vector2.Distance(points[i], now[i]) < (exact ? 0.05f : 1.5f), $"{label} point {i}: {points[i]} became {now[i]}");
            }
        }
    }

    private static HudBlock Line(HudDocument file, string element, string name) =>
        Blocks(file.TopLevel).First(b => b.HasBody && b.ItemName == element).Children.Last(c => !c.HasBody && c.Name == name);

    private static IEnumerable<HudBlock> Blocks(IEnumerable<HudBlock> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            foreach (var inner in Blocks(b.Children)) yield return inner;
        }
    }

    [Theory]
    [InlineData("", 16, 9)]
    [InlineData("", 64, 27)]
    [InlineData("TrueWidescreen(1)", 16, 9)]
    [InlineData("TrueWidescreen(1)", 64, 27)]
    [InlineData("AuthoredRatio(16, 9)", 32, 9)]
    [InlineData("", 4, 3)]
    public void The_File_Draws_As_It_Did_At_A_Screen_Of_The_Shape(string layoutLine, int w, int h)
    {
        var text = $$"""
            FileInfo("test")
            {
                Viewports(1)
                {{layoutLine}}
            }
            """ + Body;
        var file = HudParser.Parse(text);
        var original = HudWriter.Write(file);
        var reference = HudRatioMigration.ReferenceFor(w, h, 1080);
        Assert.Null(HudRatioMigration.WhyNot(file, w, h));
        var before = Drawn(file, reference);

        var result = HudRatioMigration.Migrate(new[] { file }, file, w, h, 1080, Options);
        Assert.Equal(reference, result.Reference);
        AssertSameDrawing(before, Drawn(file, reference));

        // FileInfo says the shape, and nothing else about the layout.
        var info = file.TopLevel.First(b => b.Name == "FileInfo");
        Assert.Equal($"AuthoredRatio({w}, {h})", HudWriter.Header(Assert.Single(info.Children, c => c.Name is "AuthoredRatio" or "TrueWidescreen")));
        // Viewport, but Frame stays Frame, a seating chart's own Position stays pixels and a MeshInfo keeps its unit.
        Assert.Equal("Viewport", Line(file, "pix", "Position").Arguments[3].Raw);
        Assert.Equal("Viewport", Line(file, "pix", "BitmapRect").Arguments[4].Raw);
        Assert.Equal("Frame", Line(file, "child", "Position").Arguments[3].Raw);
        Assert.Equal(3, Line(file, "seats", "Position").Arguments.Count);
        Assert.Equal(3, Blocks(file.TopLevel).First(b => b.Name == "MeshInfo").Children.Single().Arguments.Count);

        // One undo step puts it all back.
        new CompositeEdit(result.Edits, "rewrite").Revert();
        Assert.Equal(original, HudWriter.Write(file));
    }

    [Fact]
    public void A_File_For_Split_Screens_Or_A_Shape_Out_Of_Range_Is_Refused()
    {
        var split = HudParser.Parse("FileInfo(\"x\")\n{\n    Viewports(2, 3, 4)\n}\n" + Body);
        Assert.NotNull(HudRatioMigration.WhyNot(split, 16, 9));
        var file = HudParser.Parse(Body);
        Assert.NotNull(HudRatioMigration.WhyNot(file, 5, 1));
        Assert.Null(HudRatioMigration.WhyNot(file, 16, 9));

        // A file without a FileInfo gets one.
        HudRatioMigration.Migrate(new[] { file }, file, 16, 9, 720, Options);
        Assert.Equal("FileInfo", file.TopLevel[0].Name);
    }
}
