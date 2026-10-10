using System.IO;
using System.Windows;
using System.Windows.Threading;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SwbfHudEditor.App.Tests;

/// <summary>
/// One UI thread for every test, with the application and its theme, as WPF wants one dispatcher for
/// its windows. Windows open off the screen and are never activated, the settings are a scratch file
/// and no asset folder is read, so a test run changes nothing on the desktop or in the user's settings.
/// </summary>
public static class UiThread
{
    private static readonly Lazy<Dispatcher> Ui = new(Start);

    private static Dispatcher Start()
    {
        var scratch = Directory.CreateTempSubdirectory("visualhud-tests-").FullName;
        Environment.SetEnvironmentVariable("VISUALHUD_SETTINGS", Path.Combine(scratch, "settings.json"));
        Environment.SetEnvironmentVariable("VISUALHUD_NO_SCAN", "1");
        Environment.SetEnvironmentVariable("VISUALHUD_NO_WATCH", "1");
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "UI tests",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    /// <summary>Run <paramref name="body"/> on the UI thread, with a window that has these files open.</summary>
    public static void WithWindow(string hudText, Action<MainWindow, string> body) => WithWindow(hudText, null, body);

    /// <summary>The same, with more files beside the .hud file (textures, say), by name.</summary>
    public static void WithWindow(string hudText, IReadOnlyDictionary<string, byte[]>? files, Action<MainWindow, string> body)
    {
        var folder = Directory.CreateTempSubdirectory("visualhud-hud-").FullName;
        var path = Path.Combine(folder, "test.hud");
        File.WriteAllText(path, hudText);
        foreach (var (name, bytes) in files ?? new Dictionary<string, byte[]>()) File.WriteAllBytes(Path.Combine(folder, name), bytes);
        Exception? failure = null;
        Ui.Value.Invoke(() =>
        {
            var window = new MainWindow
            {
                StartupFiles = new[] { path },
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000,
                Top = -30000,
                Width = 1400,
                Height = 900,
            };
            try
            {
                window.Show();
                Settle();
                body(window, path);
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                // Saved or not, close without asking.
                window.CloseWithoutAsking = true;
                window.Close();
            }
        });
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        if (failure is not null) throw new AggregateException(failure);
    }

    /// <summary>Let the dispatcher run what is queued: layout, bindings, deferred work.</summary>
    public static void Settle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
