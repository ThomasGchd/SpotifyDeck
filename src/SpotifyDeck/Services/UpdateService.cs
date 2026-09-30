using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace SpotifyDeck.Services;

public sealed class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/ThomasGchd/SpotifyDeck/releases/latest";

    private readonly HttpClient _http = new();

    public UpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SpotifyDeck/1.0");
    }

    public async Task<UpdateInfo?> CheckAsync()
    {
        using var response = await _http.GetAsync(LatestReleaseApi);
        if (!response.IsSuccessStatusCode)
            return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagNode)
            ? tagNode.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var versionText = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var remoteVersion))
            return null;

        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version
                             ?? new Version(0, 0, 0);

        if (remoteVersion <= currentVersion)
            return null;

        string? downloadUrl = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameNode)
                    ? nameNode.GetString()
                    : null;

                if (name?.Equals("SpotifyDeck-win-x64.zip",
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
            return null;

        var releasePage = root.TryGetProperty("html_url", out var html)
            ? html.GetString() ?? ""
            : "";

        return new UpdateInfo(remoteVersion, downloadUrl, releasePage);
    }

    public async Task<bool> DownloadAndApplyAsync(UpdateInfo update)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "SpotifyDeck",
            "update-" + Guid.NewGuid().ToString("N"));

        var zipPath = Path.Combine(tempRoot, "update.zip");
        var extracted = Path.Combine(tempRoot, "files");

        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extracted);

        try
        {
            using var response = await _http.GetAsync(
                update.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                return false;

            await using (var file = File.Create(zipPath))
                await response.Content.CopyToAsync(file);

            ZipFile.ExtractToDirectory(zipPath, extracted, overwriteFiles: true);

            var packagedExe = Path.Combine(extracted, "SpotifyDeck.exe");
            if (!File.Exists(packagedExe))
                return false;

            var currentDirectory = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            var currentExe = Environment.ProcessPath
                             ?? Path.Combine(currentDirectory, "SpotifyDeck.exe");

            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpotifyDeck",
                "logs");
            Directory.CreateDirectory(logDirectory);

            var updateLog = Path.Combine(logDirectory, "update.log");
            var updater = Path.Combine(tempRoot, "apply-update.cmd");
            var currentPid = Environment.ProcessId;

            var lines = new[]
            {
                "@echo off",
                "setlocal EnableExtensions",
                $"set \"LOG={updateLog}\"",
                $"echo [%date% %time%] Starting update to {update.Version}>>\"%LOG%\"",
                ":wait_for_app",
                $"tasklist /FI \"PID eq {currentPid}\" 2>NUL | find \"{currentPid}\" >NUL",
                "if not errorlevel 1 (",
                "  timeout /t 1 /nobreak >nul",
                "  goto wait_for_app",
                ")",
                $"robocopy \"{extracted}\" \"{currentDirectory}\" /E /R:5 /W:1 /NFL /NDL /NJH /NJS /NP >>\"%LOG%\"",
                "set COPYCODE=%ERRORLEVEL%",
                "if %COPYCODE% GEQ 8 goto update_failed",
                $"echo [%date% %time%] Update installed. Restarting.>>\"%LOG%\"",
                $"start \"\" \"{currentExe}\"",
                $"rmdir /S /Q \"{tempRoot}\"",
                "exit /b 0",
                ":update_failed",
                "echo [%date% %time%] Update failed with robocopy code %COPYCODE%.>>\"%LOG%\"",
                $"start \"\" \"{currentExe}\"",
                "exit /b %COPYCODE%"
            };

            await File.WriteAllLinesAsync(updater, lines);

            Process.Start(new ProcessStartInfo
            {
                FileName = updater,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = tempRoot
            });

            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed record UpdateInfo(
    Version Version,
    string DownloadUrl,
    string ReleasePage);
