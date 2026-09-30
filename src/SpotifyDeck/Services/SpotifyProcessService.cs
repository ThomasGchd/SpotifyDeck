using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SpotifyDeck.Services;

public sealed class SpotifyProcessService
{
    public async Task<bool> EnsureRunningAsync()
    {
        // A Spotify background process is not proof that the desktop UI/device is ready.
        // Always try to activate the installed client first; the Web API will decide
        // whether its playback device is actually available afterwards.
        foreach (var executable in FindSpotifyExecutables())
        {
            try
            {
                await AppLog.WriteAsync("spotify-launch", $"Activating Spotify from {executable}");
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
                if (await WaitForSpotifyProcessAsync())
                    return true;
            }
            catch (Exception ex)
            {
                await AppLog.WriteAsync("spotify-launch-exe", ex);
            }
        }

        // Spotify's registered URI is the most reliable activation path for the
        // Microsoft Store build and also works with the classic desktop build.
        try
        {
            await AppLog.WriteAsync("spotify-launch", "Activating Spotify via spotify: URI.");
            Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
            await Task.Delay(1200);
            return true;
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("spotify-launch-uri", ex);
        }

        // Last resort for the Microsoft Store package.
        try
        {
            await AppLog.WriteAsync("spotify-launch", "Activating Spotify via AppsFolder.");
            Process.Start(new ProcessStartInfo(
                "explorer.exe",
                @"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")
            {
                UseShellExecute = true
            });
            await Task.Delay(1200);
            return true;
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("spotify-launch-store", ex);
        }

        return false;
    }

    private static IEnumerable<string> FindSpotifyExecutables()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string?>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Spotify", "Spotify.exe")
        };

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\App Paths\Spotify.exe");
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
        for (var i = 0; i < 20; i++)
        {
            if (Process.GetProcessesByName("Spotify").Any(p =>
            {
                try { return !p.HasExited; }
                catch { return false; }
            }))
                return true;

            await Task.Delay(250);
        }

        return false;
    }
}
