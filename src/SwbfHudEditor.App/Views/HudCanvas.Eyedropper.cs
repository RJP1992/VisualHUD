using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SwbfHudEditor.App.Views;

/// <summary>
/// Picking a colour off the canvas: the next click takes the reference image's own pixel there when it shows
/// (whatever its opacity), else the HUD's as drawn. Esc lets it go.
/// </summary>
public partial class HudCanvas
{
    private Action<Color>? _pickColor;

    /// <summary>Whether the next click picks a colour.</summary>
    internal bool PickingColor => _pickColor is not null;

    /// <summary>Take the colour under the next click on the canvas and hand it to <paramref name="picked"/>.</summary>
    public void BeginPickColor(Action<Color> picked)
    {
        _pickColor = picked;
        CanvasSurface.Cursor = Cursors.Cross;
        Focus();
        HoverInfoChanged?.Invoke(this, "Click the canvas to take its colour there: the reference image's when it shows, else the HUD's. Esc lets it go.");
    }

    private void EndPickColor()
    {
        _pickColor = null;
        CanvasSurface.Cursor = null;
        HoverInfoChanged?.Invoke(this, null);
    }

    /// <summary>The colour at a point of the screen, handed to whoever asked for it: the tests' way in.</summary>
    internal Color? PickColorAt(Point pos)
    {
        if (_pickColor is not { } picked) return null;
        EndPickColor();
        if (ColorAt(pos) is not { } color) return null;
        picked(color);
        return color;
    }

    /// <summary>
    /// The reference image's pixel at a point of the screen when it shows there (left of a wipe's divider),
    /// else the HUD's as drawn.
    /// </summary>
    private Color? ColorAt(Point pos)
    {
        if (pos.X < 0 || pos.Y < 0 || pos.X >= _screen.Width || pos.Y >= _screen.Height) return null;
        if (_reference is { } reference && ReferenceBox.IsChecked == true &&
            (ReferenceWipeBox.IsChecked != true || pos.X < _wipe * _screen.Width))
            return Pixel(reference, (int)(pos.X / _screen.Width * reference.PixelWidth), (int)(pos.Y / _screen.Height * reference.PixelHeight));
        return Pixel(RenderForExport(), (int)pos.X, (int)pos.Y);
    }

    private static Color Pixel(BitmapSource image, int x, int y)
    {
        x = Math.Clamp(x, 0, image.PixelWidth - 1);
        y = Math.Clamp(y, 0, image.PixelHeight - 1);
        var bytes = new byte[4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0);
        return Color.FromRgb(bytes[2], bytes[1], bytes[0]);
    }
}
