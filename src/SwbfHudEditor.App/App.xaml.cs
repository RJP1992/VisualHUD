using System.Windows;
using System.Windows.Threading;

namespace SwbfHudEditor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The tests make the application for its theme and open their own windows.
        if (Environment.GetEnvironmentVariable("VISUALHUD_TESTS") == "1") return;
        if (Snapshot.TryRun(e.Args, out var exitCode))
        {
            Shutdown(exitCode);
            return;
        }
        if (UiSnapshot.TryRun(e.Args, this)) return;
        var window = new MainWindow();
        window.Show();
        // The first start asks where the mod tools are, once the window is up.
        if (window.FirstStart)
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => window.AskModTools(firstStart: true));
    }
}
