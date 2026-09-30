using System.IO;

namespace SpotifyDeck.Services;

public static class AppLog
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyDeck",
        "logs");

    public static Task WriteAsync(string area, Exception ex)
        => WriteAsync(area, ex.ToString());

    public static async Task WriteAsync(string area, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, "spotifydeck.log");
            await File.AppendAllTextAsync(
                path,
                $"[{DateTime.Now:O}] [{area}] {message}\r\n\r\n");
        }
        catch
        {
            // Logging must never stop SpotifyDeck from running.
        }
    }
}
