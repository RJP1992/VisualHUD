using System.Numerics;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Layout;

/// <summary>The element types a <c>.hud</c> file can make, by their keyword.</summary>
public enum ElementKind
{
    Group,
    Text,
    Bitmap,
    BitmapMasked,
    BarBitmap,
    ProceduralBarBitmap,
    BarSegmented,
    Model3D,
    MultilineText,
    BorderedBox,
    ObjectiveList,
    Map,
    Target,
    VehicleSeating,
}

public static class ElementKinds
{
    private static readonly Dictionary<string, ElementKind> ByKeyword = new(StringComparer.Ordinal)
    {
        ["Group"] = ElementKind.Group,
        ["Text"] = ElementKind.Text,
        ["Bitmap"] = ElementKind.Bitmap,
        ["BitmapMasked"] = ElementKind.BitmapMasked,
        ["BarBitmap"] = ElementKind.BarBitmap,
        ["ProceduralBarBitmap"] = ElementKind.ProceduralBarBitmap,
        ["BarSegmented"] = ElementKind.BarSegmented,
        ["Model3D"] = ElementKind.Model3D,
        ["MultilineText"] = ElementKind.MultilineText,
        ["BorderedBox"] = ElementKind.BorderedBox,
        ["ObjectiveList"] = ElementKind.ObjectiveList,
        ["Map"] = ElementKind.Map,
        ["Target"] = ElementKind.Target,
        ["VehicleSeating"] = ElementKind.VehicleSeating,
    };

    /// <summary>
    /// Whether a block makes an element: an element keyword with a body. Without one, <c>Text</c> and
    /// <c>Bitmap</c> are properties (the localize key, the texture); other bodied blocks
    /// (<c>MeshInfo</c>, <c>Segment</c>, the map's and target's templates) are sub-scopes of their element.
    /// </summary>
    public static bool TryGet(HudBlock block, out ElementKind kind)
    {
        kind = default;
        return block.HasBody && ByKeyword.TryGetValue(block.Name, out kind);
    }

    /// <summary>Types built on <c>ElementGroupBase</c>: they read <c>Rect</c> and hold children.</summary>
    public static bool IsGroupBased(ElementKind kind) => kind is ElementKind.Group or ElementKind.BarSegmented
        or ElementKind.MultilineText or ElementKind.BorderedBox or ElementKind.ObjectiveList or ElementKind.Map
        or ElementKind.Target or ElementKind.VehicleSeating;

    /// <summary>Types built on <c>ElementBitmapBase</c>: they read <c>BitmapRect</c> and draw a quad.</summary>
    public static bool IsBitmapBased(ElementKind kind) => kind is ElementKind.Bitmap or ElementKind.BitmapMasked
        or ElementKind.BarBitmap or ElementKind.ProceduralBarBitmap;

    public static bool IsBar(ElementKind kind) => kind is ElementKind.BarBitmap or ElementKind.ProceduralBarBitmap
        or ElementKind.BarSegmented;
}

/// <summary>
/// One element's properties, read from its block the way the engine's <c>ReadData</c> chain reads them
/// (<c>docs/RE/HUDElementProperties.md</c> in BF2GameExt): defaults from the constructors, each line
/// applied in order so a later line replaces an earlier one, and sizes kept in their written units
/// until the layout knows the screen.
/// </summary>
public sealed class ElementSpec
{
    public required HudBlock Block { get; init; }
    public required ElementKind Kind { get; init; }

    /// <summary>The element's name, its block's first string argument.</summary>
    public string? Name { get; init; }

    // ---- Element (every visible type) ----------------------------------------------------------

    /// <summary><c>Position(x, y, z [, mode])</c>; the matrix starts at the origin.</summary>
    public Vector3 Position { get; set; }
    public RelativeMode PositionMode { get; set; } = RelativeMode.Pixels;
    public bool HasPosition { get; set; }

    /// <summary><c>Rotation(x, y, z)</c> in degrees.</summary>
    public Vector3 Rotation { get; set; }

    /// <summary><c>Scale(x, y, z)</c>, applied in <c>PostReadSetup</c>.</summary>
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary><c>ZOrder(n)</c>, 0 to 255; a lower one draws on top. A drawable starts at 128 (P <c>00891350</c>).</summary>
    public int ZOrder { get; set; } = 128;
    public float Alpha { get; set; } = 1f;
    public (byte R, byte G, byte B) Color { get; set; } = (255, 255, 255);

    /// <summary><c>Viewport(n)</c>, the screen group, clamped 0 to 4; null to inherit the parent's.</summary>
    public int? Viewport { get; set; }

    /// <summary><c>Scalable(1)</c>: follows the runtime HUD scale (<c>ScriptCB_SetHUDScale</c>).</summary>
    public bool Scalable { get; set; }

    /// <summary><c>EditOnly(1)</c>: shown only in the HUD editor.</summary>
    public bool EditOnly { get; set; }

    public bool Additive { get; set; }

    // ---- Group base ------------------------------------------------------------------------------

    /// <summary><c>Rect(w, h [, h-align [, v-align [, mode]]])</c>: 1 × 1, Viewport, Left/Top by default.</summary>
    public float RectWidth { get; set; } = 1f;
    public float RectHeight { get; set; } = 1f;
    public HAlign RectHAlign { get; set; }
    public VAlign RectVAlign { get; set; }
    public RelativeMode RectMode { get; set; } = RelativeMode.Viewport;

    /// <summary><c>PropagateAlpha(1)</c>: children's alpha is multiplied by this group's at draw.</summary>
    public bool PropagateAlpha { get; set; }

    // ---- Bitmap base -----------------------------------------------------------------------------

    public string? Texture { get; set; }
    public bool HasBitmapRect { get; set; }
    public float BitmapWidth { get; set; }
    public float BitmapHeight { get; set; }
    public HAlign BitmapHAlign { get; set; }
    public VAlign BitmapVAlign { get; set; }
    public RelativeMode BitmapMode { get; set; } = RelativeMode.Viewport;
    public TexRect TexCoords { get; set; } = TexRect.Full;

    // ---- Bars ------------------------------------------------------------------------------------

    /// <summary><c>Value</c>, <c>MinValue</c>, <c>MaxValue</c>: 1, 0 and 1 from <c>ElementBar</c>'s constructor.</summary>
    public float Value { get; set; } = 1f;
    public float MinValue { get; set; }
    public float MaxValue { get; set; } = 1f;

    /// <summary><c>ScaleTexture</c> and <c>ScaleSize</c>, both on by default.</summary>
    public bool ScaleTexture { get; set; } = true;
    public bool ScaleSize { get; set; } = true;

    /// <summary>GameExt's <c>FillFrom</c>; a game without GameExt fills from the left.</summary>
    public FillFrom FillFrom { get; set; } = FillFrom.Left;

    public int NumSegments { get; set; }
    public bool Circular { get; set; } = true;
    public float AngleStart { get; set; }
    public float AngleEnd { get; set; }

    /// <summary>The <c>Segment("Segment") { ... }</c> template a <c>BarSegmented</c> copies per segment.</summary>
    public HudBlock? SegmentTemplate { get; set; }

    // ---- Text ------------------------------------------------------------------------------------

    public string? TextKey { get; set; }
    public string? EventText { get; set; }
    public string? EventNumber { get; set; }
    public string? Font { get; set; }
    public bool HasTextBox { get; set; }
    public float TextBoxWidth { get; set; }
    public float TextBoxHeight { get; set; }
    public Vector2 TextScale { get; set; } = Vector2.One;
    public TextHAlign TextHAlign { get; set; }
    public TextVAlign TextVAlign { get; set; }
    public string? TextStyle { get; set; }

    /// <summary>How <c>EventNumber</c> prints (<c>ElementText::EventNumber</c>, P <c>0060BE50</c>).</summary>
    public string? IntegerFormat { get; set; }
    public string? FloatFormat { get; set; }
    public bool Percent { get; set; }
    public string? NumberToTime { get; set; }
    public bool InfiniteDashes { get; set; }

    // ---- Event bindings (each a single handler: a later line replaces an earlier one) -----------

    public string? EventEnable { get; set; }
    public string? EventDisable { get; set; }
    public string? EventValue { get; set; }
    public string? EventColor { get; set; }
    public string? EventBitmap { get; set; }
    public string? EventPosition { get; set; }
    public string? EventScale { get; set; }
    public string? EventRotation { get; set; }
    public string? EventAlpha { get; set; }

    /// <summary>Every event this element binds, with the property that binds it.</summary>
    public IEnumerable<(string Property, string Event)> Bindings
    {
        get
        {
            foreach (var (prop, evt) in new[]
            {
                ("EventEnable", EventEnable), ("EventDisable", EventDisable), ("EventValue", EventValue),
                ("EventText", EventText), ("EventNumber", EventNumber), ("EventColor", EventColor),
                ("EventBitmap", EventBitmap), ("EventMesh", EventMesh), ("EventPosition", EventPosition),
                ("EventScale", EventScale), ("EventRotation", EventRotation), ("EventAlpha", EventAlpha),
            })
                if (evt is not null) yield return (prop, evt);
        }
    }

    // ---- Model3D --------------------------------------------------------------------------------

    /// <summary><c>Mesh("name")</c>: the model set as the file loads.</summary>
    public string? Mesh { get; set; }

    /// <summary><c>EventMesh("event")</c>: an event that sets the model later.</summary>
    public string? EventMesh { get; set; }

    /// <summary>
    /// <c>MeshInfo("mesh") { ... }</c>: when that mesh is the element's model,
    /// <c>ElementModel3D::SetMesh</c> (P <c>006063A0</c>) replaces the element's whole matrix with this one.
    /// </summary>
    public List<MeshInfoSpec> MeshInfos { get; } = new();

    // ---- GameExt ---------------------------------------------------------------------------------

    /// <summary><c>ScreenAnchor</c>'s share of the width beyond the layout, if the element has a valid one.</summary>
    public float? ScreenAnchorShare { get; set; }

    /// <summary>Whether that share came from a name, which the editor writes back as the name.</summary>
    public bool ScreenAnchorNamed { get; set; }

    // ---- Structure -------------------------------------------------------------------------------

    /// <summary>The elements inside this one, in file order.</summary>
    public List<HudBlock> Children { get; } = new();

    /// <summary>Bodied blocks that are not elements: <c>MeshInfo</c>, <c>Segment</c>, templates.</summary>
    public List<HudBlock> SubScopes { get; } = new();

    /// <summary>The bar fraction <c>ElementBar::SetValue</c> returns for a value: clamped, then normalised.</summary>
    public float BarFraction(float value)
    {
        var (min, max) = BarRange();
        var v = ClampValue(value);
        return max - min == 0f ? 0f : (v - min) / (max - min);
    }

    /// <summary>The value <c>ElementBar::SetValue</c> keeps: clamped to the range.</summary>
    public float ClampValue(float value)
    {
        var (min, max) = BarRange();
        return value < min ? min : value > max ? max : value;
    }

    /// <summary><c>ElementBar::PostReadSetup</c> raises a maximum below the minimum to it.</summary>
    private (float Min, float Max) BarRange()
    {
        var max = MaxValue;
        var min = MinValue;
        if (max <= min && min != max) max = min;
        return (min, max);
    }
}

/// <summary>
/// A <c>MeshInfo</c> sub-scope (<c>ElementModel3D::MeshInfo::Read</c>, P <c>00605FC0</c>): a matrix of its
/// own, from the origin with no rotation and a scale of 1, which <c>ElementModel3D::PostReadSetup</c>
/// converts with the element's sizes.
/// </summary>
public sealed class MeshInfoSpec
{
    public required string Mesh { get; init; }
    public required HudBlock Block { get; init; }
    public Vector3 Position { get; set; }
    public RelativeMode PositionMode { get; set; } = RelativeMode.Pixels;
    public Vector3 Rotation { get; set; }
    public Vector3 Scale { get; set; } = Vector3.One;
    public float? Depth { get; set; }

    public static MeshInfoSpec Read(HudBlock block, string mesh)
    {
        var info = new MeshInfoSpec { Mesh = mesh, Block = block };
        foreach (var line in block.Children)
        {
            if (line.HasBody) continue;
            var n = line.Arguments.Count;
            switch (line.Name)
            {
                case "Position" when n >= 3:
                    info.Position = new Vector3(EngineArgs.FloatAt(line, 0), EngineArgs.FloatAt(line, 1), EngineArgs.FloatAt(line, 2));
                    info.PositionMode = n == 4 ? EngineArgs.PositionMode(line.Arguments[3]) : RelativeMode.Pixels;
                    break;
                case "Rotation" when n >= 3:
                    info.Rotation = new Vector3(EngineArgs.FloatAt(line, 0), EngineArgs.FloatAt(line, 1), EngineArgs.FloatAt(line, 2));
                    break;
                case "Scale" when n >= 3:
                    info.Scale = new Vector3(EngineArgs.FloatAt(line, 0), EngineArgs.FloatAt(line, 1), EngineArgs.FloatAt(line, 2));
                    break;
                case "Depth" when n >= 1:
                    info.Depth = EngineArgs.FloatAt(line, 0);
                    break;
            }
        }
        return info;
    }
}

/// <summary>Reads an <see cref="ElementSpec"/> from an element's block.</summary>
public static class ElementReader
{
    public static ElementSpec Read(HudBlock block, ElementKind kind)
    {
        var spec = new ElementSpec
        {
            Block = block,
            Kind = kind,
            Name = block.Arguments.Count > 0 && EngineArgs.IsString(block.Arguments[0]) ? block.Arguments[0].Raw : null,
        };

        foreach (var line in block.Children)
        {
            if (ElementKinds.TryGet(line, out _))
            {
                spec.Children.Add(line);
                continue;
            }
            if (line.HasBody)
            {
                spec.SubScopes.Add(line);
                if (line.Name == "Segment" && kind == ElementKind.BarSegmented) spec.SegmentTemplate = line;
                if (line.Name == "MeshInfo" && kind == ElementKind.Model3D && line.Arguments.Count > 0 &&
                    EngineArgs.String(line.Arguments[0]) is { Length: > 0 } mesh)
                    spec.MeshInfos.Add(MeshInfoSpec.Read(line, mesh));
                continue;
            }
            Apply(spec, line);
        }
        return spec;
    }

    private static void Apply(ElementSpec s, HudBlock line)
    {
        var n = line.Arguments.Count;
        switch (line.Name)
        {
            // ---- Element ----
            case "Position" when n >= 3:
                s.Position = new Vector3(EngineArgs.FloatAt(line, 0), EngineArgs.FloatAt(line, 1), EngineArgs.FloatAt(line, 2));
                s.PositionMode = n == 4 ? EngineArgs.PositionMode(line.Arguments[3]) : RelativeMode.Pixels;
                s.HasPosition = true;
                break;
            case "Rotation" when n >= 3:
                s.Rotation = new Vector3(EngineArgs.FloatAt(line, 0), EngineArgs.FloatAt(line, 1), EngineArgs.FloatAt(line, 2));
                break;
            case "Scale" when n >= 3:
                s.Scale = new Vector3(EngineArgs.FloatAt(line, 0), EngineArgs.FloatAt(line, 1), EngineArgs.FloatAt(line, 2));
                break;
            case "ZOrder" when n >= 1:
                s.ZOrder = Math.Clamp((int)EngineArgs.FloatAt(line, 0), 0, 255);
                break;
            case "Alpha" when n >= 1:
                s.Alpha = EngineArgs.FloatAt(line, 0);
                break;
            case "Color" when n >= 3:
                s.Color = (Byte(line, 0), Byte(line, 1), Byte(line, 2));
                break;
            case "Viewport" when n >= 1:
                s.Viewport = Math.Clamp((int)EngineArgs.FloatAt(line, 0), 0, 4);
                break;
            case "Scalable" when n >= 1:
                s.Scalable = EngineArgs.Bool(line.Arguments[0]);
                break;
            case "EditOnly" when n >= 1:
                s.EditOnly = EngineArgs.Bool(line.Arguments[0]);
                break;
            case "BlendMode" when n >= 1:
                // An unknown value is ignored (HUDBlendMode.md).
                var blend = EngineArgs.String(line.Arguments[0]);
                if (string.Equals(blend, "Additive", StringComparison.OrdinalIgnoreCase)) s.Additive = true;
                else if (string.Equals(blend, "Alpha", StringComparison.OrdinalIgnoreCase)) s.Additive = false;
                break;

            // ---- Group base ----
            case "Rect" when n >= 2 && ElementKinds.IsGroupBased(s.Kind):
                s.RectWidth = EngineArgs.FloatAt(line, 0);
                s.RectHeight = EngineArgs.FloatAt(line, 1);
                s.RectHAlign = n > 2 ? (HAlign)EngineArgs.NameAt(line, 2, EngineArgs.HAlignNames) : HAlign.Left;
                s.RectVAlign = n > 3 ? (VAlign)EngineArgs.NameAt(line, 3, EngineArgs.VAlignNames) : VAlign.Top;
                if (n > 4) s.RectMode = EngineArgs.RectMode(line.Arguments[4], s.RectMode);
                break;
            case "PropagateAlpha" when n >= 1:
                s.PropagateAlpha = EngineArgs.Bool(line.Arguments[0]);
                break;

            // ---- Bitmap base ----
            case "Bitmap" when n >= 1 && ElementKinds.IsBitmapBased(s.Kind):
                s.Texture = EngineArgs.String(line.Arguments[0]);
                break;
            case "BitmapRect" when n >= 2 && ElementKinds.IsBitmapBased(s.Kind):
                s.HasBitmapRect = true;
                s.BitmapWidth = EngineArgs.FloatAt(line, 0);
                s.BitmapHeight = EngineArgs.FloatAt(line, 1);
                s.BitmapHAlign = n > 2 ? (HAlign)EngineArgs.NameAt(line, 2, EngineArgs.HAlignNames) : HAlign.Left;
                s.BitmapVAlign = n > 3 ? (VAlign)EngineArgs.NameAt(line, 3, EngineArgs.VAlignNames) : VAlign.Top;
                s.BitmapMode = EngineArgs.BitmapRectMode(line);
                break;
            case "TexCoords" when n >= 4 && ElementKinds.IsBitmapBased(s.Kind):
                s.TexCoords = new TexRect(EngineArgs.FloatAt(line, 0), EngineArgs.FloatAt(line, 1),
                    EngineArgs.FloatAt(line, 2), EngineArgs.FloatAt(line, 3));
                break;

            // ---- Bars ----
            case "Value" when n >= 1 && ElementKinds.IsBar(s.Kind):
                s.Value = EngineArgs.FloatAt(line, 0);
                break;
            case "MinValue" when n >= 1 && ElementKinds.IsBar(s.Kind):
                s.MinValue = EngineArgs.FloatAt(line, 0);
                break;
            case "MaxValue" when n >= 1 && ElementKinds.IsBar(s.Kind):
                s.MaxValue = EngineArgs.FloatAt(line, 0);
                break;
            case "ScaleTexture" when n >= 1:
                s.ScaleTexture = EngineArgs.Bool(line.Arguments[0]);
                break;
            case "ScaleSize" when n >= 1:
                s.ScaleSize = EngineArgs.Bool(line.Arguments[0]);
                break;
            case "FillFrom" when s.Kind is ElementKind.BarBitmap or ElementKind.ProceduralBarBitmap:
                // GameExt's reader: a quoted name, any case; anything else fills from the left.
                var from = n >= 1 ? EngineArgs.String(line.Arguments[0]) : null;
                s.FillFrom = from is null ? FillFrom.Left
                    : string.Equals(from, "Right", StringComparison.OrdinalIgnoreCase) ? FillFrom.Right
                    : string.Equals(from, "Bottom", StringComparison.OrdinalIgnoreCase) ? FillFrom.Bottom
                    : string.Equals(from, "Top", StringComparison.OrdinalIgnoreCase) ? FillFrom.Top
                    : FillFrom.Left;
                break;
            case "NumSegments" when n >= 1:
                s.NumSegments = Math.Max(0, (int)EngineArgs.FloatAt(line, 0));
                break;
            case "Circular" when n >= 1:
                s.Circular = EngineArgs.Bool(line.Arguments[0]);
                break;
            case "AngleStart" when n >= 1:
                s.AngleStart = EngineArgs.FloatAt(line, 0);
                break;
            case "AngleEnd" when n >= 1:
                s.AngleEnd = EngineArgs.FloatAt(line, 0);
                break;

            // ---- Text ----
            case "Text" when n >= 1 && s.Kind == ElementKind.Text:
                s.TextKey = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventText" when n >= 1:
                s.EventText = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventNumber" when n >= 1:
                s.EventNumber = EngineArgs.String(line.Arguments[0]);
                break;
            case "TextFont" when n >= 1:
                s.Font = EngineArgs.String(line.Arguments[0]);
                break;
            case "TextBox" when n >= 2:
                s.HasTextBox = true;
                s.TextBoxWidth = EngineArgs.FloatAt(line, 0);
                s.TextBoxHeight = EngineArgs.FloatAt(line, 1);
                break;
            case "TextScale" when n >= 2:
                s.TextScale = new Vector2(EngineArgs.FloatAt(line, 0), EngineArgs.FloatAt(line, 1));
                break;
            case "TextAlignment" when n >= 2:
                // Each half keeps its current value when it names nothing.
                var h = EngineArgs.NameAt(line, 0, EngineArgs.TextHAlignNames, (int)s.TextHAlign);
                var v = EngineArgs.NameAt(line, 1, EngineArgs.TextVAlignNames, (int)s.TextVAlign);
                s.TextHAlign = (TextHAlign)h;
                s.TextVAlign = (TextVAlign)v;
                break;
            case "TextStyle" when n >= 1:
                s.TextStyle = EngineArgs.String(line.Arguments[0]);
                break;
            case "IntegerFormat" when n >= 1:
                s.IntegerFormat = EngineArgs.String(line.Arguments[0]);
                break;
            case "FloatFormat" when n >= 1:
                s.FloatFormat = EngineArgs.String(line.Arguments[0]);
                break;
            case "Percent" when n >= 1:
                s.Percent = EngineArgs.Bool(line.Arguments[0]);
                break;
            case "NumberToTime" when n >= 1:
                s.NumberToTime = EngineArgs.String(line.Arguments[0]);
                break;
            case "InfiniteDashes" when n >= 1:
                s.InfiniteDashes = EngineArgs.Bool(line.Arguments[0]);
                break;

            // ---- Event bindings ----
            case "EventEnable" when n >= 1:
                s.EventEnable = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventDisable" when n >= 1:
                s.EventDisable = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventValue" when n >= 1 && ElementKinds.IsBar(s.Kind):
                s.EventValue = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventColor" when n >= 1:
                s.EventColor = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventBitmap" when n >= 1 && ElementKinds.IsBitmapBased(s.Kind):
                s.EventBitmap = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventPosition" when n >= 1 && ElementKinds.IsGroupBased(s.Kind):
                s.EventPosition = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventScale" when n >= 1 && ElementKinds.IsGroupBased(s.Kind):
                s.EventScale = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventRotation" when n >= 1 && ElementKinds.IsGroupBased(s.Kind):
                s.EventRotation = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventAlpha" when n >= 1 && ElementKinds.IsGroupBased(s.Kind):
                s.EventAlpha = EngineArgs.String(line.Arguments[0]);
                break;

            // ---- Model3D ----
            case "Mesh" when n >= 1 && s.Kind == ElementKind.Model3D:
                s.Mesh = EngineArgs.String(line.Arguments[0]);
                break;
            case "EventMesh" when n >= 1 && s.Kind == ElementKind.Model3D:
                s.EventMesh = EngineArgs.String(line.Arguments[0]);
                break;

            // ---- GameExt ----
            case "ScreenAnchor":
                ReadScreenAnchor(s, line);
                break;
        }
    }

    /// <summary>
    /// GameExt's <c>read_anchor</c>: exactly one argument, a name of the three in any case (quoted or
    /// not) or a share from 0 to 1. Anything else is logged and ignored, keeping an earlier line's.
    /// </summary>
    private static void ReadScreenAnchor(ElementSpec s, HudBlock line)
    {
        if (line.Arguments.Count != 1) return;
        var a = line.Arguments[0];
        if (a.Kind is HudArgKind.String or HudArgKind.Identifier)
        {
            if (!TrueWidescreen.AnchorNamed(a.Raw, out var anchor)) return;
            s.ScreenAnchorShare = TrueWidescreen.ShareOf(anchor);
            s.ScreenAnchorNamed = true;
            return;
        }
        var share = EngineArgs.Float(a);
        if (!TrueWidescreen.ValidShare(share)) return;
        s.ScreenAnchorShare = share;
        s.ScreenAnchorNamed = false;
    }

    private static byte Byte(HudBlock line, int index) => (byte)Math.Clamp((int)EngineArgs.FloatAt(line, index), 0, 255);
}
