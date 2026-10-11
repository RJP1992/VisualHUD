using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;
using static SwbfHudEditor.Core.Edits.HudPlacement;

namespace SwbfHudEditor.App;

/// <summary>Copies of the selection in a row: each a step further on the screen, named as Duplicate names them.</summary>
public partial class MainWindow
{
    public static readonly RoutedCommand RepeatCommand = new("Repeat", typeof(MainWindow));

    private void SetUpRepeat()
    {
        CommandBindings.Add(new CommandBinding(RepeatCommand, (_, _) => AskRepeat(), (_, e) =>
            e.CanExecute = !TypingInBox && TopSelectedBlocks().Any(IsElement)));
        InputBindings.Add(new KeyBinding(RepeatCommand, Key.D, ModifierKeys.Control | ModifierKeys.Shift));
    }

    private void AskRepeat()
    {
        var blocks = TopSelectedBlocks().Where(IsElement).ToList();
        if (blocks.Count == 0) return;
        // A step as wide as the selection and a little more, to start from.
        var bounds = Rect.Empty;
        foreach (var block in blocks)
            if (Canvas.ScreenBoundsOf(block) is { } b) bounds.Union(b);
        var dialog = new RepeatDialog(bounds.IsEmpty ? 50 : Math.Round(bounds.Width + 8)) { Owner = this };
        if (dialog.ShowDialog() == true) RepeatSelection(dialog.Count, dialog.StepAcross, dialog.StepDown);
    }

    /// <summary>
    /// <paramref name="count"/> copies of each selected element after it, the n-th moved n steps of
    /// (<paramref name="dx"/>, <paramref name="dy"/>) screen pixels through its parent's matrix, in one undo step.
    /// </summary>
    internal int RepeatSelection(int count, double dx, double dy)
    {
        var blocks = TopSelectedBlocks().Where(IsElement).ToList();
        if (blocks.Count == 0 || count < 1) return 0;
        var taken = IsNameTaken();
        var events = IsEventTaken();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copies = new List<HudBlock>();
        var edits = new List<IHudEdit>();
        // Later ones first in each list, so each original's index still holds when its copies go in.
        foreach (var block in blocks.OrderByDescending(b => Locate(b)?.File.Document.FindContainer(b)?.Index ?? 0))
        {
            if (Locate(block) is not { } at || at.File.Document.FindContainer(block) is not { } container) continue;
            var laid = Canvas.LaidOut(block);
            var made = new List<HudBlock>();
            for (var n = 1; n <= count; n++)
            {
                var copy = HudClipboard.Clone(block);
                HudClipboard.MakeNamesUnique(new[] { copy }, name => taken(name) || names.Contains(name), events);
                foreach (var b in new[] { copy }.Concat(AllBlocks(copy.Children)).Where(b => b.HasBody && b.ItemName.Length > 0)) names.Add(b.ItemName);
                if (laid is not null) Step(copy, laid, n * dx, n * dy);
                var insert = new InsertBlockEdit(container.Container, container.Index + n, copy, $"repeat {block.Name}", affected: copy);
                insert.Apply();
                edits.Add(insert);
                made.Add(copy);
            }
            copies.InsertRange(0, made);
        }
        if (edits.Count == 0) return 0;
        _undo.Push(new CompositeEdit(edits, $"repeat {blocks.Count} element{(blocks.Count == 1 ? "" : "s")} {count} times"));
        AfterBlocksChanged(null);
        SelectBlocks(copies);
        StatusText.Text = $"Made {copies.Count} cop{(copies.Count == 1 ? "y" : "ies")}, each {dx:0.#}, {dy:0.#} pixels on from the last.";
        return copies.Count;
    }

    /// <summary>
    /// A copy moved by screen pixels through the matrix its original's parent draws it with, in its Position's
    /// unit; one in pixels, or none, in Viewport (<see cref="WriteMode"/>).
    /// </summary>
    private static void Step(HudBlock copy, LaidOutElement original, double dx, double dy)
    {
        var role = original.Kind == ElementKind.VehicleSeating ? LineRole.Seating : LineRole.Own;
        var line = Last(copy, "Position");
        if (line is not null && Scaled(line, role, original) is { } scaled)
        {
            line.Arguments.Clear();
            line.Arguments.AddRange(scaled);
        }
        var mode = line is null ? WriteMode(RelativeMode.Pixels, role, original) : ReadMode(line, role);
        var delta = original.PositionDeltaFor((float)dx, (float)dy, mode);
        if (MathF.Abs(delta.X) < 1e-7f && MathF.Abs(delta.Y) < 1e-7f) return;
        if (line is not null)
        {
            line.Arguments[0] = Number(EngineArgs.Float(line.Arguments[0]) + delta.X, line.Arguments[0]);
            line.Arguments[1] = Number(EngineArgs.Float(line.Arguments[1]) + delta.Y, line.Arguments[1]);
            return;
        }
        var made = new List<HudArgument> { Number(delta.X, null), Number(delta.Y, null), HudArgument.FromInt(0) };
        if (mode != RelativeMode.Pixels) made.Add(HudArgument.FromString(EngineArgs.RelativeModeNames[(int)mode]));
        copy.Children.Insert(copy.Children.TakeWhile(c => !c.HasBody).Count(), new HudBlock("Position", made.ToArray()));
    }
}
