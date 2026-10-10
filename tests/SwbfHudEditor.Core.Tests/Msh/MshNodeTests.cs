using System.Text;
using SwbfHudEditor.Core.Msh;

namespace SwbfHudEditor.Core.Tests.Msh;

/// <summary>Segments placed by their node's transform, chained up the parents, as the game's default pose places them.</summary>
public class MshNodeTests
{
    private static byte[] Chunk(string id, params byte[][] payload)
    {
        var body = payload.SelectMany(p => p).ToArray();
        return Encoding.ASCII.GetBytes(id).Concat(BitConverter.GetBytes((uint)body.Length)).Concat(body).ToArray();
    }

    private static byte[] Text(string id, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value + "\0");
        return Chunk(id, bytes.Concat(new byte[(4 - bytes.Length % 4) % 4]).ToArray());
    }

    private static byte[] Floats(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();

    private static byte[] Node(string name, string? parent, float tx, float ty, float tz, uint flags = 0, float scale = 1f) =>
        Chunk("MODL",
            Chunk("MTYP", BitConverter.GetBytes(4)),
            Text("NAME", name),
            parent is null ? Array.Empty<byte>() : Text("PRNT", parent),
            Chunk("FLGS", BitConverter.GetBytes(flags)),
            Chunk("TRAN", Floats(scale, scale, scale, 0, 0, 0, 1, tx, ty, tz)),
            Chunk("GEOM",
                Chunk("SEGM",
                    Chunk("MATI", BitConverter.GetBytes(0)),
                    Chunk("POSL", BitConverter.GetBytes(3), Floats(0, 0, 0, 1, 0, 0, 0, 1, 0)),
                    Chunk("NDXT", BitConverter.GetBytes(1), new byte[] { 0, 0, 1, 0, 2, 0 }))));

    private static MshGeometry Parse(params byte[][] nodes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"visualhud-{Guid.NewGuid():N}.msh");
        File.WriteAllBytes(path, Chunk("HEDR", Chunk("MSH2", nodes)));
        try { return MshParser.ExtractGeometry(path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_Child_Node_Adds_Its_Parents_Transform_And_A_Hidden_Node_Is_Left_Out()
    {
        var g = Parse(
            Node("root", null, 1, 2, 0, scale: 2f),
            Node("child", "root", 0.5f, 0, 0),
            Node("collision", "root", 0, 0, 0, flags: 1));

        Assert.Equal(2, g.Segments.Count);
        // root: scaled by 2, then moved by (1, 2): (1, 0, 0) lands at (3, 2).
        Assert.Equal(new Vec3(3, 2, 0), g.Segments[0].Positions[1]);
        // child: its own 0.5 along x, then the root's scale and move: (1, 0, 0) -> (1.5, 0, 0) -> (4, 2).
        Assert.Equal(new Vec3(4, 2, 0), g.Segments[1].Positions[1]);
    }
}
