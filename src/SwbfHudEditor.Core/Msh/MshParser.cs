using System.Numerics;
using System.Text;

namespace SwbfHudEditor.Core.Msh;

/// <summary>
/// Parser for the .msh chunked binary format (little-endian). Each chunk is 4 ASCII bytes (ID)
/// + UInt32 payload size + payload. Parent chunks contain more chunks; data chunks hold raw bytes.
/// Spec: https://schlechtwetterfront.github.io/ze_filetypes/msh.html
///
/// Two entry points:
///   <see cref="ExtractTextureNames"/> — cheap, returns just the TX0D strings (first-pass billboards).
///   <see cref="ExtractGeometry"/> — full geometry: materials, segments (vertices/UVs/triangles), BBOX.
/// </summary>
public static class MshParser
{
    private static readonly HashSet<string> ParentChunks = new(StringComparer.Ordinal)
    {
        "HEDR", "MSH2", "SINF", "MATL", "MATD", "MODL", "GEOM", "SEGM",
    };

    public static IReadOnlyList<string> ExtractTextureNames(string mshPath)
    {
        using var stream = File.OpenRead(mshPath);
        using var reader = new BinaryReader(stream);
        var names = new List<string>();
        ScanForTextures(reader, stream.Length, names);
        return names;
    }

    public static MshGeometry ExtractGeometry(string mshPath)
    {
        using var stream = File.OpenRead(mshPath);
        using var reader = new BinaryReader(stream);
        var state = new ParseState();
        Parse(reader, stream.Length, state);
        return new MshGeometry(state.Materials, PlaceSegments(state), state.BoundingBox);
    }

    /// <summary>
    /// Each segment in the model's own space: the game draws a segment through its node's default pose
    /// (<c>RedModel::Render</c>, P <c>008993D0</c>), the node's <c>TRAN</c> chained up its parents. Hidden
    /// nodes and shadow volumes are not drawn.
    /// </summary>
    private static List<MshSegment> PlaceSegments(ParseState state)
    {
        var byName = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in state.Nodes) byName.TryAdd(n.Name, n);
        var placed = new List<MshSegment>(state.Segments.Count);
        foreach (var (segment, node) in state.Segments)
        {
            if (node is not null && ((node.Flags & 1) != 0 || node.Type == ShadowVolume)) continue;
            var world = node is null ? Matrix4x4.Identity : WorldOf(node, byName);
            if (world.IsIdentity)
            {
                placed.Add(segment);
                continue;
            }
            var positions = new List<Vec3>(segment.Positions.Count);
            foreach (var p in segment.Positions)
            {
                var v = Vector3.Transform(new Vector3(p.X, p.Y, p.Z), world);
                positions.Add(new Vec3(v.X, v.Y, v.Z));
            }
            placed.Add(segment with { Positions = positions });
        }
        return placed;
    }

    private static Matrix4x4 WorldOf(Node node, Dictionary<string, Node> byName)
    {
        var world = node.Local;
        var parent = node.Parent;
        for (var hops = 0; hops < 64 && parent.Length > 0 && byName.TryGetValue(parent, out var p) && !ReferenceEquals(p, node); hops++)
        {
            world *= p.Local;
            parent = p.Parent;
        }
        return world;
    }

    /// <summary><c>MTYP</c> of a shadow volume.</summary>
    private const int ShadowVolume = 6;

    // ── Texture-only scan ─────────────────────────────────────────────────────────

    private static void ScanForTextures(BinaryReader reader, long endOffset, List<string> names)
    {
        while (reader.BaseStream.Position + 8 <= endOffset)
        {
            var id = ReadChunkId(reader);
            if (id.Length != 4) return;
            var size = reader.ReadUInt32();
            var payloadEnd = reader.BaseStream.Position + (long)size;
            if (payloadEnd > endOffset) return;

            if (id == "TX0D")
            {
                names.Add(ReadAsciiZ(reader, (int)size));
            }
            else if (ParentChunks.Contains(id))
            {
                var childStart = reader.BaseStream.Position;
                if (id == "MATL" && size >= 4) childStart += 4;
                reader.BaseStream.Position = childStart;
                ScanForTextures(reader, payloadEnd, names);
            }

            reader.BaseStream.Position = payloadEnd;
        }
    }

    // ── Full geometry parse ───────────────────────────────────────────────────────

    /// <summary>A <c>MODL</c> node: its name, parent, type, flags and transform.</summary>
    private sealed class Node
    {
        public string Name = "";
        public string Parent = "";
        public int Type = -1;
        public uint Flags;
        public Matrix4x4 Local = Matrix4x4.Identity;
    }

    private sealed class ParseState
    {
        public List<string> Materials { get; } = new();
        public List<(MshSegment Segment, Node? Node)> Segments { get; } = new();
        public List<Node> Nodes { get; } = new();
        public Node? CurrentNode;
        public MshBoundingBox? BoundingBox { get; set; }
        public int CurrentSegmentMatIndex;
        public List<Vec3>? CurrentPositions;
        public List<Vec2>? CurrentUvs;
        public List<int>? CurrentTriangles;
        public List<RgbaColor>? CurrentColors;
    }

    private static void Parse(BinaryReader reader, long endOffset, ParseState state)
    {
        while (reader.BaseStream.Position + 8 <= endOffset)
        {
            var id = ReadChunkId(reader);
            if (id.Length != 4) return;
            var size = reader.ReadUInt32();
            var payloadEnd = reader.BaseStream.Position + (long)size;
            if (payloadEnd > endOffset) return;

            switch (id)
            {
                case "TX0D":
                    state.Materials.Add(ReadAsciiZ(reader, (int)size));
                    break;

                case "MODL":
                {
                    var outer = state.CurrentNode;
                    state.CurrentNode = new Node();
                    state.Nodes.Add(state.CurrentNode);
                    Parse(reader, payloadEnd, state);
                    state.CurrentNode = outer;
                    break;
                }

                case "NAME" when state.CurrentNode is not null:
                    state.CurrentNode.Name = ReadAsciiZ(reader, (int)size);
                    break;

                case "PRNT" when state.CurrentNode is not null:
                    state.CurrentNode.Parent = ReadAsciiZ(reader, (int)size);
                    break;

                case "MTYP" when state.CurrentNode is not null && size >= 4:
                    state.CurrentNode.Type = reader.ReadInt32();
                    break;

                case "FLGS" when state.CurrentNode is not null && size >= 4:
                    state.CurrentNode.Flags = reader.ReadUInt32();
                    break;

                case "TRAN" when state.CurrentNode is not null && size >= 40:
                {
                    // Scale, rotation as a quaternion (x, y, z, w), translation.
                    var scale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var translation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    state.CurrentNode.Local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) *
                                              Matrix4x4.CreateTranslation(translation);
                    break;
                }

                case "BBOX":
                    // SINF's BBOX appears first (overall mesh bounds across all MODLs).
                    // Later per-MODL GEOM.BBOX entries would give tighter per-model bounds — we
                    // want the outer one for sizing the displayed icon, so keep first-wins.
                    state.BoundingBox ??= ReadBoundingBox(reader, size);
                    break;

                case "MATI":
                    state.CurrentSegmentMatIndex = size >= 4 ? reader.ReadInt32() : 0;
                    break;

                case "POSL":
                    state.CurrentPositions = ReadVec3List(reader, size);
                    break;

                case "UV0L":
                    // Flip V so UVs work in WPF's V=0-top sampling convention.
                    // MSH files from Pandemic's Softimage pipeline are authored with V=0-bottom.
                    state.CurrentUvs = ReadVec2List(reader, size, flipV: true);
                    break;

                case "CLRL":
                    // Per-vertex RGBA. SWBF2 meshes like hud_weapon_backdrop rely on this for
                    // their transparency (no alpha-channel texture, just painted vertex colors).
                    // Layout: UInt32 count, then count × UInt32 (BGRA or ARGB — Pandemic's pipeline
                    // writes each component as a byte in BGRA order so the UInt32 read as little-
                    // endian gives us R at lowest byte after .NET's little-endian reconstruction).
                    state.CurrentColors = ReadColorList(reader, size);
                    break;

                case "STRP":
                    AppendTriangles(state, DecodeStrip(reader, size));
                    break;

                case "NDXT":
                    // Plain indexed triangle list: UInt32 count, then count*3 UInt16 indices.
                    AppendTriangles(state, ReadNdxtTriangles(reader, size));
                    break;

                case "SEGM":
                    // Reset per-segment state, recurse into children, then emit segment.
                    state.CurrentSegmentMatIndex = 0;
                    state.CurrentPositions = null;
                    state.CurrentUvs = null;
                    state.CurrentTriangles = null;
                    state.CurrentColors = null;
                    Parse(reader, payloadEnd, state);
                    if (state.CurrentPositions is { Count: > 0 } && state.CurrentTriangles is { Count: > 0 })
                    {
                        state.Segments.Add((new MshSegment(
                            state.CurrentSegmentMatIndex,
                            state.CurrentPositions,
                            state.CurrentUvs ?? (IReadOnlyList<Vec2>)Array.Empty<Vec2>(),
                            state.CurrentTriangles,
                            state.CurrentColors ?? (IReadOnlyList<RgbaColor>)Array.Empty<RgbaColor>()), state.CurrentNode));
                    }
                    break;

                default:
                    if (ParentChunks.Contains(id))
                    {
                        var childStart = reader.BaseStream.Position;
                        if (id == "MATL" && size >= 4) childStart += 4;
                        reader.BaseStream.Position = childStart;
                        Parse(reader, payloadEnd, state);
                    }
                    break;
            }

            reader.BaseStream.Position = payloadEnd;
        }
    }

    private static MshBoundingBox ReadBoundingBox(BinaryReader reader, uint size)
    {
        // Layout: rotation quaternion (4 floats) + center (3 floats) + extents (3 floats) + radius (1 float) = 44 bytes
        if (size < 44) return new MshBoundingBox(new Vec3(0, 0, 0), new Vec3(0, 0, 0));
        reader.BaseStream.Position += 16; // skip rotation quaternion
        var center = new Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        var extents = new Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        // trailing radius float — we don't use it
        return new MshBoundingBox(center, extents);
    }

    private static List<Vec3> ReadVec3List(BinaryReader reader, uint size)
    {
        if (size < 4) return new List<Vec3>();
        var count = reader.ReadInt32();
        var list = new List<Vec3>(count);
        for (var i = 0; i < count; i++)
            list.Add(new Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
        return list;
    }

    private static List<Vec2> ReadVec2List(BinaryReader reader, uint size, bool flipV = false)
    {
        if (size < 4) return new List<Vec2>();
        var count = reader.ReadInt32();
        var list = new List<Vec2>(count);
        for (var i = 0; i < count; i++)
        {
            var u = reader.ReadSingle();
            var v = reader.ReadSingle();
            list.Add(new Vec2(u, flipV ? 1.0f - v : v));
        }
        return list;
    }

    private static List<RgbaColor> ReadColorList(BinaryReader reader, uint size)
    {
        if (size < 4) return new List<RgbaColor>();
        var count = reader.ReadInt32();
        var list = new List<RgbaColor>(count);
        for (var i = 0; i < count; i++)
        {
            // Each color is 4 bytes. Softimage-pipeline MSH writes them as byte stream in the
            // order B, G, R, A — so reading individual bytes gives us explicit component order
            // without worrying about platform endianness of a UInt32 read.
            var b = reader.ReadByte();
            var g = reader.ReadByte();
            var r = reader.ReadByte();
            var a = reader.ReadByte();
            list.Add(new RgbaColor(r, g, b, a));
        }
        return list;
    }

    private static List<int> ReadNdxtTriangles(BinaryReader reader, uint size)
    {
        var list = new List<int>();
        if (size < 4) return list;
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            list.Add(reader.ReadUInt16());
            list.Add(reader.ReadUInt16());
            list.Add(reader.ReadUInt16());
        }
        return list;
    }

    private static void AppendTriangles(ParseState state, List<int> newTris)
    {
        if (newTris.Count == 0) return;
        if (state.CurrentTriangles is null) state.CurrentTriangles = newTris;
        else state.CurrentTriangles.AddRange(newTris);
    }

    /// <summary>
    /// STRP: UInt32 count, then UInt16[count] of strip indices. Pandemic's SWBF1/2 mesh format
    /// uses the DOUBLE-MARKER convention: both v0 AND v1 of each strip have the high bit (0x8000)
    /// set. Indices 2+ have no high bit and each forms a triangle with the previous two (with
    /// alternating winding). Confirmed by inspecting HUD_LowCenter_Tooltips_Background.msh whose
    /// STRP starts [0x8000, 0x8001, 0x0003, 0x0002, 0x8003, 0x8002, ...] — pairs of high-bit
    /// markers before each run of triangles.
    ///
    /// The earlier single-marker implementation lost v0 of every strip (second high-bit reset `a`
    /// to v1), which is exactly the "missing triangles" artifact that looked like broken
    /// transparency on tooltip/hero-selection background meshes.
    /// </summary>
    private static List<int> DecodeStrip(BinaryReader reader, uint size)
    {
        var tris = new List<int>();
        if (size < 4) return tris;
        var count = reader.ReadInt32();
        if (count <= 0) return tris;

        int a = -1, b = -1;
        var stripVerts = 0;
        for (var i = 0; i < count; i++)
        {
            if (reader.BaseStream.Position + 2 > reader.BaseStream.Length) break;
            var raw = reader.ReadUInt16();
            var isHigh = (raw & 0x8000) != 0;
            var idx = raw & 0x7FFF;

            if (isHigh)
            {
                if (stripVerts == 1)
                {
                    // Second high-bit marker of a double-marker strip start: this is v1.
                    b = idx;
                    stripVerts = 2;
                }
                else
                {
                    // First marker seen after a complete strip (or the very first index):
                    // start a new strip with v0 = idx.
                    a = idx;
                    b = -1;
                    stripVerts = 1;
                }
                continue;
            }

            stripVerts++;
            if (stripVerts == 2)
            {
                // Fallback for single-marker strips (some SWBF variants / third-party exporters).
                b = idx;
            }
            else if (stripVerts >= 3 && a >= 0 && b >= 0)
            {
                var c = idx;
                // Alternate winding: odd triangle CCW (a, b, c), even flipped (a, c, b).
                if ((stripVerts & 1) == 1)
                {
                    tris.Add(a); tris.Add(b); tris.Add(c);
                }
                else
                {
                    tris.Add(a); tris.Add(c); tris.Add(b);
                }
                a = b;
                b = c;
            }
        }
        return tris;
    }

    // ── Low-level helpers ────────────────────────────────────────────────────────

    private static string ReadChunkId(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : string.Empty;
    }

    private static string ReadAsciiZ(BinaryReader reader, int size)
    {
        var bytes = reader.ReadBytes(size);
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
    }
}
