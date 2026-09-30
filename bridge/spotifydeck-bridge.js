(function spotifyDeckBridge() {
    if (!window.Spicetify?.CosmosAsync || !window.Spicetify?.Player) {
        setTimeout(spotifyDeckBridge, 300);
        return;
    }

    let socket;
    let reconnectTimer;

    const send = (message) => {
        if (socket?.readyState === WebSocket.OPEN) {
            socket.send(JSON.stringify(message));
        }
    };

    const resultItem = (item, type) => ({
        id: item.id ?? "",
        name: item.name ?? "",
        subtitle: type === "track"
            ? (item.artists ?? []).map(a => a.name).join(", ")
            : "Playlist",
        uri: item.uri ?? "",
        type,
        imageUrl: type === "track"
            ? item.album?.images?.[0]?.url ?? null
            : item.images?.[0]?.url ?? null
    });

    async function handle(message) {
        const { id, type, payload } = message;

        try {
            if (type === "search") {
                const q = encodeURIComponent(payload?.query ?? "");
                const response = await Spicetify.CosmosAsync.get(
                    `https://api.spotify.com/v1/search?q=${q}&type=track,playlist&limit=8`
                );

                const tracks = (response?.tracks?.items ?? []).map(x => resultItem(x, "track"));
                const playlists = (response?.playlists?.items ?? [])
                    .filter(Boolean)
                    .map(x => resultItem(x, "playlist"));

                send({ id, data: [...tracks, ...playlists] });
                return;
            }

            if (type === "playlists") {
                const response = await Spicetify.CosmosAsync.get(
                    "https://api.spotify.com/v1/me/playlists?limit=16"
                );
                const playlists = (response?.items ?? [])
                    .filter(Boolean)
                    .map(x => resultItem(x, "playlist"));

                send({ id, data: playlists });
                return;
            }

            if (type === "recent") {
                const response = await Spicetify.CosmosAsync.get(
                    "https://api.spotify.com/v1/me/player/recently-played?limit=8"
                );
                const seen = new Set();
                const recent = [];

                for (const entry of response?.items ?? []) {
                    const track = entry?.track;
                    if (!track?.id || seen.has(track.id)) continue;
                    seen.add(track.id);
                    recent.push(resultItem(track, "track"));
                }

                send({ id, data: recent });
                return;
            }

            if (type === "play") {
                const uri = payload?.uri ?? "";
                const itemType = payload?.type ?? "";

                if (itemType === "track") {
                    await Spicetify.Player.playUri(uri);
                } else {
                    await Spicetify.CosmosAsync.put(
                        "https://api.spotify.com/v1/me/player/play",
                        { context_uri: uri }
                    );
                }

                send({ id, data: { ok: true } });
                return;
            }

            send({ id, data: { ok: false, error: "unknown_message" } });
        } catch (error) {
            send({
                id,
                data: {
                    ok: false,
                    error: String(error?.message ?? error ?? "unknown_error")
                }
            });
        }
    }

    function connect() {
        clearTimeout(reconnectTimer);

        try {
            socket = new WebSocket("ws://127.0.0.1:5544/spotifydeck");

            socket.addEventListener("open", () => {
                console.log("[SpotifyDeck] Local bridge connected");
            });

            socket.addEventListener("message", event => {
                try {
                    handle(JSON.parse(event.data));
                } catch (error) {
                    console.error("[SpotifyDeck] Invalid message", error);
                }
            });

            socket.addEventListener("close", () => {
                reconnectTimer = setTimeout(connect, 1200);
            });

            socket.addEventListener("error", () => {
                try { socket.close(); } catch {}
            });
        } catch {
            reconnectTimer = setTimeout(connect, 1200);
        }
    }

    connect();
})();
