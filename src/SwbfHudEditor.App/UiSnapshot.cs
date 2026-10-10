using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SwbfHudEditor.App;

/// <summary>
/// The whole editor window drawn to a PNG, for checking the interface without showing it: the window
/// opens off screen, never activated, so nothing on the desktop changes.
/// <code>
/// VisualHUD.exe --ui-snapshot out.png file.hud... [--ui-size 1600x900] [--ui-select name] [--ui-left events]
/// </code>
/// </summary>
internal static class UiSnapshot
{
    public static bool TryRun(string[] args, Application app)
    {
        var at = Array.IndexOf(args, "--ui-snapshot");
        if (at < 0 || at + 1 >= args.Length) return false;
        var output = Path.GetFullPath(args[at + 1]);
        var size = new Size(1600, 900);
        string? select = null;
        string? left = null;
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ui-snapshot":
                    i++;
                    break;
                case "--ui-size":
                    var parts = args[++i].ToLowerInvariant().Split('x');
                    size = new Size(double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
                    break;
                case "--ui-select":
                    select = args[++i];
                    break;
                case "--ui-left":
                    left = args[++i];
                    break;
                default:
                    if (args[i].EndsWith(".hud", StringComparison.OrdinalIgnoreCase)) files.Add(Path.GetFullPath(args[i]));
                    break;
            }
        }

        var window = new MainWindow
        {
            StartupFiles = files,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000,
            Top = -30000,
            Width = size.Width,
            Height = size.Height,
        };
        window.Show();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            try
            {
                if (left is not null) window.ShowLeftTab(left);
                if (select is not null) window.SelectByName(select);
                // Let the background asset scans and the first renders settle.
                await Task.Delay(2500);
                window.UpdateLayout();
                Save(window, output);
                app.Shutdown(0);
            }
            catch (Exception e)
            {
                File.WriteAllText(output + ".error.txt", e.ToString());
                app.Shutdown(1);
            }
        });
        return true;
    }

    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var w = (int)Math.Ceiling(content.ActualWidth);
        var h = (int)Math.Ceiling(content.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background ?? Brushes.Black, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, w, h));
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
