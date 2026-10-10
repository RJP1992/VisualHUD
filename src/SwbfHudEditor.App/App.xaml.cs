using System.Windows;

namespace SwbfHudEditor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (Snapshot.TryRun(e.Args, out var exitCode))
        {
            Shutdown(exitCode);
            return;
        }
        if (UiSnapshot.TryRun(e.Args, this)) return;
        new MainWindow().Show();
    }
}
