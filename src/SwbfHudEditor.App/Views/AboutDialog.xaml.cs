using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SwbfHudEditor.App.Views;

/// <summary>What VisualHUD is, its version, and who made it.</summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Version}";
    }

    /// <summary>The editor's version, as its project gives it: 1.0.0.</summary>
    internal static string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = 1;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0) DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }
}
