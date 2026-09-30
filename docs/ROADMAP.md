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

## Discord / Hade
Goal: add a selected Spotify track to the music queue of a Discord voice channel without leaving the overlay.

Proposed UX:
- Enter: play locally in Spotify.
- Shift + Enter: send to Hade / Discord queue.
- First use: Connect Hade.
- Recommended pairing flow: Hade generates a short one-time link code, SpotifyDeck exchanges it with Hade, then stores a local pairing token.
- SpotifyDeck sends the selected Spotify URI to Hade.
- Hade resolves the active guild / voice channel and queues the track.

Bot-side work still requires an authenticated Hade endpoint (or equivalent IPC/API) before this can be wired end-to-end.