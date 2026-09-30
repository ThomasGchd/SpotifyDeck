using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SpotifyDeck.Services;

public sealed class SpotifyProcessService
{
    public async Task<bool> EnsureRunningAsync()
    {
        if (Process.GetProcessesByName("Spotify").Length > 0)
            return true;

        foreach (var executable in FindSpotifyExecutables())
        {
            try
            {
                await AppLog.WriteAsync("spotify-launch", $"Launching Spotify from {executable}");
                Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = true
                });

                if (await WaitForSpotifyProcessAsync())
                    return true;
            }
            catch (Exception ex)
            {
                await AppLog.WriteAsync("spotify-launch", ex);
            }
        }

        try
        {
            await AppLog.WriteAsync("spotify-launch", "Launching Spotify via spotify: URI.");
            Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
            if (await WaitForSpotifyProcessAsync())
                return true;
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("spotify-launch-uri", ex);
        }

        await AppLog.WriteAsync("spotify-launch", "Spotify process was not detected after launch attempts.");
        return false;
    }

    private static IEnumerable<string> FindSpotifyExecutables()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<string?>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Spotify", "Spotify.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "Spotify.exe")
        };

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\Spotify.exe");
            candidates.Add(key?.GetValue(null) as string);
        }
        catch { }

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                continue;

            var full = Path.GetFullPath(candidate);
            if (seen.Add(full))
                yield return full;
        }
    }

    private static async Task<bool> WaitForSpotifyProcessAsync()
    {
        for (var i = 0; i < 40; i++)
        {
            if (Process.GetProcessesByName("Spotify").Length > 0)
                return true;

            await Task.Delay(250);
        }

        return false;
    }
}
