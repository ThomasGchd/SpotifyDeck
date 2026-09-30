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
        if (Process.GetProcessesByName("Spotify").Length == 0)
        {
            try
            {
                Process.Start(new ProcessStartInfo("spotify:")
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                });
            }
            catch { }
        }

        for (var i = 0; i < 15; i++)
        {
            await Task.Delay(200);
            foreach (var process in Process.GetProcessesByName("Spotify"))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        ShowWindow(process.MainWindowHandle, SwHide);
                }
                catch { }
            }
        }
    }
}
