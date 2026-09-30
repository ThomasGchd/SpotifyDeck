using System.IO;
using System.Text.Json;

namespace SpotifyDeck.Services;

public sealed class AppSettingsService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyDeck", "settings.json");

    public SpotifyDeckSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            return JsonSerializer.Deserialize<SpotifyDeckSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch { return new(); }
    }

    public void Save(SpotifyDeckSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed record SpotifyDeckSettings
{
    public string Shortcut { get; init; } = "Ctrl+Shift+M";
}
