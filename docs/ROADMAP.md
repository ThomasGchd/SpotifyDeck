# SpotifyDeck roadmap

## Core UX
- Global shortcut: Ctrl + Alt + M.
- Overlay designed to sit over borderless/fullscreen-windowed games.
- Search is focused immediately when the overlay opens.
- Search tracks and playlists, then play without bringing Spotify to the foreground.
- Spotify may be launched in the background when needed.
- Escape closes the overlay.

## Spotify connection
Production UX:
1. Click Connect Spotify.
2. SpotifyDeck opens the user's default browser.
3. The user signs in to Spotify and approves access.
4. The browser returns to SpotifyDeck.
5. Tokens are stored locally and refreshed automatically.

No end user enters developer credentials.

During early development, the local Spicetify bridge can remain available as a fallback/test transport.

## Updates
- Check for updates inside SpotifyDeck.
- Releases are hosted on GitHub Releases.
- SpotifyDeck downloads the Windows package, replaces its files after exit, and restarts itself.
- Tagged GitHub releases automatically build a self-contained Windows package.

## Discord / Hade (later)
Deferred from V1. SpotifyDeck will only integrate Hade if the bot exposes a supported authenticated API/integration. No Discord user automation or simulated slash commands.
