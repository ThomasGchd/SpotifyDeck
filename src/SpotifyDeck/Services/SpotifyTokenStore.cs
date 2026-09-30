using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpotifyDeck.Services;

public sealed class SpotifyTokenStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyDeck", "spotify-session.bin");

    public SpotifyToken? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var protectedBytes = File.ReadAllBytes(_path);
            var json = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
            return JsonSerializer.Deserialize<SpotifyToken>(json);
        }
        catch { return null; }
    }

    public void Save(SpotifyToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(token);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }
}

public sealed record SpotifyToken(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);
