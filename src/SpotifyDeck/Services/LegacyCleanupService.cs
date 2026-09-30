using System.IO;

namespace SpotifyDeck.Services;

public static class LegacyCleanupService
{
    private const string ExtensionName = "spotifydeck-bridge.js";

    public static async Task CleanupAsync()
    {
        try
        {
            CleanupInstalledBridge();
            CleanupSpicetifyExtension();
            CleanupSpicetifyConfig();
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("legacy-cleanup", ex);
        }
    }

    private static void CleanupInstalledBridge()
    {
        var bridgeDir = Path.Combine(AppContext.BaseDirectory, "bridge");
        var file = Path.Combine(bridgeDir, ExtensionName);

        if (File.Exists(file))
            File.Delete(file);

        if (Directory.Exists(bridgeDir) &&
            !Directory.EnumerateFileSystemEntries(bridgeDir).Any())
            Directory.Delete(bridgeDir);
    }

    private static void CleanupSpicetifyExtension()
    {
        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "spicetify", "Extensions", ExtensionName);

        if (File.Exists(file))
            File.Delete(file);
    }

    private static void CleanupSpicetifyConfig()
    {
        var config = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "spicetify", "config-xpui.ini");

        if (!File.Exists(config))
            return;

        var lines = File.ReadAllLines(config);
        var changed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("extensions", StringComparison.OrdinalIgnoreCase))
                continue;

            var equals = lines[i].IndexOf('=');
            if (equals < 0)
                continue;

            var prefix = lines[i][..(equals + 1)];
            var values = lines[i][(equals + 1)..]
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !x.Equals(ExtensionName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var replacement = prefix + " " + string.Join("|", values);
            if (!string.Equals(lines[i], replacement, StringComparison.Ordinal))
            {
                lines[i] = replacement;
                changed = true;
            }
        }

        if (changed)
            File.WriteAllLines(config, lines);
    }
}
