using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Layout;

/// <summary>How a position or size is read: <c>gRelativeModeStrings</c>, in engine order.</summary>
public enum RelativeMode : byte
{
    /// <summary>Device pixels as written.</summary>
    Pixels = 0,
    /// <summary>A fraction of the full screen.</summary>
    Screen = 1,
    /// <summary>A fraction of the element's view: the screen with one viewport, or the layout of a GameExt <c>AuthoredRatio</c> file.</summary>
    Viewport = 2,
    /// <summary>A fraction of the parent group's <c>Rect</c>; only <c>Position</c> takes it.</summary>
    Frame = 3,
}

/// <summary><c>sHAlignStrings</c> / <c>sBitmapHAlignStrings</c>: the share of the width an origin moves by.</summary>
public enum HAlign : byte { Left = 0, Center = 1, Right = 2 }

/// <summary><c>sVAlignStrings</c> / <c>sBitmapVAlignStrings</c>.</summary>
public enum VAlign : byte { Top = 0, Center = 1, Bottom = 2 }

/// <summary><c>sTextAlignHorizontal</c>: note Right before Center.</summary>
public enum TextHAlign : byte { Left = 0, Right = 1, Center = 2 }

/// <summary><c>sTextAlignVertical</c>.</summary>
public enum TextVAlign : byte { Top = 0, Bottom = 1, Center = 2, Baseline = 3 }

/// <summary>
/// Reads <see cref="HudArgument"/>s the way the engine's readers read <c>PblConfig::Data</c> arguments,
/// so a line means in the preview what it means in the game, quirks included.
/// </summary>
/// <remarks>
/// ConfigMunge stores a quoted string as an offset into the data, a number as a float and, as far as
/// is known, an unquoted word as its hash. The readers tell a string from a number by the argument's
/// raw value (a small offset is a string), so an unquoted word reads as a large number: true as a
/// bool, never a match for a name.
/// </remarks>
public static class EngineArgs
{
    public static readonly string[] RelativeModeNames = { "Pixels", "Screen", "Viewport", "Frame" };
    public static readonly string[] HAlignNames = { "Left", "Center", "Right" };
    public static readonly string[] VAlignNames = { "Top", "Center", "Bottom" };
    public static readonly string[] TextHAlignNames = { "Left", "Right", "Center" };
    public static readonly string[] TextVAlignNames = { "Top", "Bottom", "Center", "Baseline" };
    public static readonly string[] TextBreakNames = { "None", "Word", "No Reformat", "Word Reformat" };

    /// <summary>
    /// <c>HUD::TranslateString</c> (Phantom <c>00618150</c>): the index of the first of the first
    /// <paramref name="count"/> names the text starts with, ignoring case, or -1. It compares each
    /// name's length only, so "ViewportX" reads as "Viewport" and "Cent" as nothing.
    /// </summary>
    public static int TranslateString(IReadOnlyList<string> names, int count, string? text)
    {
        if (text is null) return -1;
        for (var i = 0; i < count && i < names.Count; i++)
        {
            var name = names[i];
            if (text.Length >= name.Length &&
                string.Compare(name, 0, text, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return i;
        }
        return -1;
    }

    public static bool IsString(HudArgument a) => a.Kind == HudArgKind.String;

    public static bool IsNumber(HudArgument a) => a.Kind is HudArgKind.Integer or HudArgKind.Float;

    /// <summary><c>GetStringArg</c>: the text of a quoted argument, or null for anything else.</summary>
    public static string? String(HudArgument a) => IsString(a) ? a.Raw : null;

    /// <summary>
    /// <c>GetFloatArg</c>: a number as a float. A string or word reads its raw storage as a float in
    /// the game, a meaningless value; here 0.
    /// </summary>
    public static float Float(HudArgument a)
    {
        if (!IsNumber(a)) return 0f;
        return float.TryParse(a.Raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    /// <summary><c>GetBoolArg</c>: a number other than 0. A word (its hash) is true.</summary>
    public static bool Bool(HudArgument a) => a.Kind switch
    {
        HudArgKind.Identifier => true,
        HudArgKind.String => true,
        _ => Float(a) != 0f,
    };

    /// <summary>The argument at <paramref name="index"/>, or null past the end.</summary>
    public static HudArgument? At(HudBlock line, int index) =>
        index < line.Arguments.Count ? line.Arguments[index] : null;

    public static float FloatAt(HudBlock line, int index, float fallback = 0f) =>
        At(line, index) is { } a ? Float(a) : fallback;

    /// <summary>
    /// A name argument through <see cref="TranslateString"/>, or <paramref name="fallback"/> when it
    /// is missing, not a string or not a name in the table: what the readers do with
    /// <c>uVar &lt; count ? uVar : 0</c>.
    /// </summary>
    public static int NameAt(HudBlock line, int index, IReadOnlyList<string> names, int fallback = 0)
    {
        if (At(line, index) is not { } a || !IsString(a)) return fallback;
        var i = TranslateString(names, names.Count, a.Raw);
        return i < 0 ? fallback : i;
    }

    /// <summary>
    /// The mode argument of <c>Position</c> (Phantom <c>HUD::Matrix::ReadPosition</c>): a name of the
    /// four, else a bool where true is Viewport and false Pixels; an unknown name is Pixels.
    /// </summary>
    public static RelativeMode PositionMode(HudArgument? a)
    {
        if (a is null) return RelativeMode.Pixels;
        if (!IsString(a)) return Bool(a) ? RelativeMode.Viewport : RelativeMode.Pixels;
        var i = TranslateString(RelativeModeNames, 4, a.Raw);
        return i < 0 ? RelativeMode.Pixels : (RelativeMode)i;
    }

    /// <summary>
    /// The mode argument of a group's <c>Rect</c> (<c>ElementGroupBase::ReadData</c>): one of the
    /// first three names, else a bool as for <c>Position</c>; an unknown name keeps
    /// <paramref name="current"/> (Viewport by default).
    /// </summary>
    public static RelativeMode RectMode(HudArgument? a, RelativeMode current)
    {
        if (a is null) return current;
        if (!IsString(a)) return Bool(a) ? RelativeMode.Viewport : RelativeMode.Pixels;
        var i = TranslateString(RelativeModeNames, 3, a.Raw);
        return i < 0 ? current : (RelativeMode)i;
    }

    /// <summary>
    /// The mode argument of <c>BitmapRect</c> (<c>ElementBitmapBase::ReadData</c>, Phantom
    /// <c>005FA670</c>): read only when the line has exactly five arguments. One of the first three
    /// names; else a number, where 0 means Screen and anything else Viewport; else Viewport.
    /// </summary>
    public static RelativeMode BitmapRectMode(HudBlock line)
    {
        if (line.Arguments.Count != 5) return RelativeMode.Viewport;
        var a = line.Arguments[4];
        if (!IsString(a)) return a.Kind != HudArgKind.Identifier && Float(a) == 0f ? RelativeMode.Screen : RelativeMode.Viewport;
        var i = TranslateString(RelativeModeNames, 3, a.Raw);
        return i < 0 ? RelativeMode.Viewport : (RelativeMode)i;
    }
}
