using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;

namespace SpotifyDeck.Services;

public sealed class BridgeInstallerService
{
    private readonly string _extensionName = "spotifydeck-bridge.js";

    private string TargetPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "spicetify", "Extensions", _extensionName);

    public bool IsInstalled() => File.Exists(TargetPath);

    public async Task<bool> EnsureInstalledAsync()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "bridge", _extensionName);
        if (!File.Exists(source))
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TargetPath)!);

            // Avoid restarting/reapplying Spicetify on every SpotifyDeck launch.
            // Re-apply only when the bundled bridge actually changed.
            if (File.Exists(TargetPath) && FilesMatch(source, TargetPath))
                return true;

            File.Copy(source, TargetPath, true);
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("bridge-install", ex);
            return false;
        }

        if (!await RunSpicetifyAsync($"config extensions {_extensionName}"))
            return false;

        return await RunSpicetifyAsync("apply");
    }

    private static bool FilesMatch(string left, string right)
    {
        try
        {
            using var a = File.OpenRead(left);
            using var b = File.OpenRead(right);
            if (a.Length != b.Length) return false;

            var hashA = SHA256.HashData(a);
            var hashB = SHA256.HashData(b);
            return hashA.AsSpan().SequenceEqual(hashB);
        }
        catch
        {
            return false;
        }
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
            if (process.ExitCode == 0)
                return true;

            var error = await process.StandardError.ReadToEndAsync();
            await AppLog.WriteAsync("spicetify", $"spicetify {arguments} failed ({process.ExitCode}): {error}");
            return false;
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("spicetify", ex);
            return false;
        }
    }
}
