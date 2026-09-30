using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace SpotifyDeck.Services;

public sealed class HadeService
{
    private readonly HttpClient _http = new();
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyDeck", "hade.json");

    public bool IsPaired => Load() is { BaseUrl.Length: > 0, Token.Length: > 0 };

    public async Task<bool> PairAsync(string baseUrl, string oneTimeCode)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(oneTimeCode))
            return false;

        baseUrl = baseUrl.Trim().TrimEnd('/');

        try
        {
            using var response = await _http.PostAsJsonAsync(
                baseUrl + "/spotifydeck/pair",
                new { code = oneTimeCode.Trim() });

            if (!response.IsSuccessStatusCode)
                return false;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("token", out var tokenNode))
                return false;

            var token = tokenNode.GetString();
            if (string.IsNullOrWhiteSpace(token))
                return false;

            Save(new HadeSettings(baseUrl, token));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<QueueResult> QueueAsync(string spotifyUri)
    {
        var settings = Load();
        if (settings is null)
            return new QueueResult(false, "Hade n'est pas connecté.");

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                settings.BaseUrl + "/spotifydeck/queue");

            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.Token);

            request.Content = JsonContent.Create(new { uri = spotifyUri });

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new QueueResult(false, "Hade a refusé l'ajout à la file.");

            return new QueueResult(true, "Ajouté à la file Discord.");
        }
        catch
        {
            return new QueueResult(false, "Hade est inaccessible.");
        }
    }

    public void Disconnect()
    {
        try
        {
            if (File.Exists(_settingsPath))
                File.Delete(_settingsPath);
        }
        catch { }
    }

    private HadeSettings? Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return null;

            return JsonSerializer.Deserialize<HadeSettings>(
                File.ReadAllText(_settingsPath));
        }
        catch
        {
            return null;
        }
    }

    private void Save(HadeSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            _settingsPath,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record HadeSettings(string BaseUrl, string Token);
}

public sealed record QueueResult(bool Success, string Message);
