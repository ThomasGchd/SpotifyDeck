using System.Threading;
using System.Windows;

namespace SpotifyDeck;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "SpotifyDeck.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        await window.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
