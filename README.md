# SpotifyDeck

SpotifyDeck is a lightweight Windows overlay for controlling Spotify without bringing the Spotify window to the foreground.

## Current features

- Global configurable shortcut to show/hide the overlay.
- Search tracks and playlists.
- Recent tracks and personal playlists as quick access.
- Play a track or playlist without foregrounding Spotify.
- Previous / play-pause / next controls.
- Tray mode and optional Windows autostart.
- Single-instance behavior: launching SpotifyDeck again opens the existing overlay.
- Per-user Windows installer (no admin required).
- In-app GitHub Release updater with automatic restart.
- Spotify Web API OAuth PKCE path prepared for production builds.
- Local Spicetify bridge retained as the current development fallback.

## Build

Requirements:

- Windows
- .NET 8 SDK

```powershell
dotnet build src/SpotifyDeck/SpotifyDeck.csproj -c Release
```

The GitHub Actions build also publishes:

- `SpotifyDeck-portable-win-x64`
- `SpotifyDeck-installer-win-x64`

## Spotify connection

Production releases can bundle the public Spotify application client id through the repository variable `SPOTIFY_CLIENT_ID`. End users never enter a client secret.

The registered redirect URI is:

```text
http://127.0.0.1:43821/callback/
```

When the official OAuth configuration is absent, development builds can use the bundled Spicetify bridge.
