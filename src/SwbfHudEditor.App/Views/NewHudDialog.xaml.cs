using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace SwbfHudEditor.App.Views;

/// <summary>Where a new .hud file goes, its name, and the layout its FileInfo asks for.</summary>
public partial class NewHudDialog : Window
{
    public NewHudDialog(string folder, bool canAdd)
    {
        InitializeComponent();
        FolderBox.Text = folder;
        AddToOpenBox.IsEnabled = canAdd;
        AddToOpenBox.IsChecked = canAdd;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>The file to create.</summary>
    public string FilePath { get; private set; } = string.Empty;

    /// <summary>The file's text: its FileInfo.</summary>
    public string Text { get; private set; } = string.Empty;

    public bool AddToOpen => AddToOpenBox.IsEnabled && AddToOpenBox.IsChecked == true;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Folder for the new HUD file",
            InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null,
        };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.EndsWith(".hud", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('"'))
        {
            Warn("Give the file a name a file can have.");
            return;
        }
        if (FolderBox.Text.Trim() is not { Length: > 0 } folder)
        {
            Warn("Choose a folder.");
            return;
        }
        string? layout = null;
        if (TrueWidescreenLayout.IsChecked == true) layout = "TrueWidescreen(1)";
        else if (AuthoredLayout.IsChecked == true)
        {
            if (!double.TryParse(RatioWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ||
                !double.TryParse(RatioHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ||
                w <= 0 || h <= 0 || w / h < 1 || w / h > 4)
            {
                Warn("AuthoredRatio takes two positive numbers whose ratio is from 1:1 to 4:1, such as 16 and 9.");
                return;
            }
            layout = $"AuthoredRatio({RatioWidth.Text.Trim()}, {RatioHeight.Text.Trim()})";
        }
        // The game's tools write a file's lines with CRLF and a tab indent.
        Text = $"FileInfo(\"{name}\")\r\n{{\r\n\tViewports(1)\r\n{(layout is null ? "" : $"\t{layout}\r\n")}}}\r\n";
        FilePath = Path.Combine(folder, name + ".hud");
        DialogResult = true;
    }

    private void Warn(string text) => MessageBox.Show(this, text, Title, MessageBoxButton.OK, MessageBoxImage.Information);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = 1;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0) DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }
}
