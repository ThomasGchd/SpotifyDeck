using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SpotifyDeck.Models;

namespace SpotifyDeck.Services;

public sealed class SpotifyApiService
{
    private readonly SpotifyAuthService _auth;
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://api.spotify.com/v1/") };

    public SpotifyApiService(SpotifyAuthService auth) => _auth = auth;

    public async Task<IReadOnlyList<SpotifyItem>> SearchAsync(string query)
    {
        var root = await GetJsonAsync($"search?q={Uri.EscapeDataString(query)}&type=track,playlist&limit=8");
        if (root is null) return [];

        var items = new List<SpotifyItem>();
        if (root.Value.TryGetProperty("tracks", out var tracks))
            foreach (var x in tracks.GetProperty("items").EnumerateArray())
                items.Add(MapTrack(x));

        if (root.Value.TryGetProperty("playlists", out var playlists))
            foreach (var x in playlists.GetProperty("items").EnumerateArray())
                if (x.ValueKind == JsonValueKind.Object)
                    items.Add(MapPlaylist(x));

        return items;
    }

    public async Task<IReadOnlyList<SpotifyItem>> GetPlaylistsAsync()
    {
        var root = await GetJsonAsync("me/playlists?limit=20");
        if (root is null || !root.Value.TryGetProperty("items", out var items)) return [];

        return items.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(MapPlaylist)
            .ToList();
    }

    public async Task<bool> PlayAsync(SpotifyItem item)
    {
        var token = await _auth.GetAccessTokenAsync();
        if (token is null) return false;

        using var request = new HttpRequestMessage(HttpMethod.Put, "me/player/play");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var body = item.Type == "track"
            ? JsonSerializer.Serialize(new { uris = new[] { item.Uri } })
            : JsonSerializer.Serialize(new { context_uri = item.Uri });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private async Task<JsonElement?> GetJsonAsync(string path)
    {
        var token = await _auth.GetAccessTokenAsync();
        if (token is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static SpotifyItem MapTrack(JsonElement x)
    {
        var artists = x.TryGetProperty("artists", out var artistNodes)
            ? string.Join(", ", artistNodes.EnumerateArray().Select(a => a.GetProperty("name").GetString()))
            : "";
        string? image = null;
        if (x.TryGetProperty("album", out var album) &&
            album.TryGetProperty("images", out var images) &&
            images.GetArrayLength() > 0)
            image = images[0].GetProperty("url").GetString();

        return new(
            x.GetProperty("id").GetString() ?? "",
            x.GetProperty("name").GetString() ?? "",
            artists,
            x.GetProperty("uri").GetString() ?? "",
            "track",
            image);
    }

    private static SpotifyItem MapPlaylist(JsonElement x)
    {
        string? image = null;
        if (x.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0)
            image = images[0].GetProperty("url").GetString();

        return new(
            x.GetProperty("id").GetString() ?? "",
            x.GetProperty("name").GetString() ?? "",
            "Playlist",
            x.GetProperty("uri").GetString() ?? "",
            "playlist",
            image);
    }
}
