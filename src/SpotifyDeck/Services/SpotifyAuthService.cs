using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace SpotifyDeck.Services;

public sealed class SpotifyAuthService
{
    private const string RedirectUri = "http://127.0.0.1:43821/callback/";
    public string ClientId { get; } = ResolveClientId();
    private static readonly string[] Scopes =
    [
        "playlist-read-private",
        "playlist-read-collaborative",
        "user-read-playback-state",
        "user-modify-playback-state",
        "user-read-recently-played"
    ];

    private readonly HttpClient _http = new();
    private readonly SpotifyTokenStore _store = new();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
    public bool HasSession => _store.Load() is not null;

    public async Task<string?> GetAccessTokenAsync()
    {
        var token = _store.Load();
        if (token is null) return null;

        if (token.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            return token.AccessToken;

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            return null;

        using var response = await _http.PostAsync(
            "https://accounts.spotify.com/api/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = token.RefreshToken,
                ["client_id"] = ClientId
            }));

        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var refreshed = token with
        {
            AccessToken = root.GetProperty("access_token").GetString() ?? "",
            RefreshToken = root.TryGetProperty("refresh_token", out var refresh)
                ? refresh.GetString() ?? token.RefreshToken
                : token.RefreshToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32())
        };

        _store.Save(refreshed);
        return refreshed.AccessToken;
    }

    public async Task<SpotifyAuthResult> ConnectAsync()
    {
        if (!IsConfigured)
            return new(false, "SpotifyDeck attend encore son Client ID Spotify officiel.");

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));

        using var listener = new TcpListener(IPAddress.Loopback, 43821);
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            return new(false, "Le port de connexion Spotify est déjà utilisé. Ferme l'autre instance puis réessaie.");
        }

        var query = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge,
            ["state"] = state,
            ["scope"] = string.Join(' ', Scopes)
        };

        var authUrl = "https://accounts.spotify.com/authorize?" +
            string.Join("&", query.Select(x =>
                $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        TcpClient client;
        try
        {
            client = await listener.AcceptTcpClientAsync(timeout.Token);
        }
        catch
        {
            return new(false, "Connexion Spotify annulée ou expirée.");
        }
        finally
        {
            listener.Stop();
        }

        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true))
        {
            var requestLine = await reader.ReadLineAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(requestLine))
                return new(false, "Réponse Spotify invalide.");

            var parts = requestLine.Split(' ');
            if (parts.Length < 2)
                return new(false, "Réponse Spotify invalide.");

            var callbackUri = new Uri($"http://127.0.0.1{parts[1]}");
            var callbackQuery = QueryHelpers.ParseQuery(callbackUri.Query);

            var code = callbackQuery.TryGetValue("code", out var codeValue)
                ? codeValue.ToString()
                : null;
            var returnedState = callbackQuery.TryGetValue("state", out var stateValue)
                ? stateValue.ToString()
                : null;
            var error = callbackQuery.TryGetValue("error", out var errorValue)
                ? errorValue.ToString()
                : null;

            const string page = "<html><body style='font-family:Segoe UI;background:#111;color:#fff;padding:40px'><h2>SpotifyDeck</h2><p>Connexion terminee. Tu peux fermer cet onglet et revenir a SpotifyDeck.</p></body></html>";
            var body = Encoding.UTF8.GetBytes(page);
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, timeout.Token);
            await stream.WriteAsync(body, timeout.Token);
            await stream.FlushAsync(timeout.Token);

            if (!string.IsNullOrWhiteSpace(error))
                return new(false, $"Spotify a refusé la connexion : {error}.");
            if (returnedState != state || string.IsNullOrWhiteSpace(code))
                return new(false, "Réponse Spotify invalide.");

            return await ExchangeCodeAsync(code, verifier);
        }

    }

    private async Task<SpotifyAuthResult> ExchangeCodeAsync(string code, string verifier)
    {
        using var response = await _http.PostAsync(
            "https://accounts.spotify.com/api/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = ClientId,
                ["code_verifier"] = verifier
            }));

        if (!response.IsSuccessStatusCode)
            return new(false, $"Spotify n'a pas délivré de jeton ({(int)response.StatusCode}).");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        _store.Save(new SpotifyToken(
            root.GetProperty("access_token").GetString() ?? "",
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() ?? "" : "",
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32())));

        return new(true, "Spotify connecté.");
    }

    public void Disconnect() => _store.Clear();

    private static string ResolveClientId()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("SPOTIFYDECK_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment.Trim();

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "spotify-client-id.txt");
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();
        }
        catch { }

        return string.Empty;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record SpotifyAuthResult(bool Success, string Message);
