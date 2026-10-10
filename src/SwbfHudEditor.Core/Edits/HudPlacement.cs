using System.Numerics;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Edits;

/// <summary>
/// What a position line is: an element's own; a seating chart's own, read as pixels; a map's for one of its
/// modes; a MeshInfo's, whose table another model may inherit and convert again.
/// </summary>
public enum LineRole { Own, Seating, Mode, MeshInfo }

/// <summary>
/// Where an element's position lines put it on the screen, and the numbers that put it at a given point: the
/// shared ground of the rewrites that keep elements where they are drawn while what they are in changes.
/// </summary>
public static class HudPlacement
{
    /// <summary>
    /// The lines that place an element in its parent: its last <c>Position</c> (not a group's that follows a
    /// position GameExt tracks), a map's positions for each mode, and each <c>MeshInfo</c>'s, which replaces a
    /// model's matrix.
    /// </summary>
    public static IEnumerable<(HudBlock Line, LineRole Role)> PositionLines(LaidOutElement e)
    {
        var block = e.Block;
        if (Last(block, "Position") is { } own && !Tracked(e))
            yield return (own, e.Kind == ElementKind.VehicleSeating ? LineRole.Seating : LineRole.Own);
        if (e.Kind == ElementKind.Map)
            foreach (var name in new[] { "PositionSmall", "PositionLarge", "PositionSpawn" })
                if (Last(block, name) is { } line) yield return (line, LineRole.Mode);
        if (e.Kind == ElementKind.Model3D)
            foreach (var info in block.Children.Where(c => c.HasBody && c.Name == "MeshInfo"))
                if (Last(info, "Position") is { } line) yield return (line, LineRole.MeshInfo);
    }

    /// <summary>
    /// A group that follows a position GameExt tracks (a reticule, a command post): in a file it lays out, the
    /// written Position, the place until the first event, is a share of the real screen whatever the shape.
    /// </summary>
    public static bool Tracked(LaidOutElement e) =>
        e.Block.Children.Any(c => !c.HasBody && c.Name == "EventPosition" && EngineArgs.String(EngineArgs.At(c, 0) ?? HudArgument.FromInt(0)) is { } name &&
                                  HudLayout.IsTracked(name));

    /// <summary>An element without a Position line is at its parent's origin, unless code places it (a target, a map).</summary>
    public static bool PlacedByOrigin(LaidOutElement e) =>
        Last(e.Block, "Position") is null && e.Kind is not (ElementKind.Target or ElementKind.Map) && !Tracked(e);

    /// <summary>The last line of that name whose first two numbers are numbers.</summary>
    public static HudBlock? Last(HudBlock block, string name) =>
        block.Children.LastOrDefault(c => !c.HasBody && c.Name == name && c.Arguments.Count >= 2 &&
                                          EngineArgs.IsNumber(c.Arguments[0]) && EngineArgs.IsNumber(c.Arguments[1]));

    /// <summary>Where a position line puts its element, in its parent's pixels.</summary>
    public static Vector3 PixelsOf(HudBlock line, LineRole role, LaidOutElement e)
    {
        var x = EngineArgs.FloatAt(line, 0);
        var y = EngineArgs.FloatAt(line, 1);
        var z = EngineArgs.FloatAt(line, 2);
        // VehicleSeating::PostReadSetup reads its own Position as pixels whatever its mode.
        if (role == LineRole.Seating) return new Vector3(x, y, z);
        var mode = EngineArgs.PositionMode(EngineArgs.At(line, 3));
        return new Vector3(
            EngineMath.ToPixels(mode, x, e.FrameWidth, e.ViewWidth, e.ScreenWidth),
            EngineMath.ToPixels(mode, y, e.FrameHeight, e.ViewHeight, e.ScreenHeight), z);
    }

    /// <summary>The matrix an element's parent hands it, to the screen: its own matrix taken off its draw.</summary>
    public static Matrix4x4? ParentToScreen(LaidOutElement e)
    {
        if (!Matrix4x4.Invert(e.Local, out var inverse)) return null;
        var parent = inverse * e.ToScreen;
        return MathF.Abs(Determinant2(parent)) > 1e-9f ? parent : null;
    }

    public static float Determinant2(Matrix4x4 m) => m.M11 * m.M22 - m.M12 * m.M21;

    public static Vector2 OnScreen(Vector3 pixels, Matrix4x4 parent)
    {
        var p = Vector3.Transform(pixels, parent);
        return new Vector2(p.X, p.Y);
    }

    /// <summary>How long a matrix's two axes are drawn, in screen pixels per own pixel.</summary>
    public static (float Across, float Down) Axes(Matrix4x4 m) =>
        (MathF.Sqrt(m.M11 * m.M11 + m.M12 * m.M12), MathF.Sqrt(m.M21 * m.M21 + m.M22 * m.M22));

    /// <summary>
    /// The arguments that put a position line's element at <paramref name="point"/> on the screen through its
    /// parent's matrix as it now draws, z kept, or null when nothing moves. In Viewport when
    /// <paramref name="viewport"/> asks (Frame, a seating chart's pixels and a MeshInfo's own unit kept), else in
    /// the line's own unit; a unit left out stays left out where it is the one kept.
    /// </summary>
    public static List<HudArgument>? Place(HudBlock line, LineRole role, LaidOutElement e, Vector2 point, Matrix4x4 parent, bool viewport)
    {
        var z = EngineArgs.FloatAt(line, 2);
        var bx = point.X - z * parent.M31 - parent.M41;
        var by = point.Y - z * parent.M32 - parent.M42;
        var det = Determinant2(parent);
        if (MathF.Abs(det) < 1e-12f) return null;
        var x = (bx * parent.M22 - by * parent.M21) / det;
        var y = (by * parent.M11 - bx * parent.M12) / det;

        var current = EngineArgs.PositionMode(EngineArgs.At(line, 3));
        var mode = role == LineRole.Seating ? RelativeMode.Pixels
            : !viewport || role == LineRole.MeshInfo || current == RelativeMode.Frame ? current
            : RelativeMode.Viewport;
        var args = new List<HudArgument>
        {
            Number(EngineMath.ToRelative(mode, x, e.FrameWidth, e.ViewWidth, e.ScreenWidth), EngineArgs.At(line, 0)),
            Number(EngineMath.ToRelative(mode, y, e.FrameHeight, e.ViewHeight, e.ScreenHeight), EngineArgs.At(line, 1)),
            EngineArgs.At(line, 2) ?? HudArgument.FromInt(0),
        };
        if (line.Arguments.Count > 3 || mode != current) args.Add(HudArgument.FromString(EngineArgs.RelativeModeNames[(int)mode]));
        args.AddRange(line.Arguments.Skip(4));
        return args.SequenceEqual(line.Arguments) ? null : args;
    }

    /// <summary>A number written as the one it replaces was where it can be, a new one whole where it is whole.</summary>
    public static HudArgument Number(double value, HudArgument? was)
    {
        var rounded = Math.Round(value, 6);
        if (Math.Abs(rounded) < 5e-7) rounded = 0;
        return (was is null || was.Kind == HudArgKind.Integer) && Math.Abs(rounded - Math.Round(rounded)) < 1e-6
            ? HudArgument.FromInt((long)Math.Round(rounded))
            : HudArgument.FromFloat(rounded);
    }
}
