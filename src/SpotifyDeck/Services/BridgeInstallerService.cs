using System.IO;
using System.Diagnostics;

namespace SpotifyDeck.Services;

public sealed class BridgeInstallerService
{
    private readonly string _extensionName = "spotifydeck-bridge.js";

    public bool IsInstalled()
    {
        var target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "spicetify", "Extensions", _extensionName);

        return File.Exists(target);
    }

    public async Task<bool> EnsureInstalledAsync()
    {
        var appDir = AppContext.BaseDirectory;
        var source = Path.Combine(appDir, "bridge", _extensionName);

        if (!File.Exists(source))
            return false;

        var extensionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "spicetify", "Extensions");

        try
        {
            Directory.CreateDirectory(extensionDir);
            File.Copy(source, Path.Combine(extensionDir, _extensionName), true);
        }
        catch
        {
            return false;
        }

        if (!await RunSpicetifyAsync($"config extensions {_extensionName}"))
            return false;

        return await RunSpicetifyAsync("apply");
    }

    private static async Task<bool> RunSpicetifyAsync(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "spicetify",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
                return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
