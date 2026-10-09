using System.Globalization;
using System.Numerics;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>Saving leaves out the lines that restate the engine's defaults, and only those.</summary>
public class ElementDefaultsTests
{
    private const string ModtoolsHudFolder = @"C:\BF2_ModTools\data\Common\hud";

    private static List<string> Saved(string source) =>
        ElementDefaults.WriteWithoutDefaults(HudParser.Parse(source))
            .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    [Fact]
    public void Default_Lines_Are_Left_Out()
    {
        var lines = Saved("""
            Group("g")
            {
                Position(0, 0, 0, "Viewport")
                Rotation(0, 0, 0)
                Scale(1, 1, 1)
                Rect(1, 1, "Left", "Top", "Viewport")
                Alpha(1)
                Color(255, 255, 255)
                PropagateAlpha(0)
                Scalable(0)
                Viewport(0)
                FadeInTime(0)
                ZOrder(128)
                EventEnable("initialize")
                BarBitmap("b")
                {
                    BitmapRect(10, 10)
                    TexCoords(0, 0, 1, 1)
                    BlendMode("alpha")
                    MaxValue(1)
                    FillFrom("Left")
                    Position(5, 0, 0)
                }
                Text("t")
                {
                    TextScale(1, 1)
                    TextAlignment("Left", "Top")
                    IntegerFormat("%d")
                    TextFont("gamefont_small")
                    Text("level.common.hud.ammo")
                }
            }
            """);
        foreach (var gone in new[]
                 {
                     "Position(0, 0, 0, \"Viewport\")", "Rotation(0, 0, 0)", "Scale(1, 1, 1)", "Rect(1, 1, \"Left\", \"Top\", \"Viewport\")",
                     "Alpha(1)", "Color(255, 255, 255)", "PropagateAlpha(0)", "Scalable(0)", "Viewport(0)", "FadeInTime(0)",
                     "TexCoords(0, 0, 1, 1)", "BlendMode(\"alpha\")", "MaxValue(1)", "FillFrom(\"Left\")",
                     "TextScale(1, 1)", "TextAlignment(\"Left\", \"Top\")", "IntegerFormat(\"%d\")",
                 })
            Assert.DoesNotContain(gone, lines);
        // What makes the element, its events, its draw order and what differs from the default.
        foreach (var kept in new[]
                 {
                     "ZOrder(128)", "EventEnable(\"initialize\")", "BitmapRect(10, 10)", "Position(5, 0, 0)",
                     "TextFont(\"gamefont_small\")", "Text(\"level.common.hud.ammo\")",
                 })
            Assert.Contains(kept, lines);
    }

    [Fact]
    public void Lines_That_Matter_Stay()
    {
        var lines = Saved("""
            FileInfo("f") { Viewports(1) }
            Group("g")
            {
                Alpha(0.5)
                Alpha(1)
                Color(255, 255, 255, 255)
                Scalable(false)
                TexCoords(0, 0, 1, 1)
                Rect(1, 1, "Center", "Top")
                Model3D("m")
                {
                    Depth(0.01)
                    Lighting(1)
                    MeshInfo("mesh")
                    {
                        Position(0, 0, 0)
                        Depth(0.01)
                    }
                }
            }
            Map("map") { Alpha(1) }
            TransformNumberColor("tint") { NumberColor(0, 255, 255, 255) EventInput("x") EventOutput("y") }
            """);
        // A later line replaces an earlier one, so both stay; so does the map's, which its code draws.
        Assert.Contains("Alpha(0.5)", lines);
        Assert.Equal(2, lines.Count(l => l == "Alpha(1)"));
        // Not the plain form: four numbers, and a word, which the game reads as its hash (true).
        Assert.Contains("Color(255, 255, 255, 255)", lines);
        Assert.Contains("Scalable(false)", lines);
        // A group has no texture coordinates to default; a centred Rect moves the group.
        Assert.Contains("TexCoords(0, 0, 1, 1)", lines);
        Assert.Contains("Rect(1, 1, \"Center\", \"Top\")", lines);
        // The model's defaults go; its MeshInfo stays, with the Depth that replaces the element's.
        Assert.DoesNotContain("Lighting(1)", lines);
        Assert.DoesNotContain("Position(0, 0, 0)", lines);
        Assert.Contains("MeshInfo(\"mesh\")", lines);
        Assert.Single(lines, l => l == "Depth(0.01)");
        // Outside the elements nothing changes.
        Assert.Contains("Viewports(1)", lines);
        Assert.Contains("NumberColor(0, 255, 255, 255)", lines);
    }

    [Fact]
    public void The_Document_Keeps_Its_Lines()
    {
        var doc = HudParser.Parse("""Bitmap("b") { Alpha(1) BitmapRect(4, 4) }""");
        var before = HudWriter.Write(doc);
        ElementDefaults.WriteWithoutDefaults(doc);
        Assert.Equal(before, HudWriter.Write(doc));
        Assert.Equal(2, doc.TopLevel[0].Children.Count);
    }

    [Theory]
    [InlineData("1playerhud.hud")]
    [InlineData("2playerhud.hud")]
    [InlineData("4playerhud.hud")]
    [InlineData("hudtransforms.hud")]
    public void A_Stock_File_Without_Its_Defaults_Lays_Out_The_Same(string fileName)
    {
        var path = Path.Combine(ModtoolsHudFolder, fileName);
        if (!File.Exists(path)) return; // hermetic skip when modtools aren't on this machine

        var doc = HudParser.Parse(File.ReadAllText(path), path);
        var saved = HudParser.Parse(ElementDefaults.WriteWithoutDefaults(doc));
        foreach (var target in new[] { HudTarget.Vanilla, HudTarget.GameExt })
        foreach (var screen in new[] { new HudScreen(1280, 720), new HudScreen(1024, 768), new HudScreen(2560, 1080) })
        {
            var options = new LayoutOptions { Screen = screen, Target = target };
            Assert.Equal(Describe(HudLayout.Lay(doc, options)), Describe(HudLayout.Lay(saved, options)));
        }
    }

    /// <summary>Everything the preview draws an element with, one line per element.</summary>
    private static List<string> Describe(HudLayoutResult r) => r.All.Select(e =>
    {
        var s = e.Spec;
        var parts = new List<object?>
        {
            e.Kind, s.Name, e.Depth, e.DrawIndex, e.Viewport, e.InActiveViewport, M(e.ToScreen), e.Quad, e.Uv,
            e.DrawColor, e.DrawAlpha, e.BarFraction, e.TextBoxWidth, e.TextBoxHeight, e.Texture, e.Mesh, e.Role, e.Slide,
            e.RectWidthPixels, e.RectHeightPixels, s.ZOrder, s.Scalable, s.EditOnly, s.Additive, s.PropagateAlpha,
            s.Font, s.TextKey, s.TextScale, s.TextHAlign, s.TextVAlign, s.TextStyle, s.IntegerFormat, s.FloatFormat,
            s.Value, s.MinValue, s.MaxValue, s.ScaleTexture, s.ScaleSize, s.FillFrom, s.NumSegments, s.Circular,
            s.AngleStart, s.AngleEnd, string.Join(",", s.Bindings), e.Segments.Count,
            string.Join(";", e.Segments.Select(seg => $"{seg.Lit}{M(seg.ToScreen)}")),
        };
        return string.Join(" | ", parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    }).ToList();

    private static string M(Matrix4x4 m) => string.Create(CultureInfo.InvariantCulture,
        $"[{m.M11:0.####} {m.M12:0.####} {m.M21:0.####} {m.M22:0.####} {m.M41:0.###} {m.M42:0.###}]");
}
