using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Layout;

/// <summary>
/// The lines a <c>.hud</c> file can do without: a property line that sets what the element's
/// constructors already set, so the game reads the element the same without it. Saving leaves
/// them out; the document keeps them.
/// </summary>
/// <remarks>
/// Kept whatever their value: <c>ZOrder</c> (a child's line moves it in front of its equals even at
/// the starting 128), every line of a key written more than once in a block (a later line replaces
/// an earlier one), the lines that make the element (<c>BitmapRect</c>, <c>Bitmap</c>, <c>Mesh</c>,
/// <c>Text</c>, <c>TextFont</c>), events, lines in a form other than the plain one, keys whose
/// default is not known, a <c>MeshInfo</c>'s <c>Depth</c> (it replaces the element's), and everything
/// outside the elements the game builds as written: <c>FileInfo</c>, transforms, <c>ViewPort</c>
/// settings, sounds, <c>Segment</c> templates, and the elements its code draws (Map, Target,
/// VehicleSeating, ObjectiveList, BorderedBox, MultilineText) with all they hold.
/// </remarks>
public static class ElementDefaults
{
    /// <summary>The document as text without the lines that restate a default. The document is not changed.</summary>
    public static string WriteWithoutDefaults(HudDocument doc, string indent = "    ")
    {
        var redundant = RedundantLines(doc);
        return HudWriter.Write(doc, indent, redundant.Contains);
    }

    /// <summary>Every line of the document that only restates a default.</summary>
    public static IReadOnlySet<HudBlock> RedundantLines(HudDocument doc)
    {
        var lines = new HashSet<HudBlock>(ReferenceEqualityComparer.Instance);
        foreach (var top in HudLayout.TopLevelElements(doc)) Visit(top, lines);
        return lines;
    }

    private static void Visit(HudBlock element, HashSet<HudBlock> into)
    {
        if (!ElementKinds.TryGet(element, out var kind) || DrawnByCode(kind)) return;
        AddRedundant(element, line => IsDefault(kind, line), into);
        foreach (var child in element.Children)
        {
            if (ElementKinds.TryGet(child, out _)) Visit(child, into);
            else if (kind == ElementKind.Model3D && child.HasBody && child.Name == "MeshInfo")
                AddRedundant(child, IsMeshInfoDefault, into);
        }
    }

    /// <summary>Elements whose drawing, and whose templates, the game's code does: left as written.</summary>
    private static bool DrawnByCode(ElementKind kind) => kind is ElementKind.Map or ElementKind.Target
        or ElementKind.VehicleSeating or ElementKind.ObjectiveList or ElementKind.BorderedBox or ElementKind.MultilineText;

    private static void AddRedundant(HudBlock scope, Func<HudBlock, bool> isDefault, HashSet<HudBlock> into)
    {
        // Keys hash without case in the game, so Alpha and alpha are the same key.
        var counts = scope.Children.Where(c => !c.HasBody)
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var line in scope.Children)
            if (!line.HasBody && counts[line.Name] == 1 && isDefault(line)) into.Add(line);
    }

    /// <summary>Whether a line of an element of this kind sets only what its constructors set.</summary>
    private static bool IsDefault(ElementKind kind, HudBlock line)
    {
        var a = line.Arguments;
        var bitmap = ElementKinds.IsBitmapBased(kind);
        var barBitmap = kind is ElementKind.BarBitmap or ElementKind.ProceduralBarBitmap;
        return line.Name switch
        {
            // HUD::Element's constructor (P 005F1E70) and its envelope (P 0060EDC0): every element.
            "Position" => a.Count is 3 or 4 && Leading(line, 0f, 0f, 0f),
            "Rotation" => Numbers(line, 0f, 0f, 0f),
            "Scale" => Numbers(line, 1f, 1f, 1f),
            "Alpha" => Numbers(line, 1f),
            "Color" or "ColorChange" => Numbers(line, 255f, 255f, 255f),
            "ColorChangeRate" => Numbers(line, 0.3f),
            "ColorPulseRate" or "FadeHoldTime" => Numbers(line, float.MaxValue),
            "FadeInTime" or "FadeSustainTime" or "FadeOutTime" => Numbers(line, 0f),
            "Viewport" => Numbers(line, 0f),
            // Flag bits 0, 3 and 5, cleared by the constructor.
            "UseChangeColor" or "EditOnly" or "Scalable" => Numbers(line, 0f),

            // ElementGroupBase: 1 × 1 of the view from its top left (P 005FB4D0).
            "Rect" when kind == ElementKind.Group =>
                a.Count is >= 2 and <= 5 && Leading(line, 1f, 1f)
                && (a.Count < 3 || IsName(a[2], EngineArgs.HAlignNames, (int)HAlign.Left))
                && (a.Count < 4 || IsName(a[3], EngineArgs.VAlignNames, (int)VAlign.Top))
                && (a.Count < 5 || IsName(a[4], EngineArgs.RelativeModeNames, (int)RelativeMode.Viewport, count: 3)),
            "PropagateAlpha" when kind == ElementKind.Group => Numbers(line, 0f),

            // RedBitmapElement's constructor (P 008C8A10).
            "TexCoords" when bitmap => Numbers(line, 0f, 0f, 1f, 1f),
            "BitmapStyle" when bitmap => IsWord(line, "Normal"),
            "BlendMode" when bitmap => IsWord(line, "Alpha"),

            // ElementBar's constructor: full, from 0 to 1.
            "Value" when ElementKinds.IsBar(kind) => Numbers(line, 1f),
            "MinValue" when ElementKinds.IsBar(kind) => Numbers(line, 0f),
            "MaxValue" when ElementKinds.IsBar(kind) => Numbers(line, 1f),
            "ScaleTexture" or "ScaleSize" when barBitmap => Numbers(line, 1f),
            "FlashyScale" when barBitmap => Numbers(line, 0.2f),
            "FlashyIncFadeOutTime" or "FlashyDecFadeOutTime" when barBitmap => Numbers(line, 0f),
            // GameExt's FillFrom: from the left, as without the line.
            "FillFrom" when barBitmap => IsWord(line, "Left"),
            "NumSegments" when kind == ElementKind.BarSegmented => Numbers(line, 0f),
            "Circular" when kind == ElementKind.BarSegmented => Numbers(line, 1f),
            "AngleStart" or "AngleEnd" when kind == ElementKind.BarSegmented => Numbers(line, 0f),

            // RedTextElement's constructor (P 008C4E90) and ElementText's formats.
            "TextScale" when kind == ElementKind.Text => Numbers(line, 1f, 1f),
            "TextAlignment" when kind == ElementKind.Text =>
                a.Count == 2 && IsName(a[0], EngineArgs.TextHAlignNames, (int)TextHAlign.Left)
                && IsName(a[1], EngineArgs.TextVAlignNames, (int)TextVAlign.Top),
            "TextBox" when kind == ElementKind.Text => Numbers(line, 0f, 0f),
            "TextCharacterSpacing" when kind == ElementKind.Text => Numbers(line, 1f),
            "TextTabSpacing" when kind == ElementKind.Text => Numbers(line, 0f),
            "IntegerFormat" when kind == ElementKind.Text => IsText(line, "%d"),
            "FloatFormat" when kind == ElementKind.Text => IsText(line, "%f"),

            // Red3DModelElementLite.
            "Depth" when kind == ElementKind.Model3D => Numbers(line, 0.01f),
            "Lighting" when kind == ElementKind.Model3D => Numbers(line, 1f),

            _ => false,
        };
    }

    /// <summary>A <c>MeshInfo</c>'s own matrix starts at the origin, unturned, at scale 1 (P <c>00605FC0</c>).</summary>
    private static bool IsMeshInfoDefault(HudBlock line) => line.Name switch
    {
        "Position" => line.Arguments.Count is 3 or 4 && Leading(line, 0f, 0f, 0f),
        "Rotation" => Numbers(line, 0f, 0f, 0f),
        "Scale" => Numbers(line, 1f, 1f, 1f),
        _ => false,
    };

    /// <summary>Exactly these numbers.</summary>
    private static bool Numbers(HudBlock line, params float[] values) =>
        line.Arguments.Count == values.Length && Leading(line, values);

    /// <summary>The line starts with these numbers.</summary>
    private static bool Leading(HudBlock line, params float[] values)
    {
        if (line.Arguments.Count < values.Length) return false;
        for (var i = 0; i < values.Length; i++)
        {
            var arg = line.Arguments[i];
            if (!EngineArgs.IsNumber(arg) || EngineArgs.Float(arg) != values[i]) return false;
        }
        return true;
    }

    /// <summary>One quoted argument naming this word, in any case.</summary>
    private static bool IsWord(HudBlock line, string word) =>
        line.Arguments.Count == 1 && EngineArgs.IsString(line.Arguments[0]) &&
        string.Equals(line.Arguments[0].Raw, word, StringComparison.OrdinalIgnoreCase);

    /// <summary>One quoted argument that is exactly this text.</summary>
    private static bool IsText(HudBlock line, string text) =>
        line.Arguments.Count == 1 && EngineArgs.IsString(line.Arguments[0]) && line.Arguments[0].Raw == text;

    /// <summary>A quoted name the game reads as the name at <paramref name="index"/>.</summary>
    private static bool IsName(HudArgument a, IReadOnlyList<string> names, int index, int? count = null) =>
        EngineArgs.IsString(a) && EngineArgs.TranslateString(names, count ?? names.Count, a.Raw) == index;
}
