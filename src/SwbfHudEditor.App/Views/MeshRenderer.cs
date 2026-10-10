using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Layout;
using SwbfHudEditor.Core.Msh;

namespace SwbfHudEditor.App.Views;

/// <summary>A mesh as drawn for one element: the image, and where its top-left corner falls, in screen pixels.</summary>
public sealed record MeshDrawing(BitmapSource Image, int Left, int Top);

/// <summary>
/// Draws a HUD model as the game does: every vertex through the matrix <see cref="ModelProjection.MeshToView"/>
/// gives and the interface camera's perspective, clipped at the near plane, each triangle textured and
/// coloured by its vertices, nearer surfaces over farther ones. Unlit, as the HUD's models mostly are.
/// The geometry is read once per mesh, and a drawing kept per mesh, matrix and screen.
/// </summary>
public sealed class MeshRenderer
{
    /// <summary>The largest image side drawn; a model reaching far off the screen is cut there.</summary>
    private const int MaxSide = 4096;

    private readonly AssetResolver _resolver;
    private readonly TextureCache _textures;
    private readonly Dictionary<string, MshGeometry?> _geometry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texels?> _texels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DrawingKey, MeshDrawing?> _drawings = new();

    private readonly record struct DrawingKey(string Mesh, Matrix4x4 MeshToView, int Width, int Height);

    /// <summary>A texture's pixels, Bgra32, rows top to bottom.</summary>
    private sealed record Texels(byte[] Pixels, int Width, int Height);

    public MeshRenderer(AssetResolver resolver, TextureCache textures)
    {
        _resolver = resolver;
        _textures = textures;
    }

    /// <summary>The mesh's geometry, or null when it is not found or cannot be read.</summary>
    public MshGeometry? Geometry(string meshName)
    {
        if (_geometry.TryGetValue(meshName, out var cached)) return cached;
        MshGeometry? geometry = null;
        if (_resolver.FindMesh(meshName) is { } path)
        {
            try
            {
                geometry = MshParser.ExtractGeometry(path);
                if (geometry.Segments.Count == 0) geometry = null;
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException or ArgumentException)
            {
                geometry = null;
            }
        }
        _geometry[meshName] = geometry;
        return geometry;
    }

    /// <summary>The mesh drawn under <paramref name="meshToView"/> on this screen, or null when nothing of it shows.</summary>
    public MeshDrawing? Draw(string meshName, Matrix4x4 meshToView, HudScreen screen)
    {
        var key = new DrawingKey(meshName.ToLowerInvariant(), meshToView, screen.Width, screen.Height);
        if (_drawings.TryGetValue(key, out var cached)) return cached;
        if (_drawings.Count > 512) _drawings.Clear();
        var drawing = Geometry(meshName) is { } geometry ? Rasterize(geometry, meshToView, screen) : null;
        _drawings[key] = drawing;
        return drawing;
    }

    // ---- Rasterizing ----------------------------------------------------------------------------------

    /// <summary>A vertex in the camera's space with what it carries.</summary>
    private readonly record struct ViewVertex(Vector3 View, Vector2 Uv, Vector4 Color);

    /// <summary>A vertex on the screen: pixels, 1/distance, and its UV and colour divided by the distance.</summary>
    private readonly record struct ScreenVertex(float X, float Y, float InvZ, Vector2 UvOverZ, Vector4 ColorOverZ);

    private readonly record struct Triangle(ScreenVertex A, ScreenVertex B, ScreenVertex C, Texels? Texture);

    private MeshDrawing? Rasterize(MshGeometry geometry, Matrix4x4 meshToView, HudScreen screen)
    {
        var triangles = new List<Triangle>();
        var polygon = new List<ViewVertex>(4);
        foreach (var segment in geometry.Segments)
        {
            var texture = TextureOf(geometry, segment);
            var hasUv = segment.Uvs.Count == segment.Positions.Count;
            var hasColor = segment.VertexColors.Count == segment.Positions.Count;
            var view = new ViewVertex[segment.Positions.Count];
            for (var i = 0; i < view.Length; i++)
            {
                var p = segment.Positions[i];
                var uv = hasUv ? new Vector2(segment.Uvs[i].X, segment.Uvs[i].Y) : Vector2.Zero;
                var c = hasColor ? segment.VertexColors[i] : new RgbaColor(255, 255, 255, 255);
                view[i] = new ViewVertex(Vector3.Transform(new Vector3(p.X, p.Y, p.Z), meshToView), uv,
                    new Vector4(c.R, c.G, c.B, c.A) / 255f);
            }
            var indices = segment.TriangleIndices;
            for (var t = 0; t + 2 < indices.Count; t += 3)
            {
                int i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
                if (i0 >= view.Length || i1 >= view.Length || i2 >= view.Length) continue;
                polygon.Clear();
                ClipNear(view[i0], view[i1], view[i2], polygon);
                for (var k = 1; k + 1 < polygon.Count; k++)
                {
                    var triangle = new Triangle(OnScreen(polygon[0], screen), OnScreen(polygon[k], screen), OnScreen(polygon[k + 1], screen), texture);
                    if (Finite(triangle.A) && Finite(triangle.B) && Finite(triangle.C)) triangles.Add(triangle);
                }
            }
        }
        if (triangles.Count == 0) return null;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var t in triangles)
            foreach (var v in new[] { t.A, t.B, t.C })
            {
                minX = MathF.Min(minX, v.X);
                minY = MathF.Min(minY, v.Y);
                maxX = MathF.Max(maxX, v.X);
                maxY = MathF.Max(maxY, v.Y);
            }
        // Only what can be on the screen, with room for a reference that spills over its edge.
        minX = MathF.Max(minX, -screen.WidthF);
        minY = MathF.Max(minY, -screen.HeightF);
        maxX = MathF.Min(maxX, 2f * screen.WidthF);
        maxY = MathF.Min(maxY, 2f * screen.HeightF);
        var left = (int)MathF.Floor(minX);
        var top = (int)MathF.Floor(minY);
        var width = Math.Min(MaxSide, (int)MathF.Ceiling(maxX) - left);
        var height = Math.Min(MaxSide, (int)MathF.Ceiling(maxY) - top);
        if (width <= 0 || height <= 0) return null;

        var pixels = new uint[width * height];
        var depth = new float[width * height];
        Array.Fill(depth, float.MaxValue);
        foreach (var t in triangles) Fill(t, left, top, width, height, pixels, depth);

        var image = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        image.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        image.Freeze();
        return new MeshDrawing(image, left, top);
    }

    /// <summary>
    /// The part of a triangle at or beyond the near plane (distance 1, view z -1), as a polygon of 0, 3
    /// or 4 vertices, its UVs and colours cut along with it.
    /// </summary>
    private static void ClipNear(ViewVertex a, ViewVertex b, ViewVertex c, List<ViewVertex> into)
    {
        static float Beyond(ViewVertex v) => -v.View.Z - ModelProjection.NearPlane;
        static ViewVertex Cut(ViewVertex p, ViewVertex q, float dp, float dq)
        {
            var s = dp / (dp - dq);
            return new ViewVertex(Vector3.Lerp(p.View, q.View, s), Vector2.Lerp(p.Uv, q.Uv, s), Vector4.Lerp(p.Color, q.Color, s));
        }
        var corners = new[] { a, b, c };
        for (var i = 0; i < 3; i++)
        {
            var p = corners[i];
            var q = corners[(i + 1) % 3];
            float dp = Beyond(p), dq = Beyond(q);
            if (dp >= 0) into.Add(p);
            if (dp >= 0 != dq >= 0) into.Add(Cut(p, q, dp, dq));
        }
    }

    private static bool Finite(ScreenVertex v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.InvZ);

    private static ScreenVertex OnScreen(ViewVertex v, HudScreen screen)
    {
        var distance = MathF.Max(-v.View.Z, ModelProjection.NearPlane);
        var f = screen.WidthF / EngineMath.FrustumWidth;
        var invZ = 1f / distance;
        return new ScreenVertex(0.5f * screen.WidthF + f * v.View.X * invZ, 0.5f * screen.HeightF - f * v.View.Y * invZ,
            invZ, v.Uv * invZ, v.Color * invZ);
    }

    /// <summary>
    /// One triangle into the image: every pixel whose centre it covers, with UV and colour interpolated
    /// in perspective, drawn where it is no farther than what is there, blended over it.
    /// </summary>
    private static void Fill(Triangle t, int left, int top, int width, int height, uint[] pixels, float[] depth)
    {
        static float Edge(ScreenVertex a, ScreenVertex b, float px, float py) => (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);

        var area = Edge(t.A, t.B, t.C.X, t.C.Y);
        if (MathF.Abs(area) < 1e-6f) return;
        var x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(t.A.X, MathF.Min(t.B.X, t.C.X))) - left);
        var x1 = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(t.A.X, MathF.Max(t.B.X, t.C.X))) - left);
        var y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(t.A.Y, MathF.Min(t.B.Y, t.C.Y))) - top);
        var y1 = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(t.A.Y, MathF.Max(t.B.Y, t.C.Y))) - top);
        var inverseArea = 1f / area;

        for (var y = y0; y <= y1; y++)
        {
            var py = y + top + 0.5f;
            for (var x = x0; x <= x1; x++)
            {
                var px = x + left + 0.5f;
                var w0 = Edge(t.B, t.C, px, py) * inverseArea;
                var w1 = Edge(t.C, t.A, px, py) * inverseArea;
                var w2 = 1f - w0 - w1;
                if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f) continue;

                var invZ = w0 * t.A.InvZ + w1 * t.B.InvZ + w2 * t.C.InvZ;
                if (invZ <= 0f) continue;
                var distance = 1f / invZ;
                var at = y * width + x;
                if (distance > depth[at] + 1e-4f) continue;

                var color = (w0 * t.A.ColorOverZ + w1 * t.B.ColorOverZ + w2 * t.C.ColorOverZ) * distance;
                if (t.Texture is { } texture)
                {
                    var uv = (w0 * t.A.UvOverZ + w1 * t.B.UvOverZ + w2 * t.C.UvOverZ) * distance;
                    color *= Sample(texture, uv);
                }
                color = Vector4.Clamp(color, Vector4.Zero, Vector4.One);
                if (color.W <= 0f) continue;
                depth[at] = distance;
                pixels[at] = Over(color, pixels[at]);
            }
        }
    }

    /// <summary>The texture at <paramref name="uv"/>, filtered between its four nearest texels, edges held.</summary>
    private static Vector4 Sample(Texels t, Vector2 uv)
    {
        var fx = Math.Clamp(uv.X * t.Width - 0.5f, 0f, t.Width - 1f);
        var fy = Math.Clamp(uv.Y * t.Height - 0.5f, 0f, t.Height - 1f);
        var x0 = (int)fx;
        var y0 = (int)fy;
        var x1 = Math.Min(x0 + 1, t.Width - 1);
        var y1 = Math.Min(y0 + 1, t.Height - 1);
        var sx = fx - x0;
        var sy = fy - y0;
        var top = Vector4.Lerp(Texel(t, x0, y0), Texel(t, x1, y0), sx);
        var bottom = Vector4.Lerp(Texel(t, x0, y1), Texel(t, x1, y1), sx);
        return Vector4.Lerp(top, bottom, sy);
    }

    private static Vector4 Texel(Texels t, int x, int y)
    {
        var i = (y * t.Width + x) * 4;
        return new Vector4(t.Pixels[i + 2], t.Pixels[i + 1], t.Pixels[i], t.Pixels[i + 3]) / 255f;
    }

    /// <summary>A straight-alpha colour over a straight-alpha Bgra32 pixel.</summary>
    private static uint Over(Vector4 src, uint dst)
    {
        var dstA = ((dst >> 24) & 0xff) / 255f;
        var dstC = new Vector3((dst >> 16) & 0xff, (dst >> 8) & 0xff, dst & 0xff) / 255f;
        var outA = src.W + dstA * (1f - src.W);
        if (outA <= 1e-5f) return 0;
        var outC = (new Vector3(src.X, src.Y, src.Z) * src.W + dstC * dstA * (1f - src.W)) / outA;
        static uint Byte(float v) => (uint)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
        return (Byte(outA) << 24) | (Byte(outC.X) << 16) | (Byte(outC.Y) << 8) | Byte(outC.Z);
    }

    private Texels? TextureOf(MshGeometry geometry, MshSegment segment)
    {
        if (segment.MaterialIndex < 0 || segment.MaterialIndex >= geometry.MaterialTextures.Count) return null;
        var name = geometry.MaterialTextures[segment.MaterialIndex];
        if (name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) return null;
        if (_texels.TryGetValue(name, out var cached)) return cached;
        Texels? texels = null;
        if (_textures.Get(name) is { PixelWidth: > 0, PixelHeight: > 0 } bitmap)
        {
            var source = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            source.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            texels = new Texels(pixels, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        _texels[name] = texels;
        return texels;
    }
}
