namespace SpotifyDeck.Models;

public sealed record SpotifyItem(
    string Id,
    string Name,
    string Subtitle,
    string Uri,
    string Type,
    string? ImageUrl = null);
