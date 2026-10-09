namespace SwbfHudEditor.Core.Msh;

public readonly record struct Vec3(float X, float Y, float Z);
public readonly record struct Vec2(float X, float Y);
/// <summary>Per-vertex RGBA color from the MSH <c>CLRL</c> chunk. Each component 0..255.</summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A);

public sealed record MshBoundingBox(Vec3 Center, Vec3 Extents);

/// <summary>One geometry segment within a mesh — a contiguous set of triangles sharing a single material.</summary>
public sealed record MshSegment(
    int MaterialIndex,
    IReadOnlyList<Vec3> Positions,
    IReadOnlyList<Vec2> Uvs,
    IReadOnlyList<int> TriangleIndices,
    IReadOnlyList<RgbaColor> VertexColors);

/// <summary>Full mesh payload we care about for flat HUD rendering: materials, segments, and bounds.</summary>
public sealed record MshGeometry(
    IReadOnlyList<string> MaterialTextures,
    IReadOnlyList<MshSegment> Segments,
    MshBoundingBox? BoundingBox);
