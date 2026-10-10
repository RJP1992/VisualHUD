using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App.Views;

/// <summary>The screen shape to rewrite a file for, with GameExt's AuthoredRatio, keeping its look at that shape.</summary>
public partial class ScreenShapeDialog : Window
{
    /// <summary>The shapes screens come in: a name, and the ratio as AuthoredRatio writes it.</summary>
    public static readonly IReadOnlyList<(string Name, int Width, int Height)> Shapes = new[]
    {
        ("4:3", 4, 3),
        ("5:4", 5, 4),
        ("16:10", 16, 10),
        ("16:9", 16, 9),
        ("21:9, 2560×1080 (64:27)", 64, 27),
        ("21:9, 3440×1440 (43:18)", 43, 18),
        ("32:9", 32, 9),
    };

    private readonly int _height;

    public ScreenShapeDialog(string fileName, int screenHeight, (int Width, int Height)? current)
    {
        InitializeComponent();
        _height = screenHeight;
        IntroText.Text = $"Rewrites {fileName} for GameExt's AuthoredRatio. On a screen of the shape chosen it draws as it does now; " +
                         "on other screens GameExt fits it at the real height and slides its pieces to their edges.";
        foreach (var (name, w, h) in Shapes)
        {
            var button = new RadioButton
            {
                Content = name,
                GroupName = "Shape",
                Tag = (w, h),
                Margin = new Thickness(0, 0, 16, 6),
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            };
            button.Checked += Shape_Changed;
            ShapeList.Children.Add(button);
        }
        var start = current ?? (16, 9);
        var preset = ShapeList.Children.OfType<RadioButton>().FirstOrDefault(b => b.Tag is (int w, int h) && w * start.Height == h * start.Width);
        if (preset is not null) preset.IsChecked = true;
        else
        {
            OtherWidth.Text = start.Width.ToString(CultureInfo.InvariantCulture);
            OtherHeight.Text = start.Height.ToString(CultureInfo.InvariantCulture);
            OtherShape.IsChecked = true;
        }
        ShowReference();
    }

    /// <summary>The shape chosen, as AuthoredRatio(w, h) takes it.</summary>
    public int RatioWidth { get; private set; }
    public int RatioHeight { get; private set; }

    /// <summary>The shape picked or typed, or null with a typed one AuthoredRatio does not take.</summary>
    private (int Width, int Height)? Chosen()
    {
        if (ShapeList.Children.OfType<RadioButton>().FirstOrDefault(b => b.IsChecked == true) is { Tag: (int w, int h) }) return (w, h);
        if (OtherShape.IsChecked != true) return null;
        return int.TryParse(OtherWidth.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ow) &&
               int.TryParse(OtherHeight.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var oh) &&
               ow > 0 && oh > 0 && TrueWidescreen.ValidRatio(ow / (float)oh)
            ? (ow, oh)
            : null;
    }

    private void ShowReference()
    {
        if (ReferenceText is null) return;
        if (Chosen() is not { } shape)
        {
            ReferenceText.Text = "AuthoredRatio takes two whole numbers whose ratio is from 1:1 to 4:1, such as 16 and 9.";
            RewriteButton.IsEnabled = false;
            return;
        }
        var reference = HudRatioMigration.ReferenceFor(shape.Width, shape.Height, _height);
        ReferenceText.Text = $"It keeps the look at {reference.Width}×{reference.Height}: the canvas's height, at that shape. " +
                             $"Choose that Screen on the canvas to compare.";
        RewriteButton.IsEnabled = true;
    }

    private void Shape_Changed(object sender, RoutedEventArgs e) => ShowReference();

    private void Other_Changed(object sender, TextChangedEventArgs e)
    {
        if (OtherShape is null) return;
        OtherShape.IsChecked = true;
        ShowReference();
    }

    private void Rewrite_Click(object sender, RoutedEventArgs e)
    {
        if (Chosen() is not { } shape) return;
        (RatioWidth, RatioHeight) = shape;
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
