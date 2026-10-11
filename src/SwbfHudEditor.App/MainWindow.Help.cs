using System.IO;
using System.Windows;
using System.Windows.Input;
using SwbfHudEditor.App.Views;

namespace SwbfHudEditor.App;

/// <summary>Help: the guide (docs/Guide.md, built into the editor), F1, and About.</summary>
public partial class MainWindow
{
    private GuideWindow? _guide;

    private void SetUpHelp()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (_, _) => ShowGuide()));
    }

    /// <summary>The guide's text, as built into the editor.</summary>
    internal static string GuideText()
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Guide.md");
        if (stream is null) return "# VisualHUD guide\n\nThe guide is not built into this copy of the editor.";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Open the guide beside the editor, or bring it forward when it is open.</summary>
    internal GuideWindow ShowGuide()
    {
        if (_guide is { IsLoaded: true })
        {
            if (_guide.WindowState == WindowState.Minimized) _guide.WindowState = WindowState.Normal;
            _guide.Activate();
            return _guide;
        }
        _guide = new GuideWindow(GuideText()) { Owner = this };
        _guide.Closed += (_, _) => _guide = null;
        _guide.Show();
        return _guide;
    }

    private void Guide_Click(object sender, RoutedEventArgs e) => ShowGuide();

    private void About_Click(object sender, RoutedEventArgs e) => new AboutDialog { Owner = this }.ShowDialog();
}
