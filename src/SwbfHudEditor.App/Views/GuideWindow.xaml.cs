using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SwbfHudEditor.App.Views;

/// <summary>The guide, docs/Guide.md as built into the editor, to read beside it.</summary>
public partial class GuideWindow : Window
{
    public GuideWindow(string markdown)
    {
        InitializeComponent();
        Viewer.Document = MarkdownFlow.Build(markdown, this);
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
