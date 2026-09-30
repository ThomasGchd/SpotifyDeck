namespace SpotifyDeck.Models;

public sealed record SpotifyItem(
    string Id,
    string Name,
    string Subtitle,
    string Uri,
    SpotifyItemType Type);

public enum SpotifyItemType
{
    Track,
    Playlist
}
