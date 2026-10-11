using System.Numerics;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;
using static SwbfHudEditor.Core.Edits.HudPlacement;

namespace SwbfHudEditor.Core.Edits;

/// <summary>
/// Where elements are drawn, taken before what holds them changes (a new group round them, another group, a
/// parent's frame moved) and given back after it: each position line written again against the parent as it
/// then draws, in its own unit (Pixels made Viewport, <see cref="HudPlacement.WriteMode"/>), so the element
/// stays where it was on the screen. An element at its parent's origin gets a Position, in Viewport, if it
/// would move; a parent that scales otherwise along the screen's axes is made up for by the element's own
/// Scale. Elements inside the ones kept move with them.
/// </summary>
public sealed class HudKeepPlace
{
    private sealed record Kept(Matrix4x4 Parent, List<(HudBlock Line, LineRole Role, Vector2 Point)> Lines, Vector2? Origin);

    private readonly Dictionary<HudBlock, Kept> _kept = new(ReferenceEqualityComparer.Instance);

    /// <summary>The elements' places as <paramref name="layout"/> draws them, before the change.</summary>
    public static HudKeepPlace Capture(HudLayoutResult layout, IEnumerable<HudBlock> elements)
    {
        var kept = new HudKeepPlace();
        foreach (var block in elements)
        {
            if (layout.Find(block) is not { } e || ParentToScreen(e) is not { } parent) continue;
            var lines = PositionLines(e).Select(x => (x.Line, x.Role, OnScreen(PixelsOf(x.Line, x.Role, e), parent))).ToList();
            kept._kept[block] = new Kept(parent, lines, PlacedByOrigin(e) ? OnScreen(Vector3.Zero, parent) : null);
        }
        return kept;
    }

    /// <summary>
    /// The edits, made already, that put each element back where it was, against <paramref name="layout"/> drawn
    /// after the change. What cannot be made up for (a parent turned otherwise) is said in <paramref name="notes"/>.
    /// </summary>
    public List<IHudEdit> Restore(HudLayoutResult layout, ICollection<string>? notes = null)
    {
        var edits = new List<IHudEdit>();
        void Apply(IHudEdit edit)
        {
            edit.Apply();
            edits.Add(edit);
        }

        foreach (var (block, kept) in _kept)
        {
            if (layout.Find(block) is not { } e || ParentToScreen(e) is not { } parent) continue;
            foreach (var (line, role, point) in kept.Lines)
                if (Place(line, role, e, point, parent, viewport: false) is { } args)
                    Apply(new ReplaceArgumentsEdit(line, args, block));

            if (kept.Origin is { } origin && Last(block, "Position") is null &&
                Vector2.Distance(origin, OnScreen(Vector3.Zero, parent)) > 1e-3f)
            {
                var made = new HudBlock("Position", HudArgument.FromInt(0), HudArgument.FromInt(0), HudArgument.FromInt(0));
                var role = e.Kind == ElementKind.VehicleSeating ? LineRole.Seating : LineRole.Own;
                if (Place(made, role, e, origin, parent, viewport: false) is { } args)
                {
                    made.Arguments.Clear();
                    made.Arguments.AddRange(args);
                    Apply(new InsertBlockEdit(block.Children, block.Children.TakeWhile(c => !c.HasBody).Count(), made, "add Position", affected: block));
                }
            }

            MakeUpScale(block, kept.Parent, parent, Apply, notes);
        }
        return edits;
    }

    /// <summary>
    /// A parent that now scales otherwise: the element's own matrix must take what the parents gave before and
    /// not now, <c>before × after⁻¹</c>. Along the screen's axes that is its Scale line multiplied; a turn is not
    /// made up for.
    /// </summary>
    private static void MakeUpScale(HudBlock block, Matrix4x4 before, Matrix4x4 after, Action<IHudEdit> apply, ICollection<string>? notes)
    {
        var det = Determinant2(after);
        if (MathF.Abs(det) < 1e-12f) return;
        var (a, b, c, d) = (after.M22 / det, -after.M12 / det, -after.M21 / det, after.M11 / det);
        var c11 = before.M11 * a + before.M12 * c;
        var c12 = before.M11 * b + before.M12 * d;
        var c21 = before.M21 * a + before.M22 * c;
        var c22 = before.M21 * b + before.M22 * d;
        const float near = 1e-4f;
        if (MathF.Abs(c11 - 1) < near && MathF.Abs(c22 - 1) < near && MathF.Abs(c12) < near && MathF.Abs(c21) < near) return;

        var name = $"{block.Name} \"{block.ItemName}\"";
        var turnedOwn = block.Children.Any(l => !l.HasBody && l.Name == "Rotation" && l.Arguments.Any(x => EngineArgs.IsNumber(x) && EngineArgs.Float(x) != 0f));
        if (MathF.Abs(c12) > near || MathF.Abs(c21) > near || turnedOwn && MathF.Abs(c11 - c22) > near)
        {
            notes?.Add($"{name} is in something turned otherwise now: its place is kept, not its turn or size.");
            return;
        }

        var scale = block.Children.LastOrDefault(l => !l.HasBody && l.Name == "Scale" && l.Arguments.Count >= 3);
        var sx = scale is null ? 1f : EngineArgs.FloatAt(scale, 0);
        var sy = scale is null ? 1f : EngineArgs.FloatAt(scale, 1);
        var args = new List<HudArgument>
        {
            Number(sx * c11, scale?.Arguments[0]),
            Number(sy * c22, scale?.Arguments[1]),
            scale?.Arguments[2] ?? HudArgument.FromInt(1),
        };
        if (scale is not null) args.AddRange(scale.Arguments.Skip(3));
        if (scale is null)
            apply(new InsertBlockEdit(block.Children, block.Children.TakeWhile(l => !l.HasBody).Count(),
                new HudBlock("Scale", args.ToArray()), "add Scale", affected: block));
        else if (!args.SequenceEqual(scale.Arguments))
            apply(new ReplaceArgumentsEdit(scale, args, block));
    }
}
