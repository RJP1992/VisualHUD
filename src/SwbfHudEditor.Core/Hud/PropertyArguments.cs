namespace SwbfHudEditor.Core.Hud;

/// <summary>
/// What each argument of a property line is, for the Properties panel's editors: its name, the values
/// the game's reader accepts when it reads a name (BF2GameExt <c>docs/RE/HUDElementProperties.md</c>),
/// whether a line is a 0/1 flag or a colour, and whether an argument is an event's name.
/// </summary>
public static class PropertyArguments
{
    private static readonly string[] Modes = { "Pixels", "Screen", "Viewport", "Frame" };
    private static readonly string[] RectModes = { "Pixels", "Screen", "Viewport" };
    private static readonly string[] HAligns = { "Left", "Center", "Right" };
    private static readonly string[] VAligns = { "Top", "Center", "Bottom" };

    private static readonly Dictionary<string, string[]> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Position"] = new[] { "x", "y", "z", "mode" },
        ["PositionSmall"] = new[] { "x", "y", "z", "mode" },
        ["PositionLarge"] = new[] { "x", "y", "z", "mode" },
        ["PositionSpawn"] = new[] { "x", "y", "z", "mode" },
        ["Rotation"] = new[] { "x", "y", "z" },
        ["Scale"] = new[] { "x", "y", "z" },
        ["Rect"] = new[] { "width", "height", "horizontal alignment", "vertical alignment", "mode" },
        ["BitmapRect"] = new[] { "width", "height", "horizontal alignment", "vertical alignment", "mode" },
        ["TextBox"] = new[] { "width", "height" },
        ["TextScale"] = new[] { "x", "y" },
        ["TextAlignment"] = new[] { "horizontal", "vertical" },
        ["Color"] = new[] { "red", "green", "blue" },
        ["ColorChange"] = new[] { "red", "green", "blue" },
        ["ColorFriendly"] = new[] { "red", "green", "blue" },
        ["TexCoords"] = new[] { "u0", "v0", "u1", "v1" },
        ["MaskTexCoords"] = new[] { "u0", "v0", "u1", "v1" },
        ["NumberColor"] = new[] { "key", "red", "green", "blue" },
        ["NumberVector3"] = new[] { "key", "x", "y", "z" },
        ["NameMesh"] = new[] { "weapon", "model" },
        ["Clamp"] = new[] { "min", "max" },
        ["InputRange"] = new[] { "min", "max" },
        ["AuthoredRatio"] = new[] { "width", "height" },
        ["Border"] = new[] { "width", "height" },
    };

    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "PropagateAlpha", "Scalable", "UseChangeColor", "EditOnly", "ScaleTexture", "ScaleSize", "Circular", "Lighting",
        "AlwaysScroll", "AddToTop", "DisableOnEmpty", "InfiniteDashes", "Percent", "FourSegmentLockOn", "AllowMiniMap",
        "PostNumbers", "WrapInput", "OutputIsAlpha", "TrueWidescreen",
    };

    /// <summary>The name of argument <paramref name="index"/> of a <paramref name="key"/> line, or null.</summary>
    public static string? NameOf(string key, int index) =>
        Names.TryGetValue(key, out var names) && index < names.Length ? names[index] : null;

    /// <summary>A line of one 0/1 argument: on or off.</summary>
    public static bool IsFlag(string key) => Flags.Contains(key);

    /// <summary>The index of a colour's red argument, or null when the line holds no colour.</summary>
    public static int? ColorStart(string key) => key.ToLowerInvariant() switch
    {
        "color" or "colorchange" or "colorfriendly" or "postflashcolor" or "targetfirecolor" => 0,
        "numbercolor" => 1,
        _ => null,
    };

    /// <summary>Whether argument <paramref name="index"/> of a <paramref name="key"/> line names an event.</summary>
    public static bool IsEvent(string key, int index) =>
        key.StartsWith("Event", StringComparison.OrdinalIgnoreCase) ||
        (index <= 1 && key.Equals("InputRange", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The names argument <paramref name="index"/> of a <paramref name="key"/> line takes, in a block named
    /// <paramref name="blockName"/>; null when it takes a number or free text.
    /// </summary>
    public static IReadOnlyList<string>? Choices(string key, int index, string? blockName = null)
    {
        switch (key.ToLowerInvariant())
        {
            case "position" or "positionsmall" or "positionlarge" or "positionspawn" when index == 3:
                return Modes;
            case "rect" or "bitmaprect" when index == 2:
                return HAligns;
            case "rect" or "bitmaprect" when index == 3:
                return VAligns;
            case "rect" or "bitmaprect" when index == 4:
                return RectModes;
            case "textalignment" when index == 0:
                return new[] { "Left", "Center", "Right" };
            case "textalignment" when index == 1:
                return new[] { "Top", "Center", "Bottom", "Baseline" };
            case "textbreak" when index == 0:
                return new[] { "None", "Word", "No Reformat" };
            case "textstyle" when index == 0:
                return new[] { "Normal", "Shadow", "Selected" };
            case "textclip" when index == 0:
                return new[] { "Character" };
            case "bitmapstyle" when index == 0:
                return new[] { "Normal", "Shadow" };
            case "blendmode" when index == 0:
                return new[] { "Alpha", "Additive" };
            case "fillfrom" when index == 0:
                return new[] { "Left", "Right", "Bottom", "Top" };
            case "screenanchor" when index == 0:
                return new[] { "Left", "Center", "Right" };
            case "backgroundmaskshapesmall" or "backgroundmaskshapelarge" or "backgroundmaskshapespawn" when index == 0:
                return new[] { "Circle", "Rectangle" };
            case "textfont" when index == 0:
                return new[] { "gamefont_super_tiny", "gamefont_tiny", "gamefont_small", "gamefont_medium", "gamefont_large" };
            case "operation" when index == 0:
                return blockName == "TransformNumberCompare"
                    ? new[] { "Greater", "GreaterOrEqual", "Less", "LessOrEqual", "Equal", "NotEqual" }
                    : new[] { "Add", "Subtract", "Multiply", "Divide", "Min", "Max" };
            default:
                return null;
        }
    }
}
