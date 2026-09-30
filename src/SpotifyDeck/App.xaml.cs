using System.IO;
using System.Threading;
using System.Windows;

namespace SpotifyDeck;

public partial class App : System.Windows.Application
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

        try
        {
            var window = new MainWindow();
            MainWindow = window;

            // First launch should never look like a crash or a no-op.
            // Show the overlay immediately, then initialize background services.
            window.Show();
            window.Activate();

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

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
