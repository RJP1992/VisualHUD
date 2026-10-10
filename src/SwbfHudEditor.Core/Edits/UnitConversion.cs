using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Edits;

/// <summary>
/// The sizes a line's numbers are read against, in pixels: the frame (the group's <c>Rect</c> it is in),
/// the view and the screen, as the game converts them (<c>ConvertRelativeToPixels</c>, M <c>00691120</c>).
/// </summary>
public readonly record struct UnitBasis(float FrameWidth, float FrameHeight, float ViewWidth, float ViewHeight,
    float ScreenWidth, float ScreenHeight)
{
    public float ToPixels(RelativeMode mode, float value, bool vertical) => vertical
        ? EngineMath.ToPixels(mode, value, FrameHeight, ViewHeight, ScreenHeight)
        : EngineMath.ToPixels(mode, value, FrameWidth, ViewWidth, ScreenWidth);

    public float ToRelative(RelativeMode mode, float pixels, bool vertical) => vertical
        ? EngineMath.ToRelative(mode, pixels, FrameHeight, ViewHeight, ScreenHeight)
        : EngineMath.ToRelative(mode, pixels, FrameWidth, ViewWidth, ScreenWidth);
}

/// <summary>
/// A line's numbers rewritten in another unit, so the element keeps its place and size at the screen
/// they are worked out for: <c>Position</c> (and the map's <c>PositionSmall</c>, <c>PositionLarge</c>,
/// <c>PositionSpawn</c>) in Pixels, Screen, Viewport or Frame; <c>Rect</c> and <c>BitmapRect</c> in the
/// first three, which is all the game reads for them.
/// </summary>
public static class UnitConversion
{
    /// <summary>Where a line keeps its two numbers and its mode, and what the mode is when the line has none.</summary>
    public sealed record ModeLine(int X, int Y, int Mode, RelativeMode Default, bool TakesFrame, int MinArguments);

    public static ModeLine? Of(string lineName) => lineName switch
    {
        "Position" or "PositionSmall" or "PositionLarge" or "PositionSpawn" => new ModeLine(0, 1, 3, RelativeMode.Pixels, true, 3),
        "Rect" or "BitmapRect" => new ModeLine(0, 1, 4, RelativeMode.Viewport, false, 2),
        _ => null,
    };

    /// <summary>The mode the game reads the line's numbers in.</summary>
    public static RelativeMode ModeOf(HudBlock line)
    {
        var n = line.Arguments.Count;
        return line.Name switch
        {
            "Rect" => n > 4 ? EngineArgs.RectMode(line.Arguments[4], RelativeMode.Viewport) : RelativeMode.Viewport,
            "BitmapRect" => EngineArgs.BitmapRectMode(line),
            _ => n == 4 ? EngineArgs.PositionMode(line.Arguments[3]) : RelativeMode.Pixels,
        };
    }

    /// <summary>Whether the game reads a mode this line could be given: a BitmapRect with more than five arguments reads none.</summary>
    public static bool CanConvert(HudBlock line, RelativeMode to)
    {
        if (Of(line.Name) is not { } m || line.HasBody) return false;
        if (to == RelativeMode.Frame && !m.TakesFrame) return false;
        var n = line.Arguments.Count;
        if (n < m.MinArguments) return false;
        if (line.Name == "BitmapRect" && n > 5) return false;
        if (m.Mode == 3 && n > 4) return false;   // a Position with more arguments than the game reads
        return EngineArgs.IsNumber(line.Arguments[m.X]) && EngineArgs.IsNumber(line.Arguments[m.Y]);
    }

    /// <summary>
    /// The line's arguments with its numbers in <paramref name="to"/>, the same in pixels at <paramref name="basis"/>,
    /// the mode written out (with the alignments a Rect or BitmapRect needs before it); null when it cannot take it.
    /// </summary>
    public static IReadOnlyList<HudArgument>? Convert(HudBlock line, RelativeMode to, UnitBasis basis)
    {
        if (!CanConvert(line, to) || Of(line.Name) is not { } m) return null;
        var from = ModeOf(line);
        var args = line.Arguments.ToList();
        if (from != to)
        {
            args[m.X] = Number(basis.ToRelative(to, basis.ToPixels(from, EngineArgs.Float(args[m.X]), false), false));
            args[m.Y] = Number(basis.ToRelative(to, basis.ToPixels(from, EngineArgs.Float(args[m.Y]), true), true));
        }
        // What comes between the numbers and the mode: a Position's z, a Rect's alignments.
        while (args.Count < m.Mode)
            args.Add(m.Mode == 3 ? HudArgument.FromFloat(0) : HudArgument.FromString(args.Count == 2 ? "Left" : "Top"));
        var name = HudArgument.FromString(EngineArgs.RelativeModeNames[(int)to]);
        if (args.Count == m.Mode) args.Add(name);
        else args[m.Mode] = name;
        return args;
    }

    /// <summary>A number typed in pixels on one of the line's two numbers, in the line's own mode.</summary>
    public static float? FromPixels(HudBlock line, int index, float pixels, UnitBasis basis)
    {
        if (line.Name == "TextBox" && index is 0 or 1)
            return basis.ToRelative(RelativeMode.Viewport, pixels, index == 1);   // always shares of the view
        if (Of(line.Name) is not { } m || (index != m.X && index != m.Y)) return null;
        return basis.ToRelative(ModeOf(line), pixels, index == m.Y);
    }

    private static HudArgument Number(float value) => HudArgument.FromFloat(Math.Round(value, 6));
}
