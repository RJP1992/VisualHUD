using System.Windows.Input;

namespace SwbfHudEditor.App.Views;

/// <summary>
/// How far a number moves for an arrow key or a notch of the wheel: one for a whole number or a count of
/// pixels, a hundredth for any other fraction; Shift ten times as far, Alt a hundred times, Ctrl a tenth.
/// </summary>
public static class NumberSteps
{
    /// <summary>The wheel turns this much a notch.</summary>
    public const int Notch = 120;

    public static double For(bool whole, ModifierKeys keys)
    {
        var step = whole ? 1.0 : 0.01;
        if (keys.HasFlag(ModifierKeys.Alt)) return step * 100;
        if (keys.HasFlag(ModifierKeys.Shift)) return step * 10;
        if (keys.HasFlag(ModifierKeys.Control)) return step * 0.1;
        return step;
    }

    /// <summary>A number moved by <paramref name="count"/> steps, rounded to the six places a file keeps, never -0.</summary>
    public static double Move(double value, int count, double step)
    {
        var moved = Math.Round(value + count * step, 6);
        return Math.Abs(moved) < 5e-7 ? 0 : moved;
    }

    /// <summary>The tooltip's words for it.</summary>
    public const string Help = "Up and Down, or the wheel once it is clicked, change it: Shift by ten times as much, Alt by a hundred, Ctrl by a tenth";
}
