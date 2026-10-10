using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SwbfHudEditor.App.Views;

/// <summary>Pick a colour; <see cref="ColorChanged"/> fires as it changes, for a live preview.</summary>
public partial class ColorPicker : UserControl
{
    // A few colours the stock HUDs use, then the primaries.
    private static readonly Color[] PresetColors =
    {
        Colors.White, Color.FromRgb(160, 160, 224), Color.FromRgb(192, 224, 224), Color.FromRgb(255, 200, 0),
        Color.FromRgb(255, 50, 50), Color.FromRgb(0, 150, 255), Color.FromRgb(0, 255, 0), Colors.Black,
    };

    private double _hue;
    private double _saturation;
    private double _value = 1;
    private bool _dragging;

    public ColorPicker()
    {
        InitializeComponent();
        foreach (var preset in PresetColors)
        {
            var chip = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(preset),
                BorderBrush = (Brush)FindResource("BorderStrongBrush"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = Hex(preset),
            };
            chip.MouseLeftButtonUp += (_, _) => Color = preset;
            Presets.Children.Add(chip);
        }
        Loaded += (_, _) => Draw();
    }

    public event EventHandler<Color>? ColorChanged;

    public Color Color
    {
        get => FromHsv(_hue, _saturation, _value);
        set
        {
            (_hue, _saturation, _value) = ToHsv(value, _hue);
            Draw();
            ColorChanged?.Invoke(this, Color);
        }
    }

    private void Draw()
    {
        var color = Color;
        HueFill.Fill = new SolidColorBrush(FromHsv(_hue, 1, 1));
        Swatch.Background = new SolidColorBrush(color);
        if (!HexBox.IsKeyboardFocused) HexBox.Text = Hex(color);
        if (SvArea.ActualWidth > 0)
        {
            Canvas.SetLeft(SvThumb, _saturation * SvArea.ActualWidth - SvThumb.Width / 2);
            Canvas.SetTop(SvThumb, (1 - _value) * SvArea.ActualHeight - SvThumb.Height / 2);
        }
        if (HueArea.ActualWidth > 0) Canvas.SetLeft(HueThumb, _hue / 360 * HueArea.ActualWidth - HueThumb.Width / 2);
    }

    private void SvArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        SvArea.CaptureMouse();
        SetSv(e.GetPosition(SvArea));
    }

    private void SvArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging && SvArea.IsMouseCaptured) SetSv(e.GetPosition(SvArea));
    }

    private void SetSv(Point p)
    {
        _saturation = Math.Clamp(p.X / SvArea.ActualWidth, 0, 1);
        _value = 1 - Math.Clamp(p.Y / SvArea.ActualHeight, 0, 1);
        Draw();
        ColorChanged?.Invoke(this, Color);
    }

    private void HueArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        HueArea.CaptureMouse();
        SetHue(e.GetPosition(HueArea));
    }

    private void HueArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging && HueArea.IsMouseCaptured) SetHue(e.GetPosition(HueArea));
    }

    private void SetHue(Point p)
    {
        _hue = Math.Clamp(p.X / HueArea.ActualWidth, 0, 1) * 360;
        Draw();
        ColorChanged?.Invoke(this, Color);
    }

    private void Area_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyHex();
        e.Handled = true;
    }

    private void HexBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyHex();

    private void ApplyHex()
    {
        var text = HexBox.Text.Trim().TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            Color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        else HexBox.Text = Hex(Color);
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Hue in degrees, saturation and value 0 to 1; a grey keeps the hue it had.</summary>
    private static (double H, double S, double V) ToHsv(Color c, double keepHue)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var h = keepHue;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
            if (h < 0) h += 360;
        }
        return (h, max == 0 ? 0 : delta / max, max);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (h % 360) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
