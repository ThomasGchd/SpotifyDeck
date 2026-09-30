# SpotifyDeck

SpotifyDeck is a lightweight Windows overlay for controlling Spotify without bringing the Spotify window to the foreground.

## Current features

- Global configurable shortcut to show/hide the overlay.
- Search tracks and playlists through the official Spotify Web API.
- Recent tracks and personal playlists as quick access.
- Current-track display.
- Play a track or playlist on an available Spotify device.
- Previous / play-pause / next controls.
- Tray mode and optional Windows autostart.
- Single-instance behavior: launching SpotifyDeck again opens the existing overlay.
- Per-user Windows installer (no admin required).
- In-app GitHub Release updater with automatic restart.
- Browser-based OAuth Authorization Code with PKCE.
- No Spicetify dependency.

## Spotify setup

SpotifyDeck is a public desktop OAuth client: no client secret is embedded or requested.

For personal/development builds, create an application in Spotify for Developers and register this redirect URI:

```text
http://127.0.0.1:43821/callback/
```

On first connection SpotifyDeck opens a setup dialog where the public Client ID can be pasted. It is saved locally in the user's SpotifyDeck settings.

A release build may instead bundle the public Client ID through the GitHub repository variable `SPOTIFY_CLIENT_ID`, in which case end users do not see the setup dialog.

## Build

Requirements:

- Windows
- .NET 8 SDK

```powershell
dotnet build src/SpotifyDeck/SpotifyDeck.csproj -c Release
```

GitHub Actions publishes:

- `SpotifyDeck-portable-win-x64`
- `SpotifyDeck-installer-win-x64`
