using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpotifyDeck.Services;

public sealed class SpotifyProcessService
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SwHide = 0;

    public async Task EnsureRunningHiddenAsync()
    {
        // Never hide a Spotify window the user already had open. SpotifyDeck only
        // suppresses the window it launches itself in the background.
        if (Process.GetProcessesByName("Spotify").Length > 0)
            return;

        try
        {
            Process.Start(new ProcessStartInfo("spotify:")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized
            });
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("spotify-launch", ex);
            return;
        }

        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(150);

            foreach (var process in Process.GetProcessesByName("Spotify"))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        ShowWindow(process.MainWindowHandle, SwHide);
                }
                catch
                {
                    // Spotify may restart helper processes while booting.
                }
            }
        }
    }
}
