using System.Numerics;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Layout;

/// <summary>
/// Lays a <c>.hud</c> document out the way the game does: each element read and set up as its
/// <c>PostReadSetup</c> does, GameExt's placement for an opted-in file, then the draw's matrices down
/// to the screen. The rules and every address are in <c>docs/Engine.md</c>.
/// </summary>
public static partial class HudLayout
{
    private static readonly float[] AlignShare = { 0f, 0.5f, 1f };

    /// <summary>
    /// The elements at the top of a file, looking inside <c>ViewPort</c> blocks, whose scope the game
    /// reads as the file's own (once per camera).
    /// </summary>
    public static IEnumerable<HudBlock> TopLevelElements(HudDocument doc) => TopLevelElements(doc.TopLevel);

    private static IEnumerable<HudBlock> TopLevelElements(IEnumerable<HudBlock> blocks)
    {
        foreach (var block in blocks)
        {
            if (ElementKinds.TryGet(block, out _)) yield return block;
            else if (block.HasBody && block.Name.Equals("ViewPort", StringComparison.OrdinalIgnoreCase))
                foreach (var inner in TopLevelElements(block.Children)) yield return inner;
        }
    }

    public static HudLayoutResult Lay(HudDocument doc, LayoutOptions options) => Lay(new[] { doc }, options);

    /// <summary>
    /// Lay out the files a HUD loads, in load order. The game loads them additively into the same
    /// screen groups, and each file keeps its own <c>FileInfo</c>: under GameExt an
    /// <c>AuthoredRatio</c> file is laid out on its ratio while the others keep the stock layout.
    /// </summary>
    public static HudLayoutResult Lay(IReadOnlyList<HudDocument> docs, LayoutOptions options)
    {
        var byBlock = new Dictionary<HudBlock, LaidOutElement>(ReferenceEqualityComparer.Instance);
        var sharedPositions = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        var meshInfoUsers = new Dictionary<List<MeshInfoSpec>, List<LaidOutElement>>(ReferenceEqualityComparer.Instance);
        var files = new List<(HudDocument Document, HudFileLayout Layout)>();
        var roots = new List<LaidOutElement>();
        var round = options.RoundsToUnits;
        var groupWorld = EngineMath.LetterboxMatrix(options.Screen) * EngineMath.ScreenGroupParent(options.Screen);
        if (round) groupWorld = EngineMath.RoundTranslation(groupWorld);

        foreach (var doc in docs)
        {
            var file = HudFileLayout.For(doc, options);
            files.Add((doc, file));
            var ctx = new Context(options, file, doc, byBlock, sharedPositions, meshInfoUsers);

            var docRoots = new List<LaidOutElement>();
            foreach (var top in TopLevelElements(doc))
                if (ElementKinds.TryGet(top, out var kind))
                    docRoots.Add(Build(ctx, top, kind, null, 0));

            if (file.Opted) Place(ctx, docRoots);
            foreach (var root in docRoots) ApplyTrackedMoves(ctx, root);
            foreach (var root in docRoots)
            {
                var parent = !file.Opted ? groupWorld
                    : EngineMath.PixelParent(options.Screen, root.Role == PieceRole.Piece ? root.Slide : 0f);
                Draw(ctx, root, parent);
            }
            roots.AddRange(docRoots);
        }

        RedrawSharedMeshInfos(options, meshInfoUsers);

        var next = 0;
        foreach (var e in DrawOrder(TopLevelInsertionOrder(roots, options.ShowByEvents ? options.Simulation : null), topLevel: true))
            e.DrawIndex = next++;

        return new HudLayoutResult
        {
            Options = options,
            Files = files,
            Roots = roots,
            ByBlock = byBlock,
            GroupWorld = groupWorld,
        };
    }

    private sealed class Context(LayoutOptions options, HudFileLayout file, HudDocument document,
        Dictionary<HudBlock, LaidOutElement> byBlock, Dictionary<string, Vector3> sharedPositions,
        Dictionary<List<MeshInfoSpec>, List<LaidOutElement>> meshInfoUsers)
    {
        public LayoutOptions Options { get; } = options;

        /// <summary>Each MeshInfo table, and the models that read it as they loaded, in load order.</summary>
        public Dictionary<List<MeshInfoSpec>, List<LaidOutElement>> MeshInfoUsers { get; } = meshInfoUsers;

        /// <summary>The events decide what shows, as in the game.</summary>
        public bool ShowsByEvents => Options.Simulation is not null && Options.ShowByEvents;

        /// <summary>
        /// Before events are simulated: the point each position event is taken to have sent, the
        /// written position of the first group bound to it, as a share of its view.
        /// </summary>
        public Dictionary<string, Vector3> SharedPositions { get; } = sharedPositions;
        public HudFileLayout File { get; } = file;
        public HudDocument Document { get; } = document;
        public HudScreen Screen => Options.Screen;
        public Dictionary<HudBlock, LaidOutElement> ByBlock { get; } = byBlock;

        /// <summary><c>GetContainerViewWidth</c>: the screen with one viewport; GameExt's layout width for an opted-in file.</summary>
        public float ViewWidth => File.LayoutWidth;
        public float ViewHeight => Screen.HeightF;

        /// <summary>The width <c>"Screen"</c> multiplies by: GameExt hands the conversion the layout width when the view is the layout.</summary>
        public float ScreenModeWidth => File.Opted ? File.LayoutWidth : Screen.WidthF;

        /// <summary><c>0.75 / GetScreenAspectRatio()</c>, which GameExt makes 1 for an opted-in element.</summary>
        public float Stretch => File.Opted ? 1f : EngineMath.BitmapStretch(Screen);

        public bool GameExt => Options.Target == HudTarget.GameExt;

        public Matrix4x4 Round(Matrix4x4 m) => Options.RoundsToUnits ? EngineMath.RoundTranslation(m) : m;
    }

    // ---- Setup: what each element's PostReadSetup leaves ------------------------------------------

    private static LaidOutElement Build(Context ctx, HudBlock block, ElementKind kind, LaidOutElement? parent, int depth)
    {
        var spec = ElementReader.Read(block, kind);
        var group = NearestGroup(parent);
        var e = new LaidOutElement
        {
            Spec = spec,
            Parent = parent,
            Depth = depth,
            Document = ctx.Document,
            FileLayout = ctx.File,
            // HUD::Element::GetViewport asks the element's group, up to the top: a nested element's
            // own Viewport line changes nothing.
            Viewport = parent?.Viewport ?? spec.Viewport ?? 0,
            ViewWidth = ctx.ViewWidth,
            ViewHeight = ctx.ViewHeight,
            ScreenWidth = ctx.ScreenModeWidth,
            ScreenHeight = ctx.Screen.HeightF,
            ScreenPixelsWide = ctx.Screen.WidthF,
        };
        e.InActiveViewport = e.Viewport == 0 || e.Viewport == ctx.Options.ActiveViewport;
        var runtime = ctx.Options.Simulation?.For(block);
        e.Runtime = runtime;
        e.Enabled = !ctx.ShowsByEvents || runtime is { Enabled: true };
        e.Shown = e.Enabled && (parent?.Shown ?? true);
        e.Texture = runtime?.Texture ?? spec.Texture;
        e.Text = runtime?.Text;
        // GetContainerFrameWidth: the element's group's Rect in pixels, else the view.
        e.FrameWidth = group?.RectWidthPixels ?? e.ViewWidth;
        e.FrameHeight = group?.RectHeightPixels ?? e.ViewHeight;
        ctx.ByBlock[block] = e;

        if (ElementKinds.IsGroupBased(kind))
        {
            // ElementGroupBase::GetWidthPixelsFloat: the Rect in its own mode.
            e.RectWidthPixels = EngineMath.ToPixels(spec.RectMode, spec.RectWidth, e.FrameWidth, e.ViewWidth, e.ScreenWidth);
            e.RectHeightPixels = EngineMath.ToPixels(spec.RectMode, spec.RectHeight, e.FrameHeight, e.ViewHeight, e.ScreenHeight);
        }

        // HUD::Element::PostReadSetup: the position to pixels, the scale after the rotation.
        var translation = new Vector3(
            EngineMath.ToPixels(spec.PositionMode, spec.Position.X, e.FrameWidth, e.ViewWidth, e.ScreenWidth),
            EngineMath.ToPixels(spec.PositionMode, spec.Position.Y, e.FrameHeight, e.ViewHeight, e.ScreenHeight),
            spec.Position.Z);
        var local = EngineMath.LocalMatrix(EngineMath.Rotation(spec.Rotation.X, spec.Rotation.Y, spec.Rotation.Z),
            spec.Scale, translation);

        if (ElementKinds.IsGroupBased(kind))
        {
            // ElementGroupBase::PostReadSetup -> SetAlignment: the origin moves by -{0, 1/2, 1} of the Rect.
            local.M41 -= AlignShare[(int)spec.RectHAlign] * e.RectWidthPixels;
            local.M42 -= AlignShare[(int)spec.RectVAlign] * e.RectHeightPixels;
        }

        if (ElementKinds.IsBitmapBased(kind))
        {
            var quad = BitmapQuad(spec, e.FrameWidth, e.FrameHeight, e.ViewWidth, e.ViewHeight, e.ScreenWidth, e.ScreenHeight);
            e.FullQuad = quad;
            e.Uv = spec.TexCoords;
            e.Quad = quad;
            if (kind == ElementKind.BitmapMasked)
            {
                e.Mask = spec.Mask;
                e.MaskUv = spec.MaskTexCoords;
            }
            if (kind is ElementKind.BarBitmap or ElementKind.ProceduralBarBitmap) FillBar(ctx, e);
            // The stock widening stretch on the bitmap's own axes.
            local = EngineMath.ScaleOwnAxes(local, 1f, ctx.Stretch);
        }

        if (kind == ElementKind.Text && spec.HasTextBox)
        {
            // ElementText::ReadData: SetTextBox((int)(w * sViewportWidth), (int)(h * sViewportHeight)).
            e.TextBoxWidth = (int)(spec.TextBoxWidth * e.ViewWidth);
            e.TextBoxHeight = (int)(spec.TextBoxHeight * e.ViewHeight);
        }

        if (spec.Scalable && ctx.Options.HudScale != 1f)
        {
            // HUD::Element::SetRuntimeScale: the axes rescaled, the translation kept.
            var s = ctx.Options.HudScale;
            local.M11 *= s; local.M12 *= s; local.M13 *= s;
            local.M21 *= s; local.M22 *= s; local.M23 *= s;
            local.M31 *= s; local.M32 *= s; local.M33 *= s;
        }

        e.Local = local;
        e.LoadLocal = local;

        // A group whose EventPosition has not been sent, where the first group on that event was written.
        if (SharedMove(ctx, e, translation) is { } shared && !(ctx.File.Opted && IsTracked(shared.Event)))
            e.Local = ApplyMove(e, e.Local, shared, shared.Value);

        // Events that moved a group since: EventPosition, EventScale and EventRotation, in the order sent.
        if (runtime is not null)
            foreach (var move in runtime.Moves)
            {
                if (move.Kind == Events.GroupMoveKind.Position && ctx.File.Opted && IsTracked(move.Event))
                    continue;   // placed once the slides are known (ApplyTrackedMoves)
                e.Local = ApplyMove(e, e.Local, move, move.Value);
            }

        if (kind == ElementKind.Model3D) PlaceModel(ctx, e, spec, runtime);

        // EventColor replaces the colour (its alpha is dropped); a group's EventAlpha replaces its alpha.
        var c = runtime?.Color is { } rc ? new Vector3(rc.R, rc.G, rc.B) / 255f
            : new Vector3(spec.Color.R, spec.Color.G, spec.Color.B) / 255f;
        e.DrawColor = parent is null ? c : c * parent.DrawColor;
        e.DrawAlpha = (runtime?.Alpha ?? spec.Alpha) * (parent is { Spec.PropagateAlpha: true } ? parent.DrawAlpha : 1f);

        foreach (var child in spec.Children)
            if (ElementKinds.TryGet(child, out var childKind))
                e.Children.Add(Build(ctx, child, childKind, e, depth + 1));

        if (kind == ElementKind.BarSegmented) LayOutSegments(ctx, e);
        AddCodePieces(ctx, e);
        return e;
    }

    /// <summary>
    /// A model's mesh, matrix and depth. <c>ElementModel3D::SetMesh</c> (P <c>006063A0</c>) runs on the
    /// <c>Mesh</c> line, before the MeshInfo table is read, and on each mesh an event sends. Then a
    /// MeshInfo for that mesh replaces the element's matrix and depth; without one the element keeps its
    /// own matrix, but its depth goes back to 0.01. A <c>Depth</c> line lasts only until then.
    /// </summary>
    private static void PlaceModel(Context ctx, LaidOutElement e, ElementSpec spec, Events.ElementRuntime? runtime)
    {
        var table = MeshInfoTable(ctx, spec);
        if (!ctx.MeshInfoUsers.TryGetValue(table, out var users)) ctx.MeshInfoUsers[table] = users = new List<LaidOutElement>();
        users.Add(e);

        var sent = runtime?.Mesh;
        if (sent is null && spec.Mesh is null)
            sent = ctx.Options.ModelMesh?.Invoke(spec) ?? table.FirstOrDefault()?.Mesh;   // what its event would send
        e.Mesh = sent ?? spec.Mesh;
        e.MeshDepth = spec.Depth ?? ModelProjection.DefaultDepth;
        if (sent is null) return;

        var info = table.FirstOrDefault(m => string.Equals(m.Mesh, sent, StringComparison.OrdinalIgnoreCase));
        e.MeshDepth = info?.Depth ?? ModelProjection.DefaultDepth;
        if (info is null) return;
        e.MeshInfo = info;
        e.Local = MeshInfoMatrix(info, users);
    }

    /// <summary>
    /// The MeshInfo table a model reads: its own, else the one <c>InheritMeshInfo</c> (P <c>00606150</c>)
    /// shares from a Model3D made before it, found by name. Its own blocks win, as the read assigns them
    /// after the line.
    /// </summary>
    private static List<MeshInfoSpec> MeshInfoTable(Context ctx, ElementSpec spec, int hops = 0)
    {
        if (spec.MeshInfos.Count > 0 || spec.InheritMeshInfo is not { } name || hops > 16) return spec.MeshInfos;
        foreach (var other in ctx.ByBlock.Values)
            if (other.Kind == ElementKind.Model3D && other.Spec != spec &&
                string.Equals(other.Spec.Name, name, StringComparison.OrdinalIgnoreCase))
                return MeshInfoTable(ctx, other.Spec, hops + 1) is { Count: > 0 } shared ? shared : spec.MeshInfos;
        return spec.MeshInfos;
    }

    /// <summary>
    /// A MeshInfo's matrix. Each model that reads the table converts its positions in place, with its own
    /// sizes, as it loads (<c>ElementModel3D::PostReadSetup</c>, P <c>00605BA0</c>;
    /// <c>Matrix::ApplyRelativeMode</c>, P <c>005F2AC0</c>): a table shared by <c>InheritMeshInfo</c> is
    /// converted once per model, which only <c>"Pixels"</c> positions come through unchanged.
    /// </summary>
    private static Matrix4x4 MeshInfoMatrix(MeshInfoSpec info, IReadOnlyList<LaidOutElement> users)
    {
        var x = info.Position.X;
        var y = info.Position.Y;
        foreach (var u in users)
        {
            x = EngineMath.ToPixels(info.PositionMode, x, u.FrameWidth, u.ViewWidth, u.ScreenWidth);
            y = EngineMath.ToPixels(info.PositionMode, y, u.FrameHeight, u.ViewHeight, u.ScreenHeight);
        }
        return EngineMath.LocalMatrix(EngineMath.Rotation(info.Rotation.X, info.Rotation.Y, info.Rotation.Z), info.Scale,
            new Vector3(x, y, info.Position.Z));
    }

    /// <summary>Once every file has loaded: the models showing a MeshInfo of a table that later models shared.</summary>
    private static void RedrawSharedMeshInfos(LayoutOptions options, Dictionary<List<MeshInfoSpec>, List<LaidOutElement>> tables)
    {
        var toScreen = EngineMath.UnitsToScreen(options.Screen);
        foreach (var (table, users) in tables)
        {
            if (users.Count < 2) continue;
            foreach (var e in users)
            {
                if (e.MeshInfo is not { } info || !table.Contains(info)) continue;
                e.Local = MeshInfoMatrix(info, users);
                e.World = options.RoundsToUnits ? EngineMath.RoundTranslation(e.Local * e.ParentWorld) : e.Local * e.ParentWorld;
                e.ToScreen = e.World * toScreen;
            }
        }
    }

    /// <summary>
    /// A group's event moves (Phantom <c>ElementGroupBase::EventPosition</c> <c>005FB930</c>,
    /// <c>EventScale</c> <c>005FBDE0</c>, <c>EventRotation</c> <c>005FBAC0</c>). A position is read in the
    /// group's Rect mode, z as sent, and the Rect alignment applied again; a scale sets each of the
    /// group's own axes to the length sent; a rotation is rebuilt X, Y, Z with the axes' lengths kept.
    /// The translation survives a scale or rotation.
    /// </summary>
    private static Matrix4x4 ApplyMove(LaidOutElement e, Matrix4x4 m, Events.GroupMove move, Vector3 v)
    {
        var spec = e.Spec;
        switch (move.Kind)
        {
            case Events.GroupMoveKind.Position:
                m.M41 = EngineMath.ToPixels(spec.RectMode, v.X, e.FrameWidth, e.ViewWidth, e.ScreenWidth)
                        - AlignShare[(int)spec.RectHAlign] * e.RectWidthPixels;
                m.M42 = EngineMath.ToPixels(spec.RectMode, v.Y, e.FrameHeight, e.ViewHeight, e.ScreenHeight)
                        - AlignShare[(int)spec.RectVAlign] * e.RectHeightPixels;
                m.M43 = v.Z;
                return m;
            case Events.GroupMoveKind.Scale:
            {
                static Vector3 Unit(Vector3 a) => a.LengthSquared() > 0f ? Vector3.Normalize(a) : a;
                var x = Unit(new Vector3(m.M11, m.M12, m.M13)) * v.X;
                var y = Unit(new Vector3(m.M21, m.M22, m.M23)) * v.Y;
                var z = Unit(new Vector3(m.M31, m.M32, m.M33)) * v.Z;
                return new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, m.M41, m.M42, m.M43, 1f);
            }
            default:
            {
                var lengths = new Vector3(new Vector3(m.M11, m.M12, m.M13).Length(), new Vector3(m.M21, m.M22, m.M23).Length(),
                    new Vector3(m.M31, m.M32, m.M33).Length());
                var r = EngineMath.Rotation(v.X, v.Y, v.Z) * Matrix4x4.CreateScale(lengths);
                r.M41 = m.M41;
                r.M42 = m.M42;
                r.M43 = m.M43;
                return r;
            }
        }
    }

    /// <summary>
    /// The positions that follow the world. GameExt hands an opted-in element of these the fraction that
    /// lands on the real point once laid out and slid (<c>tracked_fraction</c>).
    /// </summary>
    internal static bool IsTracked(string eventName)
    {
        var n = eventName.ToLowerInvariant();
        if (n is "player1.weapon1.reticule.position" or "player1.weapon2.reticule.position" or "player1.weapon1.lockonposition"
            or "player1.weapon2.lockonposition" or "player1.weapon1.target.position" or "player1.weapon2.target.position")
            return true;
        const string cp = "player1.commandpost";
        if (!n.StartsWith(cp, StringComparison.Ordinal) || !n.EndsWith(".position", StringComparison.Ordinal)) return false;
        var number = n[cp.Length..^".position".Length];
        return int.TryParse(number, out var slot) && slot is >= 1 and <= 16;
    }

    private static void ApplyTrackedMoves(Context ctx, LaidOutElement e)
    {
        if (ctx.File.Opted)
        {
            var moves = SharedMove(ctx, e, null) is { } shared ? new[] { shared }
                : e.Runtime?.Moves ?? Array.Empty<Events.GroupMove>();
            foreach (var move in moves)
            {
                if (move.Kind != Events.GroupMoveKind.Position || !IsTracked(move.Event)) continue;
                var slide = 0f;
                for (var p = e; p is not null; p = p.Parent)
                    if (p.Role == PieceRole.Piece) { slide = p.Slide; break; }
                var x = TrueWidescreen.TrackedFraction(move.Value.X, ctx.Screen.WidthF, ctx.File.LayoutWidth, slide);
                e.Local = ApplyMove(e, e.Local, move, move.Value with { X = x });
            }
        }
        foreach (var child in e.Children) ApplyTrackedMoves(ctx, child);
    }

    /// <summary>
    /// Until its event is sent, a group bound to an <c>EventPosition</c> is shown as if that event had
    /// sent the written position of the first group bound to it: the game sends every group on the
    /// event the same point, so groups that follow a target together stay together. The first group's
    /// <paramref name="translation"/> (its position in pixels) sets the point; null only looks it up.
    /// Not while the events decide what shows: then a group the event has not reached is where it was
    /// written, as in the game.
    /// </summary>
    private static Events.GroupMove? SharedMove(Context ctx, LaidOutElement e, Vector3? translation)
    {
        if (ctx.ShowsByEvents || e.Spec.EventPosition is not { } name || !ElementKinds.IsGroupBased(e.Kind) ||
            e.Runtime?.Moves.Any(m => m.Kind == Events.GroupMoveKind.Position) == true)
            return null;
        if (!ctx.SharedPositions.TryGetValue(name, out var value))
        {
            if (translation is not { } t || e.ViewWidth <= 0f || e.ViewHeight <= 0f) return null;
            ctx.SharedPositions[name] = value = new Vector3(t.X / e.ViewWidth, t.Y / e.ViewHeight, t.Z);
        }
        return new Events.GroupMove(Events.GroupMoveKind.Position, value, name, 0);
    }

    private static LaidOutElement? NearestGroup(LaidOutElement? e)
    {
        for (; e is not null; e = e.Parent)
            if (ElementKinds.IsGroupBased(e.Kind)) return e;
        return null;
    }

    /// <summary>
    /// <c>BitmapRect</c>'s quad: <c>SetRect(w, h, h-align, v-align)</c> puts it around the position
    /// (Left 0..w, Center -w/2..w/2, Right -w..0), <c>ElementBitmapBase::PostReadSetup</c> converts its
    /// corners to pixels in the rect's mode, and <c>RedBitmapElement::SetRect</c> rounds each edge.
    /// Without a <c>BitmapRect</c> the quad is empty: a bitmap's size never comes from its texture.
    /// </summary>
    internal static QuadRect BitmapQuad(ElementSpec spec, float frameW, float frameH, float viewW, float viewH,
        float screenW, float screenH)
    {
        if (!spec.HasBitmapRect) return new QuadRect(0f, 0f, 0f, 0f);
        var w = spec.BitmapWidth;
        var h = spec.BitmapHeight;
        var l = -AlignShare[(int)spec.BitmapHAlign] * w;
        var t = -AlignShare[(int)spec.BitmapVAlign] * h;
        var mode = spec.BitmapMode;
        return new QuadRect(
            RoundEdge(EngineMath.ToPixels(mode, l, frameW, viewW, screenW)),
            RoundEdge(EngineMath.ToPixels(mode, t, frameH, viewH, screenH)),
            RoundEdge(EngineMath.ToPixels(mode, l + w, frameW, viewW, screenW)),
            RoundEdge(EngineMath.ToPixels(mode, t + h, frameH, viewH, screenH)));
    }

    /// <summary><c>RedBitmapElement::SetRect</c> (M <c>00839130</c>): <c>floor(v + 0.5)</c>.</summary>
    internal static float RoundEdge(float v) => (float)Math.Floor(v + 0.5f);

    /// <summary>
    /// A bar's quad. At load <c>ElementBar::PostReadSetup</c> calls <c>SetValue(Value)</c>, which
    /// changes nothing while the value equals its fraction (the stock 0..1 range), so the bar draws
    /// full until an event moves it. With another range the first fill runs against the
    /// constructor's width and U span of 1, a stock quirk kept here.
    /// </summary>
    private static void FillBar(Context ctx, LaidOutElement e)
    {
        var spec = e.Spec;
        var full = e.FullQuad!.Value;
        var uv = spec.TexCoords;
        var from = ctx.GameExt ? spec.FillFrom : FillFrom.Left;

        // The load: ElementBarBitmap::SetValue (P 005F7920) compares the value as read with the
        // fraction ElementBar::SetValue returns, and fills only if they differ.
        var fraction = spec.BarFraction(spec.Value);
        var quad = full;
        var drawnUv = uv;
        if (MathF.Abs(spec.Value - fraction) > 1e-4f)
        {
            if (spec.ScaleTexture) drawnUv = drawnUv with { U1 = fraction * 1f + uv.U0 };
            if (spec.ScaleSize) quad = quad with { Right = RoundEdge(fraction * 1f + full.Left) };
        }
        // What PostReadSetup then keeps as the bar's width and U span, and GameExt as its full bar.
        e.FullQuad = quad;
        e.Quad = quad;
        e.Uv = drawnUv;
        e.BarFraction = fraction;

        float? sentValue = e.Runtime?.BarValue;
        if (sentValue is null && ctx.Options.BarValues is { } values && values.TryGetValue(e.Block, out var given)) sentValue = given;
        if (sentValue is not { } sent) return;

        // An event: the stock fill (and FillFrom("Right"), which reuses it) runs only for a fraction
        // that differs from the value the bar holds; GameExt lays a vertical bar out every time.
        var sentFraction = spec.BarFraction(sent);
        e.BarFraction = sentFraction;
        var vertical = from is FillFrom.Bottom or FillFrom.Top;
        if (!vertical && !(MathF.Abs(spec.ClampValue(spec.Value) - sentFraction) > 1e-4f)) return;
        var (rect, filledUv) = BarFill.Draw(quad, drawnUv, sentFraction, from, spec.ScaleTexture, spec.ScaleSize);
        e.Quad = new QuadRect(RoundEdge(rect.Left), RoundEdge(rect.Top), RoundEdge(rect.Right), RoundEdge(rect.Bottom));
        e.Uv = filledUv;
    }

    /// <summary>
    /// <c>ElementBarSegmented::SetValue</c> (M <c>00696AB0</c>, P <c>005F8FF0</c>), run at load with the
    /// bar's value: each lit segment's matrix replaced outright, in a row (x from 0 in steps of the
    /// Rect's whole-pixel width / N) or round an ellipse in the Rect from <c>AngleStart</c>, rotated to
    /// face out, its vertical radius stretched by the same factor bitmaps get. The template
    /// <c>Segment</c> gives every segment its quad; the matrix it is given drops the template's own
    /// position and the bitmap stretch.
    /// </summary>
    private static void LayOutSegments(Context ctx, LaidOutElement bar)
    {
        var spec = bar.Spec;
        if (spec.SegmentTemplate is { } template)
        {
            var seg = ElementReader.Read(template, ElementKind.Bitmap);
            bar.SegmentSpec = seg;
            // The template's group is the bar: its Rect is the template's frame.
            bar.SegmentQuad = BitmapQuad(seg, bar.RectWidthPixels, bar.RectHeightPixels, bar.ViewWidth, bar.ViewHeight,
                bar.ScreenWidth, bar.ScreenHeight);
        }

        var n = spec.NumSegments;
        if (n <= 0) return;
        var value = bar.Runtime?.BarValue
                    ?? (ctx.Options.BarValues is { } values && values.TryGetValue(bar.Block, out var sent) ? sent : spec.Value);
        var f = spec.BarFraction(value);
        bar.BarFraction = f;
        var w = bar.RectWidthPixels;
        var h = bar.RectHeightPixels;
        var lit = 0;

        if (!spec.Circular)
        {
            var step = (int)w / (float)n;
            var end = w * f + 0f;
            for (var x = 0f; x < end && lit < n; x += step, lit++)
                bar.Segments.Add(new SegmentDraw(lit, true, Matrix4x4.CreateTranslation(x, 0f, 0f), default, default));
        }
        else
        {
            const float toRadians = MathF.PI / 180f;
            var rx = w * 0.5f;
            var ry = h * 0.5f;
            var start = spec.AngleStart * toRadians;
            var span = MathF.Abs(spec.AngleEnd * toRadians - start);
            var step = span / n;
            var end = span * f + start;
            for (var a = start; a < end && lit < n; a += step, lit++)
            {
                var (s, c) = MathF.SinCos(a);
                var m = Matrix4x4.CreateRotationZ(a);
                m.M41 = s * rx + rx;
                m.M42 = -(ctx.Stretch * c * ry) + ry;
                bar.Segments.Add(new SegmentDraw(lit, true, m, default, default));
            }
        }
        // The rest are disabled where they were last placed; at load that is nowhere.
        for (var i = lit; i < n; i++)
            bar.Segments.Add(new SegmentDraw(i, false, Matrix4x4.Identity, default, default));
    }

    // ---- GameExt's placement of an opted-in file ----------------------------------------------------

    /// <summary>
    /// <c>place()</c> in <c>hud_true_widescreen.cpp</c>: each top-level element by its translation in
    /// layout pixels, then each child of a plain container.
    /// </summary>
    private static void Place(Context ctx, List<LaidOutElement> roots)
    {
        var width = ctx.Screen.WidthF;
        var layout = ctx.File.LayoutWidth;
        foreach (var root in roots)
        {
            if (root.Kind == ElementKind.Target)
            {
                root.Role = PieceRole.Target;   // ElementTarget places its markers on the real screen
                continue;
            }
            Assign(root, true, width, layout);
        }
        foreach (var root in roots)
        {
            if (root.Role != PieceRole.Container) continue;
            foreach (var child in root.Children) Assign(child, false, width, layout);
        }
    }

    private static void Assign(LaidOutElement e, bool topLevel, float width, float layout)
    {
        var x = e.LoadLocal.M41;
        var y = e.LoadLocal.M42;
        var share = e.Spec.ScreenAnchorShare;
        var at = TrueWidescreen.PlacementFor(x, y, topLevel, share, width, layout);
        e.Role = at.Container ? PieceRole.Container : PieceRole.Piece;
        e.Slide = at.Slide;
        e.Third = at.Container || share is not null ? null : TrueWidescreen.AnchorFor(x, layout);
    }

    // ---- Draw order ------------------------------------------------------------------------------

    /// <summary>
    /// The order a group draws its children, back to front, as its child list holds them:
    /// <c>RedGroupElement::AddElement</c> (P <c>008C3650</c>) puts a new child, at the drawable's starting
    /// z-order of 128, after every child with an equal or higher one; then the child's <c>ZOrder</c> line
    /// (<c>RedInterfaceElement::SetZOrder</c>, P <c>00891EE0</c>) moves it in front of the first with an
    /// equal or lower one. The list runs from high z-order to low and is drawn from its head, so a lower
    /// <c>ZOrder</c> draws on top.
    /// </summary>
    internal static IEnumerable<LaidOutElement> DrawOrder(IReadOnlyList<LaidOutElement> siblings, bool topLevel = false)
    {
        const int startingZOrder = 128;
        var list = new List<LaidOutElement>();
        foreach (var e in siblings)
        {
            if (topLevel)
            {
                // A top-level element's drawable joins its screen group only when the element is
                // enabled (HUD::Element::UpdateEnableState, P 005F5C00), after its ZOrder line set the
                // z-order outside any list: it goes after every element of an equal or higher one.
                InsertAfterEqual(list, e, e.Spec.ZOrder);
                continue;
            }
            if (e.Piece is { } piece)
            {
                // Made in code with its z-order already set, then added.
                InsertAfterEqual(list, e, piece.ZOrder);
                continue;
            }
            InsertAfterEqual(list, e, startingZOrder);
            if (HasZOrderLine(e)) InsertBeforeEqual(list, e, e.Spec.ZOrder);
        }
        foreach (var e in list)
        {
            yield return e;
            foreach (var d in DrawOrder(e.Children)) yield return d;
        }
    }

    /// <summary>
    /// The order top-level elements joined their screen groups: the order their enabling events were
    /// sent when simulating (one enabled later goes behind those of an equal z-order already there),
    /// else load order, file by file.
    /// </summary>
    private static IReadOnlyList<LaidOutElement> TopLevelInsertionOrder(IReadOnlyList<LaidOutElement> roots,
        Events.HudSimulation? simulation)
    {
        if (simulation is null) return roots;
        return roots
            .Select((e, i) => (e, i))
            .OrderBy(x => x.e.Runtime is { Enabled: true } r ? r.EnableSeq : long.MaxValue)
            .ThenBy(x => x.i)
            .Select(x => x.e)
            .ToList();
    }

    private static void InsertAfterEqual(List<LaidOutElement> list, LaidOutElement e, int z)
    {
        var i = 0;
        while (i < list.Count && ZOf(list[i]) >= z) i++;
        list.Insert(i, e);
    }

    private static void InsertBeforeEqual(List<LaidOutElement> list, LaidOutElement e, int z)
    {
        list.Remove(e);
        var i = 0;
        while (i < list.Count && z < ZOf(list[i])) i++;
        list.Insert(i, e);
    }

    private static int ZOf(LaidOutElement e) => e.Spec.ZOrder;

    private static bool HasZOrderLine(LaidOutElement e) =>
        e.Block.Children.Any(c => !c.HasBody && c.Name == "ZOrder" && c.Arguments.Count > 0);

    // ---- The draw ----------------------------------------------------------------------------------

    /// <summary>
    /// The element draw (M <c>00816FA0</c>): its local matrix times the parent it is handed, rounded,
    /// handed on to its children. GameExt hands a plain container's child the container's matrix with
    /// the child's slide added.
    /// </summary>
    private static void Draw(Context ctx, LaidOutElement e, Matrix4x4 parentWorld)
    {
        var toScreen = EngineMath.UnitsToScreen(ctx.Screen);
        e.ParentWorld = parentWorld;
        e.World = ctx.Round(e.Local * parentWorld);
        e.ToScreen = e.World * toScreen;

        for (var i = 0; i < e.Segments.Count; i++)
        {
            var s = e.Segments[i];
            var world = ctx.Round(s.Local * e.World);
            e.Segments[i] = s with { World = world, ToScreen = world * toScreen };
        }

        var k = EngineMath.InterfaceWidth / ctx.Screen.WidthF;
        foreach (var child in e.Children)
        {
            var pw = e.World;
            if (e.Role == PieceRole.Container && child.Role == PieceRole.Piece) pw.M41 += child.Slide * k;
            Draw(ctx, child, pw);
        }
    }
}
