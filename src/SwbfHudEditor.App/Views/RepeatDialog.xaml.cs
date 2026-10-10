using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Views;

/// <summary>How many copies to make in a row, and the step between them in screen pixels.</summary>
public partial class RepeatDialog : Window
{
    public RepeatDialog(double across)
    {
        InitializeComponent();
        AcrossBox.Text = across.ToString("0.#", CultureInfo.InvariantCulture);
        Loaded += (_, _) =>
        {
            CountBox.Focus();
            CountBox.SelectAll();
        };
    }

    public int Count { get; private set; }
    public double StepAcross { get; private set; }
    public double StepDown { get; private set; }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        if (!NumberExpression.TryEvaluate(CountBox.Text, out var count) || count < 1 || count > 500 || count != Math.Round(count))
        {
            MessageBox.Show(this, "Make from 1 to 500 copies.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!NumberExpression.TryEvaluate(AcrossBox.Text, out var across) || !NumberExpression.TryEvaluate(DownBox.Text, out var down))
        {
            MessageBox.Show(this, "The steps are numbers of pixels, or sums of them.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        (Count, StepAcross, StepDown) = ((int)count, across, down);
        DialogResult = true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = 1;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0) DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }
}
