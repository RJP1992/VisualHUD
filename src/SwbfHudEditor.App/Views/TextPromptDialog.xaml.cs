using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SwbfHudEditor.App.Views;

/// <summary>A line of text asked for, such as a name.</summary>
public partial class TextPromptDialog : Window
{
    public TextPromptDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Box.Text = initial;
        Loaded += (_, _) =>
        {
            Box.Focus();
            Box.SelectAll();
        };
    }

    public string Text => Box.Text;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Box.Text.Trim().Length == 0) return;
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
