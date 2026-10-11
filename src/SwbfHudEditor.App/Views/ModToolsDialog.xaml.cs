using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace SwbfHudEditor.App.Views;

/// <summary>
/// Where the Battlefront II mod tools are: the BF2_ModTools folder, or anything in it, which is taken back to
/// the folder itself.
/// </summary>
public partial class ModToolsDialog : Window
{
    public ModToolsDialog(string? current, bool firstStart)
    {
        InitializeComponent();
        IntroText.Text = (firstStart ? "Welcome to VisualHUD. " : string.Empty) +
                         "It draws a HUD with the stock textures, models, fonts and strings of the Battlefront II mod tools, " +
                         "and reads their stock HUD files, such as the transforms every HUD uses. Where is your BF2_ModTools folder?" +
                         (firstStart ? " You can change it later from File > Mod tools folder." : string.Empty);
        FolderBox.Text = current ?? string.Empty;
        Check(current);
        Loaded += (_, _) =>
        {
            FolderBox.Focus();
            FolderBox.SelectAll();
        };
    }

    /// <summary>The BF2_ModTools folder chosen, once it is one.</summary>
    public string? Folder { get; private set; }

    private void Check(string? text)
    {
        Folder = ModTools.Resolve(text ?? string.Empty);
        OkButton.IsEnabled = Folder is not null;
        if (Folder is not null)
        {
            StatusText.Text = $"Found the mod tools: {Folder}";
            StatusText.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            StatusText.Text = string.IsNullOrWhiteSpace(text)
                ? "No folder picked. Without the mod tools, a HUD shows only what its own and its asset folders hold."
                : "There is no data\\Common there: pick the BF2_ModTools folder itself.";
            StatusText.Foreground = (Brush)FindResource(string.IsNullOrWhiteSpace(text) ? "TextMutedBrush" : "WarningBrush");
        }
    }

    private void FolderBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Check(FolderBox.Text);

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "The BF2_ModTools folder" };
        var start = Folder ?? FolderBox.Text.Trim();
        if (Directory.Exists(start)) dialog.InitialDirectory = start;
        if (dialog.ShowDialog(this) == true) FolderBox.Text = ModTools.Resolve(dialog.FolderName) ?? dialog.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Folder is not null) DialogResult = true;
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
