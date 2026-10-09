namespace SwbfHudEditor.Core.Hud;

/// <summary>
/// Catalog of properties the user can insert into an existing HudBlock via the Properties
/// panel's "Add" tab. Each entry encodes:
///   • Which element types it's valid for (empty = universal).
///   • A factory producing a fresh property block with sensible defaults.
///   • A short description for the tooltip.
/// Only properties that (a) apply to the current element type AND (b) aren't already present
/// on the block are shown to the user. That lets "Add Rotation" disappear once it's added.
/// </summary>
public static class PropertyCatalog
{
    /// <summary>
    /// One insertable property. <paramref name="ValidParents"/> is the set of element types this
    /// property may be added to; an empty set means "any element type". <paramref name="Factory"/>
    /// builds a fresh child <see cref="HudBlock"/> with default args.
    /// </summary>
    public sealed record PropertyDef(
        string Name,
        string Description,
        IReadOnlySet<string> ValidParents,
        Func<HudBlock> Factory);

    private static readonly IReadOnlySet<string> Any = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> BitmapOnly = new HashSet<string>(StringComparer.Ordinal) { "Bitmap", "BarBitmap", "ProceduralBarBitmap" };
    private static readonly IReadOnlySet<string> BarBitmapOnly = new HashSet<string>(StringComparer.Ordinal) { "BarBitmap" };
    private static readonly IReadOnlySet<string> Model3DOnly = new HashSet<string>(StringComparer.Ordinal) { "Model3D" };
    private static readonly IReadOnlySet<string> TextOnly = new HashSet<string>(StringComparer.Ordinal) { "Text" };
    // Position/Rotation/Scale/Alpha/Color/ZOrder all apply to things that render. Group is a
    // transform parent so it also benefits from these; BorderRect is included to keep parity
    // with SWBF2's actual semantics (it's positionable).
    private static readonly IReadOnlySet<string> RenderableOrGroup = new HashSet<string>(StringComparer.Ordinal)
    {
        "Group", "Bitmap", "BarBitmap", "ProceduralBarBitmap", "Model3D", "Text", "BorderRect",
    };

    /// <summary>
    /// Alphabetical-ish property list — ordered roughly by how often a modder reaches for each.
    /// Transform basics come first (Position, Rotation, Scale) because they're what the user's
    /// usually adding when something renders in the wrong spot / at the wrong angle.
    /// </summary>
    public static readonly IReadOnlyList<PropertyDef> All = new List<PropertyDef>
    {
        new("Position", "(X, Y, Z, mode): mode Pixels, Screen, Viewport or Frame. Left out, the game reads Pixels.",
            RenderableOrGroup,
            () => Leaf("Position", F(0.5), F(0.5), F(0.0), S("Viewport"))),

        new("Rotation", "(X, Y, Z) Euler angles in degrees. Y=180 flips a mesh horizontally.",
            RenderableOrGroup,
            () => Leaf("Rotation", F(0.0), F(0.0), F(0.0))),

        new("Scale", "(X, Y, Z) per-axis scale, applied after the rotation (along the parent's axes). 1 = no scaling.",
            RenderableOrGroup,
            () => Leaf("Scale", F(1.0), F(1.0), F(1.0))),

        new("Alpha", "0..1 opacity. Combine with PropagateAlpha(1) on a Group to fade a subtree.",
            RenderableOrGroup,
            () => Leaf("Alpha", F(1.0))),

        new("Color", "(R, G, B) tint — 0..255 per channel. White (255,255,255) = no tint.",
            RenderableOrGroup,
            () => Leaf("Color", I(255), I(255), I(255))),

        new("ZOrder", "Draw order, 0 to 255: a lower value draws on top. Without the line an element sits at 128.",
            RenderableOrGroup,
            () => Leaf("ZOrder", I(0))),

        new("PropagateAlpha", "1 = multiply children's alpha by this group's. Colour always tints children.",
            new HashSet<string>(StringComparer.Ordinal) { "Group" },
            () => Leaf("PropagateAlpha", I(1))),

        new("Viewport", "Screen group 0 to 4 (a player's split-screen view; 0 is shared). Only a top-level element's counts.",
            RenderableOrGroup,
            () => Leaf("Viewport", I(1))),

        new("ScaleSize", "BarBitmap only. 1 (default) lets the value move the bar's edge; 0 keeps the whole quad.",
            BarBitmapOnly,
            () => Leaf("ScaleSize", I(0))),

        new("BitmapRect", "(W, H, h-align, v-align, mode): the quad around the position. Its only size: the texture never sizes it.",
            BitmapOnly,
            () => Leaf("BitmapRect", F(0.1), F(0.1), S("Left"), S("Top"), S("Viewport"))),

        new("TexCoords", "(u1, v1, u2, v2) — sub-rectangle of the source texture to sample from.",
            BitmapOnly,
            () => Leaf("TexCoords", F(0.0), F(0.0), F(1.0), F(1.0))),

        new("BlendMode", "\"Additive\" or \"Alpha\" (default). Additive = glow.",
            BitmapOnly,
            () => Leaf("BlendMode", S("Additive"))),

        new("TextBox", "(W, H) as fractions of the view, whole pixels; text aligns inside it from the position.",
            TextOnly,
            () => Leaf("TextBox", F(0.2), F(0.05))),

        new("TextFont", "Font name — gamefont_tiny/small/medium/big/large.",
            TextOnly,
            () => Leaf("TextFont", S("gamefont_small"))),

        new("TextScale", "(X, Y) font size multiplier.",
            TextOnly,
            () => Leaf("TextScale", F(1.0), F(1.0))),

        new("TextAlignment", "(h, v): Left/Right/Center and Top/Bottom/Center/Baseline.",
            TextOnly,
            () => Leaf("TextAlignment", S("Center"), S("Center"))),

        new("TextStyle", "\"Shadow\" adds a drop shadow. Omit for plain.",
            TextOnly,
            () => Leaf("TextStyle", S("Shadow"))),

        new("Mesh", "Reference a mesh by name (no extension). Model3D/Bitmap.",
            new HashSet<string>(StringComparer.Ordinal) { "Model3D", "Bitmap" },
            () => Leaf("Mesh", S("hud_infinite_ammo_icon"))),

        new("EventEnable", "Event name that toggles this element's visibility at runtime.",
            Any,
            () => Leaf("EventEnable", S("initialize"))),

        new("EventValue", "BarBitmap/ProceduralBarBitmap: event providing the 0..1 fill fraction.",
            new HashSet<string>(StringComparer.Ordinal) { "BarBitmap", "ProceduralBarBitmap" },
            () => Leaf("EventValue", S("player1.healthFraction"))),

        new("EventMesh", "Model3D: event selecting which mesh to render.",
            Model3DOnly,
            () => Leaf("EventMesh", S("player1.weapon"))),

        new("EventColor", "Event providing an RGB color override.",
            Any,
            () => Leaf("EventColor", S("player1.teamColor"))),

        new("EventAlpha", "Event providing a 0..1 alpha value.",
            Any,
            () => Leaf("EventAlpha", S("player1.lowHealth"))),
    };

    /// <summary>
    /// Filter the catalog to properties that (a) can be legally added to a block of the given
    /// type and (b) aren't already present as a child. Result is ready-to-display.
    /// </summary>
    public static IEnumerable<PropertyDef> GetAddable(HudBlock target)
    {
        var presentNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in target.Children)
            if (!c.HasBody) presentNames.Add(c.Name);

        foreach (var def in All)
        {
            if (def.ValidParents.Count > 0 && !def.ValidParents.Contains(target.Name)) continue;
            if (presentNames.Contains(def.Name)) continue;
            yield return def;
        }
    }

    private static HudBlock Leaf(string name, params HudArgument[] args)
    {
        var b = new HudBlock { Name = name, HasBody = false };
        foreach (var a in args) b.Arguments.Add(a);
        return b;
    }

    private static HudArgument S(string s) => new(HudArgKind.String, s);
    private static HudArgument I(long n) => HudArgument.FromInt(n);
    private static HudArgument F(double n) => HudArgument.FromFloat(n);
}
