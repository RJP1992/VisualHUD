using System.Numerics;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Edits;

/// <summary>What rewriting a file for a screen shape did: its edits (made already), the screen it keeps the look at, what it left.</summary>
public sealed record RatioMigration(IReadOnlyList<IHudEdit> Edits, HudScreen Reference, int Lines, IReadOnlyList<string> Notes);

/// <summary>
/// Rewrite a file for GameExt's <c>AuthoredRatio(w, h)</c> so that on a screen of that shape (the reference: the
/// canvas's height, that shape's width) it draws as it does now, whatever laid it out before: the stock layout,
/// <c>TrueWidescreen(1)</c> or another ratio. On a screen of its own shape GameExt draws such a file as written,
/// so the numbers become what is drawn now: each position where it puts its element on the screen, each
/// bitmap's and group's size as drawn, through the matrices of the parents as they become. Elements go a level
/// at a time, each against its parents already rewritten. Positions and sizes are written in Viewport (Frame
/// stays Frame, a VehicleSeating's own Position stays pixels, a MeshInfo keeps its unit, as a table another
/// model inherits is converted again); text and models keep their own shape, which the stock layout squashed.
/// A ring of segments keeps its middle and its radii (the stock layout stretched its height as bitmaps'); an
/// element without a Position that would move gets one; a group that follows a position GameExt tracks keeps
/// its own, a share of the real screen, and its children go with it.
/// </summary>
public static class HudRatioMigration
{
    /// <summary>Why <paramref name="file"/> cannot be laid out for w:h, or null.</summary>
    public static string? WhyNot(HudDocument file, int w, int h)
    {
        if (w <= 0 || h <= 0 || !TrueWidescreen.ValidRatio(w / (float)h)) return "AuthoredRatio takes shapes from square to 4:1.";
        var viewports = file.TopLevel.Where(b => b.HasBody && b.Name == "FileInfo")
            .SelectMany(b => b.Children).LastOrDefault(l => !l.HasBody && l.Name == "Viewports");
        if (viewports is not null && viewports.Arguments.Count > 0 &&
            !viewports.Arguments.Any(a => EngineArgs.IsNumber(a) && EngineArgs.Float(a) == 1f))
            return "GameExt lays a file out by its AuthoredRatio only with one viewport, and this file is not for one.";
        return HudLayout.TopLevelElements(file).Any() ? null : "The file has no elements to lay out.";
    }

    /// <summary>The screen a w:h rewrite keeps the look at: <paramref name="height"/> tall, that shape wide.</summary>
    public static HudScreen ReferenceFor(int w, int h, int height) => new((int)Math.Round(height * (double)w / h), height);

    /// <summary>
    /// Rewrite <paramref name="file"/>, one of <paramref name="documents"/> (all the HUD loads, in order), for
    /// <c>AuthoredRatio(w, h)</c>, keeping its look at <see cref="ReferenceFor"/>. <paramref name="options"/>
    /// gives the fonts and textures; the layouts use GameExt's, at the load state.
    /// </summary>
    public static RatioMigration Migrate(IReadOnlyList<HudDocument> documents, HudDocument file, int w, int h, int height,
        LayoutOptions options)
    {
        var reference = ReferenceFor(w, h, height);
        var at = options with
        {
            Screen = reference,
            Target = HudTarget.GameExt,
            Rounding = false,
            Viewports = 1,
            ActiveViewport = 1,
            Simulation = null,
            ShowByEvents = false,
            BarValues = null,
        };

        // As it draws now.
        var old = HudLayout.Lay(documents, at);
        var points = new Dictionary<HudBlock, Vector2>(ReferenceEqualityComparer.Instance);
        var sizes = new Dictionary<HudBlock, Vector2>(ReferenceEqualityComparer.Instance);
        var notes = new List<string>();
        var rings = new Dictionary<HudBlock, Vector2>(ReferenceEqualityComparer.Instance);
        var made = new Dictionary<HudBlock, HudBlock>(ReferenceEqualityComparer.Instance);
        // Where each element's parent had its origin: a parent that moves on purpose (one GameExt tracks) takes
        // its children with it.
        var parentOrigins = new Dictionary<HudBlock, Vector2>(ReferenceEqualityComparer.Instance);
        var tracked = 0;
        var stuck = 0;
        var elements = Own(old, file).ToList();
        // The stretch bitmaps get, which a ring of segments gets on its height too.
        var stretch = old.LayoutOf(file) is { Opted: true } ? 1f : EngineMath.BitmapStretch(reference);
        foreach (var e in elements)
        {
            if (ParentToScreen(e) is not { } parent)
            {
                stuck++;
                continue;
            }
            foreach (var (line, role) in PositionLines(e)) points[line] = OnScreen(PixelsOf(line, role, e), parent);
            if (e.Parent is { } up) parentOrigins[e.Block] = OnScreen(Vector3.Zero, up.ToScreen);
            if (Tracked(e)) tracked++;
            // Without a Position the element is at its parent's origin, which may not stay where it is (the stock
            // layout's band starts below the top): a line is made for it, added if it is needed. A target is
            // placed by code where the game projects what it marks, and a map by its mode's position.
            if (Last(e.Block, "Position") is null && e.Kind is not (ElementKind.Target or ElementKind.Map) && !Tracked(e))
            {
                var line = new HudBlock("Position", HudArgument.FromInt(0), HudArgument.FromInt(0), HudArgument.FromInt(0));
                made[e.Block] = line;
                points[line] = OnScreen(Vector3.Zero, parent);
            }
            if (SizeLine(e) is { } size && DrawnSize(e) is { } drawn) sizes[size] = drawn;
            // A ring of segments keeps its centre and its radii as drawn, its height stretched as bitmaps are.
            if (IsRing(e) && SizeLine(e) is { } rect)
            {
                var (across, down) = Axes(e.ToScreen);
                rings[e.Block] = OnScreen(new Vector3(e.RectWidthPixels / 2, e.RectHeightPixels / 2, 0f), e.ToScreen);
                sizes[rect] = new Vector2(e.RectWidthPixels * across, e.RectHeightPixels * stretch * down);
            }
            foreach (var (line, piece) in TemplateSizes(old, e))
                if (piece is not null) sizes[line] = piece.Value;
        }
        var stockShapes = !old.LayoutOf(file)?.Opted ?? true;

        var edits = new List<IHudEdit>();
        void Apply(IHudEdit edit)
        {
            edit.Apply();
            edits.Add(edit);
        }
        SetRatio(file, w, h, Apply);

        // A level at a time, each against its parents as they now draw.
        var lines = 0;
        var deepest = elements.Count == 0 ? -1 : elements.Max(e => e.Depth);
        for (var depth = 0; depth <= deepest; depth++)
        {
            var now = HudLayout.Lay(documents, at);
            foreach (var e in Own(now, file).Where(e => e.Depth == depth))
            {
                if (ParentToScreen(e) is not { } parent) continue;
                var shift = e.Parent is { } up && parentOrigins.TryGetValue(e.Block, out var was)
                    ? OnScreen(Vector3.Zero, up.ToScreen) - was
                    : Vector2.Zero;
                foreach (var (line, role) in PositionLines(e))
                    if (points.TryGetValue(line, out var point) &&
                        Place(line, role, e, ((role == Role.Own ? RingPoint(e, rings, sizes, parent) : null) ?? point) + shift, parent) is { } args)
                    {
                        Apply(new ReplaceArgumentsEdit(line, args, e.Block));
                        lines++;
                    }
                if (made.TryGetValue(e.Block, out var added) && points.TryGetValue(added, out var at0) &&
                    Place(added, e.Kind == ElementKind.VehicleSeating ? Role.Seating : Role.Own, e,
                        (RingPoint(e, rings, sizes, parent) ?? at0) + shift, parent) is { } placed &&
                    (MathF.Abs(EngineArgs.Float(placed[0])) > 1e-4f || MathF.Abs(EngineArgs.Float(placed[1])) > 1e-4f))
                {
                    added.Arguments.Clear();
                    added.Arguments.AddRange(placed);
                    var index = e.Block.Children.TakeWhile(c => !c.HasBody).Count();
                    Apply(new InsertBlockEdit(e.Block.Children, index, added, "add Position", affected: e.Block));
                    lines++;
                }
                if (SizeLine(e) is { } size && sizes.TryGetValue(size, out var drawn) && Size(size, drawn, e.ToScreen, e.ViewWidth, e.ViewHeight) is { } sized)
                {
                    Apply(new ReplaceArgumentsEdit(size, sized, e.Block));
                    lines++;
                }
                foreach (var (line, axes) in TemplateAxes(now, e))
                    if (axes is { } a && sizes.TryGetValue(line, out var shown) && Size(line, shown, a.ToScreen, a.ViewWidth, a.ViewHeight) is { } resized)
                    {
                        Apply(new ReplaceArgumentsEdit(line, resized, e.Block));
                        lines++;
                    }
            }
        }

        if (stockShapes && Math.Abs(w / (double)h - 4.0 / 3.0) > 1e-4)
            notes.Add("Text and models keep their own shape, which the stock layout squashed.");
        if (tracked > 0 && stockShapes)
            notes.Add($"{tracked} group{(tracked == 1 ? " follows a position" : "s follow positions")} GameExt tracks (a reticule, a lock-on, a " +
                      "command post) and keep their Position: GameExt puts them on the real point, which the stock layout missed.");
        if (stuck > 0)
            notes.Add($"{stuck} element{(stuck == 1 ? " is" : "s are")} scaled to nothing and kept as written.");
        return new RatioMigration(edits, reference, lines, notes);
    }

    // ---- What is placed and sized ---------------------------------------------------------------------------

    /// <summary>The file's own elements, not the pieces code makes of their templates.</summary>
    private static IEnumerable<LaidOutElement> Own(HudLayoutResult layout, HudDocument file) =>
        layout.All.Where(e => ReferenceEquals(e.Document, file) && e.Piece is null);

    /// <summary>
    /// The lines that place an element in its parent: its last <c>Position</c>, a map's positions for each
    /// mode, and each <c>MeshInfo</c>'s, which replaces a model's matrix.
    /// </summary>
    private static IEnumerable<(HudBlock Line, Role Role)> PositionLines(LaidOutElement e)
    {
        var block = e.Block;
        if (Last(block, "Position") is { } own && !Tracked(e))
            yield return (own, e.Kind == ElementKind.VehicleSeating ? Role.Seating : Role.Own);
        if (e.Kind == ElementKind.Map)
            foreach (var name in new[] { "PositionSmall", "PositionLarge", "PositionSpawn" })
                if (Last(block, name) is { } line) yield return (line, Role.Mode);
        if (e.Kind == ElementKind.Model3D)
            foreach (var info in block.Children.Where(c => c.HasBody && c.Name == "MeshInfo"))
                if (Last(info, "Position") is { } line) yield return (line, Role.MeshInfo);
    }

    /// <summary>
    /// A group that follows a position GameExt tracks (a reticule, a command post): in a file it lays out, the
    /// written Position, the place until the first event, is a share of the real screen whatever the shape.
    /// </summary>
    private static bool Tracked(LaidOutElement e) =>
        e.Block.Children.Any(c => !c.HasBody && c.Name == "EventPosition" && EngineArgs.String(EngineArgs.At(c, 0) ?? HudArgument.FromInt(0)) is { } name &&
                                  HudLayout.IsTracked(name));

    /// <summary>
    /// What a position line is: an element's own; a seating chart's own, read as pixels; a map's for one of its
    /// modes; a MeshInfo's, whose table another model may inherit and convert again.
    /// </summary>
    private enum Role { Own, Seating, Mode, MeshInfo }

    /// <summary>An element's own size line: a bitmap's <c>BitmapRect</c>, a group's <c>Rect</c>.</summary>
    private static HudBlock? SizeLine(LaidOutElement e) =>
        ElementKinds.IsBitmapBased(e.Kind) ? Last(e.Block, "BitmapRect")
        : ElementKinds.IsGroupBased(e.Kind) ? Last(e.Block, "Rect")
        : null;

    private static HudBlock? Last(HudBlock block, string name) =>
        block.Children.LastOrDefault(c => !c.HasBody && c.Name == name && c.Arguments.Count >= 2 &&
                                          EngineArgs.IsNumber(c.Arguments[0]) && EngineArgs.IsNumber(c.Arguments[1]));

    /// <summary>Where a position line puts its element, in its parent's pixels.</summary>
    private static Vector3 PixelsOf(HudBlock line, Role role, LaidOutElement e)
    {
        var x = EngineArgs.FloatAt(line, 0);
        var y = EngineArgs.FloatAt(line, 1);
        var z = EngineArgs.FloatAt(line, 2);
        // VehicleSeating::PostReadSetup reads its own Position as pixels whatever its mode.
        if (role == Role.Seating) return new Vector3(x, y, z);
        var mode = EngineArgs.PositionMode(EngineArgs.At(line, 3));
        return new Vector3(
            EngineMath.ToPixels(mode, x, e.FrameWidth, e.ViewWidth, e.ScreenWidth),
            EngineMath.ToPixels(mode, y, e.FrameHeight, e.ViewHeight, e.ScreenHeight), z);
    }

    /// <summary>The matrix an element's parent hands it, to the screen: its own matrix taken off its draw.</summary>
    private static Matrix4x4? ParentToScreen(LaidOutElement e)
    {
        if (!Matrix4x4.Invert(e.Local, out var inverse)) return null;
        var parent = inverse * e.ToScreen;
        return MathF.Abs(Determinant2(parent)) > 1e-9f ? parent : null;
    }

    private static float Determinant2(Matrix4x4 m) => m.M11 * m.M22 - m.M12 * m.M21;

    private static Vector2 OnScreen(Vector3 pixels, Matrix4x4 parent)
    {
        var p = Vector3.Transform(pixels, parent);
        return new Vector2(p.X, p.Y);
    }

    /// <summary>An element's size as drawn, along its own axes: a bitmap's whole quad, a group's Rect.</summary>
    private static Vector2? DrawnSize(LaidOutElement e)
    {
        var (across, down) = Axes(e.ToScreen);
        if (ElementKinds.IsBitmapBased(e.Kind) && (e.FullQuad ?? e.Quad) is { } q) return new Vector2(q.Width * across, q.Height * down);
        if (ElementKinds.IsGroupBased(e.Kind)) return new Vector2(e.RectWidthPixels * across, e.RectHeightPixels * down);
        return null;
    }

    private static bool IsRing(LaidOutElement e) => e.Kind == ElementKind.BarSegmented && e.Spec.Circular;

    private static readonly float[] AlignShare = { 0f, 0.5f, 1f };

    /// <summary>
    /// For a ring's own Position: the screen point it must put, so that with its Rect for the ring's drawn radii
    /// the ring's centre, the Rect's middle, lands where it was. The Rect's alignment moves the origin in the
    /// parent's pixels; the middle is then turned and scaled with the bar.
    /// </summary>
    private static Vector2? RingPoint(LaidOutElement e, Dictionary<HudBlock, Vector2> rings,
        Dictionary<HudBlock, Vector2> sizes, Matrix4x4 parent)
    {
        if (!rings.TryGetValue(e.Block, out var centre) || SizeLine(e) is not { } rect || !sizes.TryGetValue(rect, out var size)) return null;
        var (across, down) = Axes(e.ToScreen);
        if (across <= 0f || down <= 0f) return null;
        var w = size.X / across;
        var h = size.Y / down;
        var l = e.Local;
        var half = new Vector2(w / 2, h / 2);
        var offset = new Vector2(half.X * l.M11 + half.Y * l.M21, half.X * l.M12 + half.Y * l.M22) -
                     new Vector2(AlignShare[(int)e.Spec.RectHAlign] * w, AlignShare[(int)e.Spec.RectVAlign] * h);
        return centre - new Vector2(offset.X * parent.M11 + offset.Y * parent.M21, offset.X * parent.M12 + offset.Y * parent.M22);
    }

    private static (float Across, float Down) Axes(Matrix4x4 m) =>
        (MathF.Sqrt(m.M11 * m.M11 + m.M12 * m.M12), MathF.Sqrt(m.M21 * m.M21 + m.M22 * m.M22));

    /// <summary>
    /// The <c>BitmapRect</c> of each template code draws copies of (a target's markers, a map's icons, a seating
    /// chart's), and of a segmented bar's <c>Segment</c>, with the size a copy is drawn at.
    /// </summary>
    private static IEnumerable<(HudBlock Line, Vector2? Drawn)> TemplateSizes(HudLayoutResult layout, LaidOutElement e)
    {
        foreach (var (line, axes) in TemplateAxes(layout, e))
        {
            if (axes is not { } a) continue;
            var (across, down) = Axes(a.ToScreen);
            yield return (line, a.Quad is { } q ? new Vector2(q.Width * across, q.Height * down) : null);
        }
    }

    private readonly record struct Drawn(Matrix4x4 ToScreen, QuadRect? Quad, float ViewWidth, float ViewHeight);

    private static IEnumerable<(HudBlock Line, Drawn? Axes)> TemplateAxes(HudLayoutResult layout, LaidOutElement e)
    {
        foreach (var template in e.Block.Children.Where(c => c.HasBody && !ElementKinds.TryGet(c, out _) && c.Name != "MeshInfo"))
        {
            if (Last(template, "BitmapRect") is not { } line) continue;
            if (template.Name == "Segment" && e.Kind == ElementKind.BarSegmented)
            {
                // Along the bar's own axes: a segment turned round a ring under the stock band is skewed, each its own way.
                yield return (line, e.SegmentQuad is { } sq ? new Drawn(e.ToScreen, sq, e.ViewWidth, e.ViewHeight) : null);
                continue;
            }
            var piece = layout.All.FirstOrDefault(p => ReferenceEquals(p.Piece?.Template, template) && (p.FullQuad ?? p.Quad) is { Width: > 0 });
            yield return (line, piece is null ? null : new Drawn(piece.ToScreen, piece.FullQuad ?? piece.Quad, piece.ViewWidth, piece.ViewHeight));
        }
    }

    // ---- Writing --------------------------------------------------------------------------------------------

    /// <summary>
    /// The arguments that put a position line's element at <paramref name="point"/> on the screen through its
    /// parent's matrix as it now draws: z kept, in Viewport (Frame, a seating chart's pixels and a MeshInfo's
    /// own unit kept), or null when nothing moves.
    /// </summary>
    private static List<HudArgument>? Place(HudBlock line, Role role, LaidOutElement e, Vector2 point, Matrix4x4 parent)
    {
        var z = EngineArgs.FloatAt(line, 2);
        var bx = point.X - z * parent.M31 - parent.M41;
        var by = point.Y - z * parent.M32 - parent.M42;
        var det = Determinant2(parent);
        var x = (bx * parent.M22 - by * parent.M21) / det;
        var y = (by * parent.M11 - bx * parent.M12) / det;

        var current = EngineArgs.PositionMode(EngineArgs.At(line, 3));
        var mode = role == Role.Seating ? RelativeMode.Pixels
            : role == Role.MeshInfo || current == RelativeMode.Frame ? current
            : RelativeMode.Viewport;
        var args = new List<HudArgument>
        {
            HudArgument.FromFloat(EngineMath.ToRelative(mode, x, e.FrameWidth, e.ViewWidth, e.ScreenWidth)),
            HudArgument.FromFloat(EngineMath.ToRelative(mode, y, e.FrameHeight, e.ViewHeight, e.ScreenHeight)),
            EngineArgs.At(line, 2) ?? HudArgument.FromInt(0),
        };
        // A unit left out stays left out where it is the one kept.
        if (line.Arguments.Count > 3 || mode != current) args.Add(HudArgument.FromString(EngineArgs.RelativeModeNames[(int)mode]));
        args.AddRange(line.Arguments.Skip(4));
        return Same(args, line) ? null : args;
    }

    /// <summary>A size line's arguments for <paramref name="drawn"/> screen pixels along these axes, in Viewport.</summary>
    private static List<HudArgument>? Size(HudBlock line, Vector2 drawn, Matrix4x4 toScreen, float viewWidth, float viewHeight)
    {
        var (across, down) = Axes(toScreen);
        if (across <= 0f || down <= 0f || viewWidth <= 0f || viewHeight <= 0f) return null;
        var args = new List<HudArgument>
        {
            HudArgument.FromFloat(drawn.X / across / viewWidth),
            HudArgument.FromFloat(drawn.Y / down / viewHeight),
            EngineArgs.At(line, 2) ?? HudArgument.FromString("Left"),
            EngineArgs.At(line, 3) ?? HudArgument.FromString("Top"),
            HudArgument.FromString("Viewport"),
        };
        return Same(args, line) ? null : args;
    }

    private static bool Same(List<HudArgument> args, HudBlock line) => args.SequenceEqual(line.Arguments);

    /// <summary>The FileInfo says AuthoredRatio(w, h), and nothing else about the layout.</summary>
    private static void SetRatio(HudDocument file, int w, int h, Action<IHudEdit> apply)
    {
        var info = file.TopLevel.FirstOrDefault(b => b.HasBody && b.Name == "FileInfo");
        if (info is null)
        {
            var name = Path.GetFileNameWithoutExtension(file.SourcePath) is { Length: > 0 } n ? n : "hud";
            info = new HudBlock("FileInfo", HudArgument.FromString(name)) { HasBody = true };
            apply(new InsertBlockEdit(file.TopLevel, 0, info, "add FileInfo", affected: info));
        }
        foreach (var line in info.Children.Where(l => !l.HasBody && l.Name is "AuthoredRatio" or "TrueWidescreen").ToList())
            apply(new DeleteBlockEdit(info.Children, info.Children.IndexOf(line), line, info));
        apply(new InsertBlockEdit(info.Children, info.Children.Count,
            new HudBlock("AuthoredRatio", HudArgument.FromInt(w), HudArgument.FromInt(h)), $"AuthoredRatio({w}, {h})", affected: info));
    }
}
