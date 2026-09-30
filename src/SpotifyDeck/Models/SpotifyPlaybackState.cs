namespace SpotifyDeck.Models;

public sealed record SpotifyPlaybackState(
    string Name,
    string Artist,
    bool IsPlaying,
    string? ImageUrl = null);
