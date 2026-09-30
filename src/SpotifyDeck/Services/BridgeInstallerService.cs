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

    public bool NeedsUpdate()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "bridge", _extensionName);
        return File.Exists(source) &&
               (!File.Exists(TargetPath) || !FilesMatch(source, TargetPath));
    }

    public async Task<bool> EnsureInstalledAsync()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "bridge", _extensionName);
        if (!File.Exists(source))
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TargetPath)!);

            if (File.Exists(TargetPath) && FilesMatch(source, TargetPath))
                return true;

            File.Copy(source, TargetPath, true);
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("bridge-install", ex);
            return false;
        }

        var executable = FindSpicetifyExecutable();
        if (executable is null)
        {
            // The extension file has still been refreshed. Existing Spicetify
            // installs may already have it enabled, but Spotify must be
            // restarted before the new JS is loaded.
            await AppLog.WriteAsync(
                "spicetify",
                "Spicetify executable not found. Bridge file updated but could not run config/apply.");
            return IsInstalled();
        }

        if (!await RunSpicetifyAsync(executable, $"config extensions {_extensionName}"))
            return false;

        return await RunSpicetifyAsync(executable, "apply");
    }

    private static string? FindSpicetifyExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "spicetify-cli", "spicetify.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".spicetify", "spicetify.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "spicetify", "spicetify.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "spicetify", "spicetify.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages", "Spicetify.Spicetify_Microsoft.Winget.Source_8wekyb3d8bbwe", "spicetify.exe")
        };

        var direct = candidates.FirstOrDefault(File.Exists);
        if (direct is not null)
            return direct;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "spicetify.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { }
        }

        return null;
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

    private static async Task<bool> RunSpicetifyAsync(string executable, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
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
