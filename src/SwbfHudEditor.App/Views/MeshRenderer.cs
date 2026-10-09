using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Msh;

namespace SwbfHudEditor.App.Views;

/// <summary>
/// Renders a SWBF2 mesh to a 2D BitmapSource by hosting an offscreen WPF Viewport3D with an
/// orthographic top-down camera, mesh geometry, UV-mapped texture materials, and an ambient
/// fill light. Caches results per mesh name so we pay the cost once per mesh, not once per
/// rendered HUD element that references it.
/// </summary>
/// <summary>
/// Cached render of a mesh. The image is TIGHT-CROPPED to the actual vertex bounds (not padded
/// to a symmetric-around-origin square), and exposes:
///   <see cref="WidthUnits"/> / <see cref="HeightUnits"/> — the mesh's size in mesh-units
///   <see cref="OriginFracX"/> / <see cref="OriginFracY"/> — where mesh origin (0,0) sits in
///     the image as a fraction of image dimensions (0..1). Lets the canvas anchor the mesh's
///     ORIGIN to the HUD Position point, matching what the engine does — not the visual center
///     of an asymmetric bounding box.
///
/// For jetpackbar frame mesh (vertex X [-4.40, 3.94], Y [-1.35, -0.85]):
///   WidthUnits = 8.34, HeightUnits = 0.50
///   OriginFracX = 4.40/8.34 = 0.528 (origin is 52.8% from left)
///   OriginFracY = (-0.85 - (-1.35)) / 0.50 = 1.0... wait — computed in mesh-Y-space (Y-up).
///     In WPF Y-down-image space, OriginFracY = 0.0 (origin is at image bottom when mesh has
///     vertices entirely below origin in world-Y).
/// </summary>
public sealed record MeshRenderResult(
    BitmapSource Image,
    double WidthUnits,
    double HeightUnits,
    double OriginFracX,
    double OriginFracY);

public sealed class MeshRenderer
{
    private const int RenderTargetSize = 256;

    /// <summary>
    /// Multiplier on the auto-computed mesh-to-pixel factor.
    ///
    /// HudCanvas computes the BASE factor as <c>canvas_width / 7</c>, derived from stock content's
    /// convention that mesh extent = 7× the equivalent bitmap viewport-fraction (jetpackbar's
    /// frame mesh is 8.33 units wide while its sibling bar fill is 1.189 viewport-units wide).
    /// This multiplier lets users fine-tune for non-stock meshes that use different unit
    /// conventions. <c>1.0 = use the canvas-relative auto factor exactly</c>.
    /// </summary>
    public double MeshUnitToHudPixels { get; set; } = 1.0;

    private readonly AssetResolver _resolver;
    private readonly TextureCache _textures;
    private readonly Dictionary<string, MeshRenderResult?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public MeshRenderer(AssetResolver resolver, TextureCache textures)
    {
        _resolver = resolver;
        _textures = textures;
    }

    public MeshRenderResult? Get(string meshName)
    {
        if (_cache.TryGetValue(meshName, out var cached)) return cached;

        var meshPath = _resolver.FindMesh(meshName);
        if (meshPath is null) return Cache(meshName, null);

        try
        {
            var geom = MshParser.ExtractGeometry(meshPath);
            if (geom.Segments.Count == 0) return Cache(meshName, null);

            var rendered = Render(geom);
            return Cache(meshName, rendered);
        }
        catch
        {
            return Cache(meshName, null);
        }
    }

    private MeshRenderResult? Cache(string meshName, MeshRenderResult? result)
    {
        _cache[meshName] = result;
        return result;
    }

    private MeshRenderResult? Render(MshGeometry geom)
    {
        // Always software-rasterize. WPF 3D's DiffuseMaterial path was flaky about texture alpha
        // (HUD_LowCenter_Tooltips_Background etc. rendered with dark solid fills where the TGA's
        // alpha channel was meant to cut them out) and couldn't interpolate per-vertex colors at
        // all. The software path writes BGRA directly, handles both vertex-color gradients and
        // texture alpha in one pass, and HUD meshes are small enough (tens of verts) that perf
        // isn't a concern. Each rendered bitmap is still cached per-mesh-name.
        return RenderSoftware(geom);

        // Unreachable legacy WPF-3D path kept below for reference/rollback.
        #pragma warning disable CS0162
        var modelGroup = new Model3DGroup();
        modelGroup.Children.Add(new AmbientLight(Colors.White));

        foreach (var segment in geom.Segments)
        {
            var model = BuildSegmentModel(segment, geom.MaterialTextures);
            if (model is not null) modelGroup.Children.Add(model);
        }

        if (modelGroup.Children.Count <= 1) return null; // nothing but the light

        var (minX, maxX, minY, maxY) = GetTightBounds(geom);
        var widthUnits = maxX - minX;
        var heightUnits = maxY - minY;
        if (widthUnits <= 0 && heightUnits <= 0) return null;
        if (widthUnits <= 0) widthUnits = heightUnits;  // degenerate vertical line
        if (heightUnits <= 0) heightUnits = widthUnits; // degenerate horizontal line

        // Camera centered on the mesh's VISUAL CENTER (midpoint of tight bounds), so the RTB is
        // fully filled with mesh content — no symmetric padding around mesh origin. This lets the
        // canvas anchor the mesh's ORIGIN (0,0) at HUD Position via the OriginFrac* fractions,
        // which is what the engine does. Stock jetpack frame mesh has origin at X=0 but vertices
        // in [-4.40, 3.94] — earlier symmetric-camera code rendered an 8.80-wide RTB that was
        // ~5% wider than actual mesh content, shifting its visual center from Position.
        var centerX = (minX + maxX) * 0.5;
        var centerY = (minY + maxY) * 0.5;
        var camera = new OrthographicCamera
        {
            Position = new Point3D(centerX, centerY, 10),
            LookDirection = new Vector3D(0, 0, -1),
            UpDirection = new Vector3D(0, 1, 0),
            Width = Math.Max(0.001, widthUnits),
            NearPlaneDistance = -1000,
            FarPlaneDistance = 1000,
        };

        // Pick render-target dimensions that match the mesh's actual aspect, capped at
        // RenderTargetSize so very elongated meshes don't blow memory.
        var aspect = widthUnits / heightUnits;
        int rtW, rtH;
        if (aspect >= 1)
        {
            rtW = RenderTargetSize;
            rtH = Math.Max(8, (int)(RenderTargetSize / aspect));
        }
        else
        {
            rtH = RenderTargetSize;
            rtW = Math.Max(8, (int)(RenderTargetSize * aspect));
        }

        var viewport = new Viewport3D
        {
            Width = rtW,
            Height = rtH,
            Camera = camera,
            ClipToBounds = true,
        };
        viewport.Children.Add(new ModelVisual3D { Content = modelGroup });

        var size = new Size(rtW, rtH);
        viewport.Measure(size);
        viewport.Arrange(new Rect(size));
        viewport.UpdateLayout();

        var rtb = new RenderTargetBitmap(rtW, rtH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(viewport);
        rtb.Freeze();

        // Origin-fraction: where mesh origin (0,0) lies inside the image.
        //   X: 0 at left edge (minX), 1 at right edge (maxX). Origin at X=0 → fraction = -minX / widthUnits
        //   Y: WPF image Y is top-down. Mesh Y-up means mesh Y=maxY maps to image Y=0 (top),
        //      mesh Y=minY maps to image Y=rtH (bottom). Origin at mesh Y=0 → image fraction
        //      from top = (maxY - 0) / (maxY - minY) = maxY / heightUnits
        var originFracX = -minX / widthUnits;
        var originFracY = maxY / heightUnits;

        return new MeshRenderResult(rtb, widthUnits, heightUnits, originFracX, originFracY);
    }

    /// <summary>
    /// Software rasterization path for meshes with per-vertex colors (CLRL chunk). Walks every
    /// triangle, computes barycentric coordinates for each pixel covered, and interpolates the
    /// per-vertex color (and texture sample, if a material texture is present). Produces a true
    /// gradient instead of the averaged-tint WPF-3D approximation.
    ///
    /// Only used when the mesh has CLRL data. Implementation is straightforward barycentric
    /// rasterization — not the fastest, but HUD meshes are tiny (tens of vertices) and we cache
    /// the resulting bitmap per-mesh so cost is paid once.
    /// </summary>
    private MeshRenderResult? RenderSoftware(MshGeometry geom)
    {
        var (minX, maxX, minY, maxY) = GetTightBounds(geom);
        var widthUnits = maxX - minX;
        var heightUnits = maxY - minY;
        if (widthUnits <= 0 && heightUnits <= 0) return null;
        if (widthUnits <= 0) widthUnits = heightUnits;
        if (heightUnits <= 0) heightUnits = widthUnits;

        var aspect = widthUnits / heightUnits;
        int rtW, rtH;
        if (aspect >= 1) { rtW = RenderTargetSize; rtH = Math.Max(8, (int)(RenderTargetSize / aspect)); }
        else             { rtH = RenderTargetSize; rtW = Math.Max(8, (int)(RenderTargetSize * aspect)); }

        // BGRA pixel buffer with straight (non-premultiplied) alpha; converted to Bgra32
        // WriteableBitmap at the end. Initialized to 0 (fully transparent).
        var pixels = new uint[rtW * rtH];

        foreach (var segment in geom.Segments)
        {
            if (segment.Positions.Count == 0 || segment.TriangleIndices.Count < 3) continue;

            // If the segment has per-vertex colors, use them; otherwise assume solid white so
            // the texture passes through unchanged. "Solid white" means no tint and full opacity
            // from the vertex-color side, leaving the texture's own alpha to control transparency.
            var hasColors = segment.VertexColors.Count == segment.Positions.Count
                            && segment.VertexColors.Count > 0;
            var defaultColor = new RgbaColor(255, 255, 255, 255);

            // Fetch the material's texture once per segment.
            BitmapSource? tex = null;
            if (segment.MaterialIndex >= 0 && segment.MaterialIndex < geom.MaterialTextures.Count)
            {
                var texName = geom.MaterialTextures[segment.MaterialIndex];
                if (texName.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) texName = texName[..^4];
                tex = _textures.Get(texName);
            }
            int texW = tex?.PixelWidth ?? 0, texH = tex?.PixelHeight ?? 0;
            byte[]? texPx = null;
            int texStride = 0;
            if (tex is not null)
            {
                var wb = tex.Format == PixelFormats.Bgra32 || tex.Format == PixelFormats.Pbgra32
                    ? tex
                    : new FormatConvertedBitmap(tex, PixelFormats.Bgra32, null, 0);
                texStride = texW * 4;
                texPx = new byte[texStride * texH];
                wb.CopyPixels(texPx, texStride, 0);
            }

            // Rasterize each triangle.
            for (int i = 0; i + 2 < segment.TriangleIndices.Count; i += 3)
            {
                int i0 = segment.TriangleIndices[i];
                int i1 = segment.TriangleIndices[i + 1];
                int i2 = segment.TriangleIndices[i + 2];
                if (i0 >= segment.Positions.Count || i1 >= segment.Positions.Count || i2 >= segment.Positions.Count)
                    continue;

                // Project mesh-space vertex to bitmap pixel coords. minX→0, maxX→rtW; minY→rtH
                // (image Y grows down, mesh Y grows up).
                static (double X, double Y) Project(Vec3 v, double minX, double maxX, double minY, double maxY, int rtW, int rtH)
                {
                    var px = (v.X - minX) / (maxX - minX) * rtW;
                    var py = (maxY - v.Y) / (maxY - minY) * rtH;
                    return (px, py);
                }
                var v0 = Project(segment.Positions[i0], minX, maxX, minY, maxY, rtW, rtH);
                var v1 = Project(segment.Positions[i1], minX, maxX, minY, maxY, rtW, rtH);
                var v2 = Project(segment.Positions[i2], minX, maxX, minY, maxY, rtW, rtH);

                var c0 = hasColors ? segment.VertexColors[i0] : defaultColor;
                var c1 = hasColors ? segment.VertexColors[i1] : defaultColor;
                var c2 = hasColors ? segment.VertexColors[i2] : defaultColor;
                var uv0 = i0 < segment.Uvs.Count ? segment.Uvs[i0] : default;
                var uv1 = i1 < segment.Uvs.Count ? segment.Uvs[i1] : default;
                var uv2 = i2 < segment.Uvs.Count ? segment.Uvs[i2] : default;
                var hasUv = segment.Uvs.Count == segment.Positions.Count;

                RasterizeTriangle(pixels, rtW, rtH, v0, v1, v2, c0, c1, c2, uv0, uv1, uv2,
                    hasUv, texPx, texW, texH, texStride);
            }
        }

        // Convert uint BGRA buffer to a WriteableBitmap.
        var wbOut = new System.Windows.Media.Imaging.WriteableBitmap(rtW, rtH, 96, 96, PixelFormats.Bgra32, null);
        wbOut.WritePixels(new System.Windows.Int32Rect(0, 0, rtW, rtH), pixels, rtW * 4, 0);
        wbOut.Freeze();

        var originFracX = -minX / widthUnits;
        var originFracY = maxY / heightUnits;
        return new MeshRenderResult(wbOut, widthUnits, heightUnits, originFracX, originFracY);
    }

    /// <summary>
    /// Rasterize one triangle into <paramref name="pixels"/> using barycentric color (and UV
    /// if a texture is supplied) interpolation. Alpha-blends the triangle over whatever is
    /// already written — so overlapping segments composite naturally.
    /// </summary>
    private static void RasterizeTriangle(
        uint[] pixels, int rtW, int rtH,
        (double X, double Y) v0, (double X, double Y) v1, (double X, double Y) v2,
        RgbaColor c0, RgbaColor c1, RgbaColor c2,
        Vec2 uv0, Vec2 uv1, Vec2 uv2,
        bool hasUv, byte[]? texPx, int texW, int texH, int texStride)
    {
        static double Edge((double X, double Y) a, (double X, double Y) b, double px, double py) =>
            (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);

        var minX = Math.Max(0, (int)Math.Floor(Math.Min(v0.X, Math.Min(v1.X, v2.X))));
        var maxX = Math.Min(rtW - 1, (int)Math.Ceiling(Math.Max(v0.X, Math.Max(v1.X, v2.X))));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(v0.Y, Math.Min(v1.Y, v2.Y))));
        var maxY = Math.Min(rtH - 1, (int)Math.Ceiling(Math.Max(v0.Y, Math.Max(v1.Y, v2.Y))));

        var area = Edge(v0, v1, v2.X, v2.Y);
        if (Math.Abs(area) < 0.0001) return; // degenerate triangle
        var invArea = 1.0 / area;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var px = x + 0.5;
                var py = y + 0.5;
                var w0 = Edge(v1, v2, px, py) * invArea;
                var w1 = Edge(v2, v0, px, py) * invArea;
                var w2 = 1.0 - w0 - w1;
                if (w0 < -0.0001 || w1 < -0.0001 || w2 < -0.0001) continue;

                var r = Math.Clamp(w0 * c0.R + w1 * c1.R + w2 * c2.R, 0, 255);
                var g = Math.Clamp(w0 * c0.G + w1 * c1.G + w2 * c2.G, 0, 255);
                var b = Math.Clamp(w0 * c0.B + w1 * c1.B + w2 * c2.B, 0, 255);
                var a = Math.Clamp(w0 * c0.A + w1 * c1.A + w2 * c2.A, 0, 255);

                if (hasUv && texPx is not null && texW > 0 && texH > 0)
                {
                    var u = w0 * uv0.X + w1 * uv1.X + w2 * uv2.X;
                    var v = w0 * uv0.Y + w1 * uv1.Y + w2 * uv2.Y;
                    var tx = Math.Clamp((int)(u * texW), 0, texW - 1);
                    var ty = Math.Clamp((int)(v * texH), 0, texH - 1);
                    var tIdx = ty * texStride + tx * 4;
                    // Texture in Bgra32: [B, G, R, A]
                    var tb = texPx[tIdx];
                    var tg = texPx[tIdx + 1];
                    var tr = texPx[tIdx + 2];
                    var ta = texPx[tIdx + 3];
                    // Multiply vertex color × texture color
                    r = (r * tr) / 255.0;
                    g = (g * tg) / 255.0;
                    b = (b * tb) / 255.0;
                    a = (a * ta) / 255.0;
                }

                int pi = y * rtW + x;
                uint existing = pixels[pi];
                if (existing == 0)
                {
                    pixels[pi] = ((uint)(byte)a << 24) | ((uint)(byte)r << 16) | ((uint)(byte)g << 8) | (byte)b;
                }
                else
                {
                    // Source-over blending: out = src*srcA + dst*(1-srcA)
                    var srcA = a / 255.0;
                    var dstA = ((existing >> 24) & 0xff) / 255.0;
                    var dstR = (existing >> 16) & 0xff;
                    var dstG = (existing >> 8) & 0xff;
                    var dstB = existing & 0xff;
                    var outA = srcA + dstA * (1 - srcA);
                    if (outA <= 0.0001) continue;
                    var outR = (r * srcA + dstR * dstA * (1 - srcA)) / outA;
                    var outG = (g * srcA + dstG * dstA * (1 - srcA)) / outA;
                    var outB = (b * srcA + dstB * dstA * (1 - srcA)) / outA;
                    pixels[pi] = ((uint)(byte)(outA * 255) << 24)
                        | ((uint)(byte)Math.Clamp(outR, 0, 255) << 16)
                        | ((uint)(byte)Math.Clamp(outG, 0, 255) << 8)
                        | (byte)Math.Clamp(outB, 0, 255);
                }
            }
        }
    }

    /// <summary>
    /// Tight (min, max) vertex bounds of the mesh's XY footprint. Tight = actual range of
    /// vertices, NOT padded to symmetric around origin. The mesh's origin (0,0) may fall
    /// anywhere inside this box (or even outside it, for meshes modeled far from origin).
    /// </summary>
    private static (double MinX, double MaxX, double MinY, double MaxY) GetTightBounds(MshGeometry geom)
    {
        if (geom.BoundingBox is { } bb)
        {
            var minX = bb.Center.X - Math.Abs(bb.Extents.X);
            var maxX = bb.Center.X + Math.Abs(bb.Extents.X);
            var minY = bb.Center.Y - Math.Abs(bb.Extents.Y);
            var maxY = bb.Center.Y + Math.Abs(bb.Extents.Y);
            if (maxX > minX || maxY > minY) return (minX, maxX, minY, maxY);
        }

        double minX2 = double.PositiveInfinity, maxX2 = double.NegativeInfinity;
        double minY2 = double.PositiveInfinity, maxY2 = double.NegativeInfinity;
        foreach (var seg in geom.Segments)
            foreach (var p in seg.Positions)
            {
                if (p.X < minX2) minX2 = p.X;
                if (p.X > maxX2) maxX2 = p.X;
                if (p.Y < minY2) minY2 = p.Y;
                if (p.Y > maxY2) maxY2 = p.Y;
            }
        if (double.IsInfinity(minX2)) return (0, 0, 0, 0);
        return (minX2, maxX2, minY2, maxY2);
    }

    private GeometryModel3D? BuildSegmentModel(MshSegment segment, IReadOnlyList<string> materials)
    {
        if (segment.Positions.Count == 0 || segment.TriangleIndices.Count == 0) return null;

        var mesh = new MeshGeometry3D();
        foreach (var p in segment.Positions) mesh.Positions.Add(new Point3D(p.X, p.Y, p.Z));
        foreach (var t in segment.TriangleIndices) mesh.TriangleIndices.Add(t);
        if (segment.Uvs.Count == segment.Positions.Count)
        {
            foreach (var uv in segment.Uvs) mesh.TextureCoordinates.Add(new Point(uv.X, uv.Y));
        }

        // Mix in vertex-color tint+opacity. Meshes like hud_weapon_backdrop have NO alpha-channel
        // texture — their transparency lives entirely in per-vertex RGBA painted at modeling
        // time. For a flat 2D preview, averaging the vertex colors into a single RGBA tint is a
        // reasonable approximation (all vertices usually share close values within a single
        // segment anyway). A true gradient-fade would need Helix Toolkit or a custom shader.
        var vertexTint = AverageVertexColor(segment.VertexColors);

        Material material;
        if (segment.MaterialIndex >= 0 && segment.MaterialIndex < materials.Count)
        {
            var texName = materials[segment.MaterialIndex];
            if (texName.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                texName = texName[..^4];
            var bmp = _textures.Get(texName);
            Brush diffuseBrush;
            if (bmp is not null)
            {
                diffuseBrush = new ImageBrush(bmp)
                {
                    Stretch = Stretch.Fill,
                    ViewportUnits = BrushMappingMode.Absolute,
                };
            }
            else
            {
                diffuseBrush = Brushes.White;
            }
            // Apply vertex-color alpha to the whole segment's opacity; apply RGB tint by color-
            // multiplying it against the brush via an OpacityMask trick when needed. For the
            // common case of white-painted-with-alpha vertices, just lowering opacity on the
            // DiffuseMaterial is enough.
            if (vertexTint is { } vt)
            {
                diffuseBrush = diffuseBrush.Clone();
                diffuseBrush.Opacity = vt.A / 255.0;
                material = new DiffuseMaterial(diffuseBrush) { Color = System.Windows.Media.Color.FromRgb(vt.R, vt.G, vt.B) };
            }
            else
            {
                material = new DiffuseMaterial(diffuseBrush);
            }
        }
        else
        {
            material = vertexTint is { } v
                ? new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromArgb(v.A, v.R, v.G, v.B)))
                : new DiffuseMaterial(Brushes.White);
        }

        return new GeometryModel3D(mesh, material)
        {
            BackMaterial = material, // double-sided so winding issues don't hide faces
        };
    }

    /// <summary>Average the R/G/B/A channels of a segment's vertex-color list. Returns null if
    /// the list is empty (no CLRL chunk) so callers can skip the vertex-color tint path.</summary>
    private static RgbaColor? AverageVertexColor(IReadOnlyList<RgbaColor> colors)
    {
        if (colors.Count == 0) return null;
        long r = 0, g = 0, b = 0, a = 0;
        foreach (var c in colors) { r += c.R; g += c.G; b += c.B; a += c.A; }
        var n = colors.Count;
        return new RgbaColor((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
    }

}
