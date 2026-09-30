using System.IO;
using System.Threading;
using System.Windows;

namespace SpotifyDeck;

public partial class App : System.Windows.Application
{
    private const string MutexName = "SpotifyDeck.SingleInstance";
    private const string ShowEventName = "SpotifyDeck.ShowExisting";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private ManualResetEvent? _shutdownSignal;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowEventName);
                signal.Set();
            }
            catch { }

            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _shutdownSignal = new ManualResetEvent(false);

            var window = new MainWindow();
            MainWindow = window;

            StartExistingInstanceListener(window);

            var background = e.Args.Any(x =>
                x.Equals("--background", StringComparison.OrdinalIgnoreCase));

            // Manual launch opens SpotifyDeck immediately. Windows autostart keeps
            // it in the tray so it never interrupts login or steals focus.
            if (!background)
            {
                window.Show();
                window.Activate();
            }

            await window.InitializeAsync();
        }
        catch (Exception ex)
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpotifyDeck",
                "logs");
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, "startup.log");
            await File.AppendAllTextAsync(
                logPath,
                $"[{DateTime.Now:O}] {ex}\r\n\r\n");

            System.Windows.MessageBox.Show(
                $"SpotifyDeck n'a pas pu démarrer correctement.\n\nUn diagnostic a été enregistré ici :\n{logPath}",
                "SpotifyDeck",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }

    private void StartExistingInstanceListener(MainWindow window)
    {
        if (_showEvent is null || _shutdownSignal is null)
            return;

        _ = Task.Run(async () =>
        {
            var handles = new WaitHandle[] { _showEvent, _shutdownSignal };

            while (true)
            {
                var index = WaitHandle.WaitAny(handles);
                if (index == 1)
                    return;

                await Dispatcher.InvokeAsync(async () =>
                {
                    await window.ShowOverlayAsync();
                });
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _shutdownSignal?.Set(); } catch { }

        _showEvent?.Dispose();
        _shutdownSignal?.Dispose();

        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
