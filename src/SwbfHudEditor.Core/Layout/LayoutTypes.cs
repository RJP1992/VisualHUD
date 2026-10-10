using System.Numerics;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Layout;

/// <summary>The game the preview reproduces.</summary>
public enum HudTarget
{
    /// <summary>The stock game: GameExt's lines are errors it skips.</summary>
    Vanilla,
    /// <summary>The game with BF2GameExt: <c>AuthoredRatio</c>, <c>ScreenAnchor</c>, <c>FillFrom</c>, <c>HudSubPixel</c>.</summary>
    GameExt,
}

/// <summary>What the layout is worked out for.</summary>
public sealed record LayoutOptions
{
    public HudScreen Screen { get; init; } = new(1280, 720);
    public HudTarget Target { get; init; } = HudTarget.GameExt;

    /// <summary>
    /// The draw's rounding of every element to whole interface units (1/640 of the width). Null: by
    /// target, as each ships: stock rounds, GameExt's <c>HudSubPixel</c> (on by default) does not.
    /// </summary>
    public bool? Rounding { get; init; }

    /// <summary>The runtime HUD scale (<c>ScriptCB_SetHUDScale</c>) for <c>Scalable(1)</c> elements.</summary>
    public float HudScale { get; init; } = 1f;

    /// <summary>How many players' views the screen is split into. GameExt lays files out only with one.</summary>
    public int Viewports { get; init; } = 1;

    /// <summary>Whose screen group to show: elements in group 0 always show, others only in their own.</summary>
    public int ActiveViewport { get; init; } = 1;

    /// <summary>
    /// Values sent to bars through <c>EventValue</c>, by the bar's block. A bar not in it shows its load
    /// state: full, since the stock setup's first fill finds the value unchanged.
    /// </summary>
    public IReadOnlyDictionary<HudBlock, float>? BarValues { get; init; }

    /// <summary>
    /// The model a <c>Model3D</c> shows, by name; null falls back to its <c>Mesh</c> line, then its first
    /// <c>MeshInfo</c>. The editor resolves <c>EventMesh</c> through the transforms file here.
    /// </summary>
    public Func<ElementSpec, string?>? ModelMesh { get; init; }

    /// <summary>
    /// The events sent and what they did to each element. Null shows every element enabled, in its
    /// load state, as an editor wants; with one, the events set values, texts, colours, models and
    /// moves, and with <see cref="ShowByEvents"/> also what shows.
    /// </summary>
    public Events.HudSimulation? Simulation { get; init; }

    /// <summary>
    /// With a <see cref="Simulation"/>: an element shows only once its events have enabled it, as in the
    /// game. False: every element shows, as without one, and the events only set what they set.
    /// </summary>
    public bool ShowByEvents { get; init; } = true;

    public bool RoundsToUnits => Rounding ?? Target == HudTarget.Vanilla;
}

/// <summary>
/// What a file's <c>FileInfo</c> asks for and whether the target honours it: GameExt lays out a file
/// with <c>AuthoredRatio(w, h)</c> or <c>TrueWidescreen(1)</c> on its ratio's screen at the real
/// height, with one viewport.
/// </summary>
public sealed record HudFileLayout(float? Ratio, bool Opted, float LayoutWidth)
{
    /// <summary>
    /// Reads every <c>FileInfo</c> as GameExt's detour does (<c>hooked_ReadData</c> in
    /// <c>hud_true_widescreen.cpp</c>): each line replaces the last; a bad <c>AuthoredRatio</c> clears
    /// the ratio and a bad <c>TrueWidescreen</c> turns it off.
    /// </summary>
    public static HudFileLayout For(HudDocument doc, LayoutOptions options)
    {
        float ratio = 0f;
        var trueWidescreen = false;
        foreach (var top in doc.TopLevel)
        {
            if (top.Name != "FileInfo") continue;
            foreach (var line in top.Children)
            {
                if (line.HasBody) continue;
                if (line.Name == "AuthoredRatio") ratio = ReadRatio(line);
                else if (line.Name == "TrueWidescreen") trueWidescreen = ReadFlag(line);
            }
        }

        var asked = ratio > 0f ? ratio : trueWidescreen ? TrueWidescreen.FourThree : 0f;
        var screen = options.Screen;
        var opted = options.Target == HudTarget.GameExt && options.Viewports == 1 && asked > 0f &&
                    TrueWidescreen.LaidOutOn(screen.WidthF, screen.HeightF, asked);
        return new HudFileLayout(asked > 0f ? asked : null, opted,
            opted ? TrueWidescreen.LayoutWidth(screen.HeightF, asked) : screen.WidthF);
    }

    /// <summary><c>read_ratio</c>: two positive numbers whose ratio is from 1:1 to 4:1, else 0.</summary>
    private static float ReadRatio(HudBlock line)
    {
        if (line.Arguments.Count != 2) return 0f;
        if (!EngineArgs.IsNumber(line.Arguments[0]) || !EngineArgs.IsNumber(line.Arguments[1])) return 0f;
        var w = EngineArgs.Float(line.Arguments[0]);
        var h = EngineArgs.Float(line.Arguments[1]);
        return w > 0f && h > 0f && TrueWidescreen.ValidRatio(w / h) ? w / h : 0f;
    }

    /// <summary>A number other than 0 turns it on; <c>true</c> and <c>false</c> work; anything else turns it off.</summary>
    private static bool ReadFlag(HudBlock line)
    {
        if (line.Arguments.Count < 1) return false;
        var a = line.Arguments[0];
        if (EngineArgs.IsNumber(a)) return EngineArgs.Float(a) != 0f;
        return a.Kind == HudArgKind.Identifier && string.Equals(a.Raw, "true", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>How GameExt places an element of an opted-in file.</summary>
public enum PieceRole
{
    /// <summary>Not placed on its own: inside a piece, or the file is not laid out.</summary>
    None,
    /// <summary>Slid by its third or its <c>ScreenAnchor</c>.</summary>
    Piece,
    /// <summary>A top-level element at the layout's corner with no anchor: its children are the pieces.</summary>
    Container,
    /// <summary>A top-level <c>Target</c> element, never slid.</summary>
    Target,
}

/// <summary>One segment of a <c>BarSegmented</c>, placed as its <c>SetValue</c> places it.</summary>
public readonly record struct SegmentDraw(int Index, bool Lit, Matrix4x4 Local, Matrix4x4 World, Matrix4x4 ToScreen);

/// <summary>An element as the game lays it out and draws it.</summary>
public sealed class LaidOutElement
{
    public required ElementSpec Spec { get; init; }
    public HudBlock Block => Spec.Block;
    public ElementKind Kind => Spec.Kind;
    public LaidOutElement? Parent { get; init; }

    /// <summary>The file it came from, and how that file is laid out (its own <c>FileInfo</c>).</summary>
    public required HudDocument Document { get; init; }
    public required HudFileLayout FileLayout { get; init; }
    public List<LaidOutElement> Children { get; } = new();

    /// <summary>0 for an element at the top of the file.</summary>
    public int Depth { get; init; }

    /// <summary>The screen group it draws in: its <c>Viewport</c>, else its parent's, else 0.</summary>
    public int Viewport { get; set; }
    public bool InActiveViewport { get; set; }

    /// <summary>What the simulated events did to it, when the layout was given a simulation.</summary>
    public Events.ElementRuntime? Runtime { get; set; }

    /// <summary>Enabled by its events (always, without a simulation).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Drawn: it and every element above it enabled, as a disabled group draws none of its children.</summary>
    public bool Shown { get; set; } = true;

    /// <summary>The texture it draws: its <c>EventBitmap</c>'s, else its <c>Bitmap</c> line's.</summary>
    public string? Texture { get; set; }

    /// <summary>A text's content from its events; null shows its <c>Text</c> key.</summary>
    public string? Text { get; set; }

    // ---- The sizes its numbers were read against, in pixels -----------------------------------

    public float ViewWidth { get; set; }
    public float ViewHeight { get; set; }
    /// <summary>What <c>"Screen"</c> multiplies by: the screen, or the layout of an opted-in file.</summary>
    public float ScreenWidth { get; set; }
    public float ScreenHeight { get; set; }
    /// <summary>What <c>"Frame"</c> multiplies by: the parent group's <c>Rect</c>, else the view.</summary>
    public float FrameWidth { get; set; }
    public float FrameHeight { get; set; }

    // ---- Matrices --------------------------------------------------------------------------------

    /// <summary>Its local matrix as drawn, in its parent's HUD pixels (<c>m_mLocal</c>).</summary>
    public Matrix4x4 Local { get; set; }

    /// <summary>
    /// Its local matrix as the file finished loading, which GameExt places it by. It differs from
    /// <see cref="Local"/> only for a model whose <c>MeshInfo</c> is shown.
    /// </summary>
    public Matrix4x4 LoadLocal { get; set; }

    /// <summary>A model's mesh as shown, and the <c>MeshInfo</c> that placed it, if any.</summary>
    public string? Mesh { get; set; }
    public MeshInfoSpec? MeshInfo { get; set; }

    /// <summary>
    /// A model's depth as drawn (<c>m_fDepth</c>): how far <c>LocalToWorld</c> pulls it toward the camera,
    /// which sets its size. See <see cref="ModelProjection"/>.
    /// </summary>
    public float MeshDepth { get; set; } = ModelProjection.DefaultDepth;

    /// <summary>The matrix its draw was handed: its parent's world, or its screen group's.</summary>
    public Matrix4x4 ParentWorld { get; set; }

    /// <summary><see cref="Local"/> × <see cref="ParentWorld"/>, in interface units, rounded if the draw rounds.</summary>
    public Matrix4x4 World { get; set; }

    /// <summary>Its own pixels to screen pixels.</summary>
    public Matrix4x4 ToScreen { get; set; }

    // ---- Geometry --------------------------------------------------------------------------------

    /// <summary>A group's <c>Rect</c> in pixels; its children's <c>"Frame"</c>.</summary>
    public float RectWidthPixels { get; set; }
    public float RectHeightPixels { get; set; }

    /// <summary>A bitmap's quad as drawn, in its own pixels, after a bar's fill; null for other types.</summary>
    public QuadRect? Quad { get; set; }

    /// <summary>A bitmap's whole quad, as its setup left it, before any fill.</summary>
    public QuadRect? FullQuad { get; set; }

    public TexRect Uv { get; set; } = TexRect.Full;

    /// <summary>A bar's fraction as drawn (1 for other types).</summary>
    public float BarFraction { get; set; } = 1f;

    /// <summary>A text's box as <c>SetTextBox</c> gets it, whole pixels; 0 when it has none.</summary>
    public int TextBoxWidth { get; set; }
    public int TextBoxHeight { get; set; }

    /// <summary>A segmented bar's segments, with the template's quad and coordinates.</summary>
    public List<SegmentDraw> Segments { get; } = new();
    public ElementSpec? SegmentSpec { get; set; }
    public QuadRect? SegmentQuad { get; set; }

    // ---- Drawing ---------------------------------------------------------------------------------

    /// <summary>Its colour times every ancestor's (a group always tints its children), 0 to 1.</summary>
    public Vector3 DrawColor { get; set; } = Vector3.One;

    /// <summary>Its alpha times its parent's when the parent has <c>PropagateAlpha(1)</c>.</summary>
    public float DrawAlpha { get; set; } = 1f;

    // ---- GameExt placement -----------------------------------------------------------------------

    public PieceRole Role { get; set; }

    /// <summary>How far right GameExt draws it from where the layout puts it, in screen pixels.</summary>
    public float Slide { get; set; }

    /// <summary>For a piece without an anchor, the third it keeps to.</summary>
    public TrueWidescreen.Anchor? Third { get; set; }

    public IEnumerable<LaidOutElement> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants())
                yield return d;
    }

    /// <summary>
    /// What a drag of (dx, dy) screen pixels does to its <c>Position</c> numbers, in their own mode: the
    /// inverse of the parent's draw, then of the mode's conversion.
    /// </summary>
    public Vector2 PositionDeltaFor(float dxScreen, float dyScreen)
    {
        // Screen pixels to interface units, then into the parent's pixels.
        var k = EngineMath.InterfaceWidth / ScreenPixelsWide;
        var du = new Vector2(dxScreen * k, -dyScreen * k);
        var p = ParentWorld;
        var det = p.M11 * p.M22 - p.M12 * p.M21;
        if (MathF.Abs(det) < 1e-12f) return Vector2.Zero;
        var dx = (du.X * p.M22 - du.Y * p.M21) / det;
        var dy = (du.Y * p.M11 - du.X * p.M12) / det;
        var mode = MeshInfo?.PositionMode ?? Spec.PositionMode;
        return new Vector2(
            EngineMath.ToRelative(mode, dx, FrameWidth, ViewWidth, ScreenWidth),
            EngineMath.ToRelative(mode, dy, FrameHeight, ViewHeight, ScreenHeight));
    }

    /// <summary>The real screen's width, which interface units are a 640th of.</summary>
    public float ScreenPixelsWide { get; set; }

    /// <summary>The block whose <c>Position</c> line places it as drawn: its shown <c>MeshInfo</c>, else itself.</summary>
    public HudBlock PositionOwner => MeshInfo?.Block ?? Block;

    /// <summary>Its place in its screen group's draw, back to front: higher draws over lower.</summary>
    public int DrawIndex { get; set; }
}

/// <summary>The files of a HUD laid out together for one screen and target.</summary>
public sealed class HudLayoutResult
{
    public required LayoutOptions Options { get; init; }

    /// <summary>Each file in load order, with how its own <c>FileInfo</c> lays it out.</summary>
    public required IReadOnlyList<(HudDocument Document, HudFileLayout Layout)> Files { get; init; }

    /// <summary>The first file's layout, for a HUD of one file.</summary>
    public HudFileLayout File => Files[0].Layout;

    public HudFileLayout? LayoutOf(HudDocument document) =>
        Files.FirstOrDefault(f => ReferenceEquals(f.Document, document)).Layout;
    public required IReadOnlyList<LaidOutElement> Roots { get; init; }
    public required IReadOnlyDictionary<HudBlock, LaidOutElement> ByBlock { get; init; }

    /// <summary>The HUD screen group's own world matrix (letterbox and mapping), for stock elements.</summary>
    public Matrix4x4 GroupWorld { get; init; }

    public IEnumerable<LaidOutElement> All => Roots.SelectMany(r => r.SelfAndDescendants());

    public LaidOutElement? Find(HudBlock block) => ByBlock.TryGetValue(block, out var e) ? e : null;
}
