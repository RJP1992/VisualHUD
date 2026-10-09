using SwbfHudEditor.Core.Msh;
using Xunit.Abstractions;

namespace SwbfHudEditor.Core.Tests.Msh;

public class MshBboxDiagnosticTests
{
    private const string ModtoolsMshFolder = @"C:\BF2_ModTools\data\Common\mshs";
    private readonly ITestOutputHelper _out;
    public MshBboxDiagnosticTests(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void Dump_Bboxes()
    {
        var files = new[]
        {
            "hud_main_reticule.msh",
            "hud_main_reticule_bullseye.msh",
            "hud_main_reticule_outline.msh",
            "hud_main_reticule_hit.msh",
            "hud_main_reticule_criticalhit.msh",
            "hud_infinite_ammo_icon.msh",
            "hud_health_regen.msh",
            "hud_herobar_saberhilt.msh",
        };
        foreach (var f in files)
        {
            var path = Path.Combine(ModtoolsMshFolder, f);
            if (!File.Exists(path)) { _out.WriteLine($"{f}: MISSING"); continue; }
            var g = MshParser.ExtractGeometry(path);
            var bboxStr = g.BoundingBox is { } b
                ? $"center=({b.Center.X:F3},{b.Center.Y:F3},{b.Center.Z:F3}) extents=({b.Extents.X:F3},{b.Extents.Y:F3},{b.Extents.Z:F3}) full=({b.Extents.X * 2:F3},{b.Extents.Y * 2:F3})"
                : "null";
            var totalVerts = g.Segments.Sum(s => s.Positions.Count);
            var totalTris = g.Segments.Sum(s => s.TriangleIndices.Count) / 3;
            _out.WriteLine($"{f,-40} segs={g.Segments.Count} verts={totalVerts} tris={totalTris} bbox={bboxStr}");
        }
    }
}
