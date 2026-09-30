(function spotifyDeckBridge() {
    if (!window.Spicetify?.Player) {
        setTimeout(spotifyDeckBridge, 300);
        return;
    }

    let socket;
    let reconnectTimer;
    const RECENT_KEY = "spotifydeck:recent-v2";

    const send = (message) => {
        if (socket?.readyState === WebSocket.OPEN) {
            socket.send(JSON.stringify(message));
        }
    };

    const imageUrl = (value) => {
        if (!value) return null;
        if (/^https?:\/\//i.test(value)) return value;

        const hash = String(value).substring(String(value).lastIndexOf(":") + 1);
        return hash && !String(value).includes("localfile")
            ? `https://i.scdn.co/image/${hash}`
            : null;
    };

    const uriId = (uri) => String(uri ?? "").split(":").pop() ?? "";

    const playerItem = () => {
        const data = Spicetify.Player?.data;
        const item = data?.item;
        const meta = item?.metadata ?? {};

        if (!item?.uri) return null;

        let artist = meta.artist_name ?? "";
        let index = 1;
        while (meta[`artist_name:${index}`]) {
            artist += artist ? `, ${meta[`artist_name:${index}`]}` : meta[`artist_name:${index}`];
            index++;
        }

        return {
            id: uriId(item.uri),
            name: meta.title ?? item.name ?? "",
            subtitle: artist || meta.album_title || "",
            uri: item.uri,
            type: "track",
            imageUrl: imageUrl(meta.image_xlarge_url ?? meta.image_url ?? null)
        };
    };

    const apiResultItem = (item, type) => ({
        id: item?.id ?? uriId(item?.uri),
        name: item?.name ?? "",
        subtitle: type === "track"
            ? (item?.artists ?? []).map(a => a.name).join(", ")
            : "Playlist",
        uri: item?.uri ?? "",
        type,
        imageUrl: type === "track"
            ? item?.album?.images?.[0]?.url ?? null
            : item?.images?.[0]?.url ?? null
    });

    const nativePlaylistItem = (item) => ({
        id: uriId(item?.uri),
        name: item?.name ?? item?.title ?? "Playlist",
        subtitle: "Playlist",
        uri: item?.uri ?? "",
        type: "playlist",
        imageUrl: imageUrl(item?.imageUrl ?? item?.image ?? item?.images?.[0]?.url ?? null)
    });

    const loadLocalRecent = () => {
        try {
            const parsed = JSON.parse(localStorage.getItem(RECENT_KEY) ?? "[]");
            return Array.isArray(parsed) ? parsed : [];
        } catch {
            return [];
        }
    };

    const rememberCurrentTrack = () => {
        const current = playerItem();
        if (!current?.uri) return;

        const next = [current, ...loadLocalRecent().filter(x => x?.uri !== current.uri)].slice(0, 8);
        try {
            localStorage.setItem(RECENT_KEY, JSON.stringify(next));
        } catch {}
    };

    const flattenRootlist = (items, output = []) => {
        for (const item of items ?? []) {
            if (!item) continue;

            if (item.type === "playlist" || String(item.uri ?? "").startsWith("spotify:playlist:")) {
                output.push(nativePlaylistItem(item));
            }

            if (Array.isArray(item.items))
                flattenRootlist(item.items, output);
        }

        return output;
    };

    async function nativePlaylists() {
        const api = Spicetify.Platform?.RootlistAPI;
        if (!api?.getContents)
            throw new Error("RootlistAPI unavailable");

        const response = await api.getContents();
        return flattenRootlist(response?.items ?? []).slice(0, 24);
    }

    async function webApiSearch(query) {
        if (!Spicetify.CosmosAsync?.get)
            throw new Error("CosmosAsync unavailable");

        const q = encodeURIComponent(query);
        const response = await Spicetify.CosmosAsync.get(
            `https://api.spotify.com/v1/search?q=${q}&type=track,playlist&limit=8`
        );

        const tracks = (response?.tracks?.items ?? []).map(x => apiResultItem(x, "track"));
        const playlists = (response?.playlists?.items ?? [])
            .filter(Boolean)
            .map(x => apiResultItem(x, "playlist"));

        return [...tracks, ...playlists];
    }

    const collectSpotifyEntities = (value, output = [], seenObjects = new Set()) => {
        if (!value || output.length >= 20) return output;

        if (typeof value === "object") {
            if (seenObjects.has(value)) return output;
            seenObjects.add(value);
        }

        if (Array.isArray(value)) {
            for (const item of value)
                collectSpotifyEntities(item, output, seenObjects);
            return output;
        }

        if (typeof value !== "object")
            return output;

        const uri = value.uri ?? value?.track?.uri ?? value?.playlist?.uri;
        const source = value.track ?? value.playlist ?? value;

        if (typeof uri === "string" &&
            (uri.startsWith("spotify:track:") || uri.startsWith("spotify:playlist:"))) {
            const type = uri.startsWith("spotify:track:") ? "track" : "playlist";
            const artists = source.artists ?? source?.albumOfTrack?.artists?.items ?? [];
            const artistText = Array.isArray(artists)
                ? artists.map(a => a?.name ?? a?.profile?.name).filter(Boolean).join(", ")
                : "";

            const cover =
                source?.album?.images?.[0]?.url ??
                source?.images?.[0]?.url ??
                source?.coverArt?.sources?.[0]?.url ??
                source?.imageUrl ??
                null;

            output.push({
                id: uriId(uri),
                name: source.name ?? source.title ?? "",
                subtitle: type === "track" ? artistText : "Playlist",
                uri,
                type,
                imageUrl: imageUrl(cover)
            });
        }

        for (const child of Object.values(value))
            collectSpotifyEntities(child, output, seenObjects);

        return output;
    };

    async function nativeSearch(query) {
        const platform = Spicetify.Platform ?? {};
        const apiEntries = Object.entries(platform)
            .filter(([name, api]) => /search/i.test(name) && api && typeof api === "object");

        for (const [, api] of apiEntries) {
            for (const methodName of ["search", "query", "getResults", "getSearchResults"]) {
                const fn = api?.[methodName];
                if (typeof fn !== "function") continue;

                const attempts = [
                    () => fn.call(api, query),
                    () => fn.call(api, query, { limit: 12 }),
                    () => fn.call(api, { query, limit: 12 })
                ];

                for (const attempt of attempts) {
                    try {
                        const response = await attempt();
                        const entities = collectSpotifyEntities(response);
                        if (entities.length > 0) {
                            const seen = new Set();
                            return entities.filter(x => {
                                if (!x.uri || seen.has(x.uri)) return false;
                                seen.add(x.uri);
                                return true;
                            }).slice(0, 12);
                        }
                    } catch {}
                }
            }
        }

        return [];
    }

    async function searchWithNativeFallback(query) {
        // Prefer Spotify's own native search service when the current desktop
        // client exposes one through Platform.
        try {
            const result = await nativeSearch(query);
            if (result.length > 0)
                return result;
        } catch (error) {
            console.warn("[SpotifyDeck] Native search unavailable", error);
        }

        // Older/current desktop builds may still allow the authenticated Web
        // API proxy. Keep it as a secondary path rather than a dependency.
        try {
            const result = await webApiSearch(query);
            if (result.length > 0)
                return result;
        } catch (error) {
            console.warn("[SpotifyDeck] Web API search unavailable", error);
        }

        // Last-resort local fallback: playlists + current/recent tracks.
        const q = query.trim().toLocaleLowerCase();
        const candidates = [];

        try {
            candidates.push(...await nativePlaylists());
        } catch {}

        const current = playerItem();
        if (current) candidates.push(current);
        candidates.push(...loadLocalRecent());

        const seen = new Set();
        return candidates
            .filter(item => {
                const haystack = `${item?.name ?? ""} ${item?.subtitle ?? ""}`.toLocaleLowerCase();
                return haystack.includes(q);
            })
            .filter(item => {
                const key = item?.uri ?? item?.id;
                if (!key || seen.has(key)) return false;
                seen.add(key);
                return true;
            })
            .slice(0, 12);
    }

    async function handle(message) {
        const { id, type, payload } = message;

        try {
            if (type === "search") {
                const query = String(payload?.query ?? "").trim();
                const results = query ? await searchWithNativeFallback(query) : [];
                send({ id, data: results });
                return;
            }

            if (type === "playlists") {
                let playlists = [];

                try {
                    playlists = await nativePlaylists();
                } catch (nativeError) {
                    console.warn("[SpotifyDeck] Native playlists unavailable", nativeError);

                    if (Spicetify.CosmosAsync?.get) {
                        const response = await Spicetify.CosmosAsync.get(
                            "https://api.spotify.com/v1/me/playlists?limit=24"
                        );
                        playlists = (response?.items ?? [])
                            .filter(Boolean)
                            .map(x => apiResultItem(x, "playlist"));
                    }
                }

                send({ id, data: playlists });
                return;
            }

            if (type === "recent") {
                let recent = loadLocalRecent();

                if (recent.length === 0 && Spicetify.CosmosAsync?.get) {
                    try {
                        const response = await Spicetify.CosmosAsync.get(
                            "https://api.spotify.com/v1/me/player/recently-played?limit=8"
                        );

                        const seen = new Set();
                        recent = [];
                        for (const entry of response?.items ?? []) {
                            const track = entry?.track;
                            if (!track?.id || seen.has(track.id)) continue;
                            seen.add(track.id);
                            recent.push(apiResultItem(track, "track"));
                        }
                    } catch {}
                }

                send({ id, data: recent });
                return;
            }

            if (type === "play") {
                const uri = payload?.uri ?? "";
                if (!uri)
                    throw new Error("missing uri");

                await Spicetify.Player.playUri(uri);
                send({ id, data: { ok: true } });
                return;
            }

            if (type === "state") {
                const current = playerItem();
                const state = Spicetify.Player?.data;

                if (!current) {
                    send({ id, data: { ok: true, empty: true } });
                    return;
                }

                send({
                    id,
                    data: {
                        ok: true,
                        name: current.name,
                        artist: current.subtitle,
                        isPlaying: !Boolean(state?.isPaused),
                        imageUrl: current.imageUrl
                    }
                });
                return;
            }

            if (type === "toggle") {
                Spicetify.Player.togglePlay();
                send({ id, data: { ok: true } });
                return;
            }

            if (type === "next") {
                Spicetify.Player.next();
                send({ id, data: { ok: true } });
                return;
            }

            if (type === "previous") {
                Spicetify.Player.back();
                send({ id, data: { ok: true } });
                return;
            }

            if (type === "diagnostics") {
                send({
                    id,
                    data: {
                        ok: true,
                        spicetifyVersion: Spicetify.Config?.version ?? null,
                        spotifyVersion: Spicetify.Platform?.PlatformData?.client_version ?? null,
                        hasCosmos: Boolean(Spicetify.CosmosAsync),
                        hasPlayer: Boolean(Spicetify.Player),
                        hasRootlistApi: Boolean(Spicetify.Platform?.RootlistAPI?.getContents),
                        platformApis: Object.keys(Spicetify.Platform ?? {}).sort()
                    }
                });
                return;
            }

            send({ id, data: { ok: false, error: "unknown_message" } });
        } catch (error) {
            console.error(`[SpotifyDeck] ${type} failed`, error);
            send({
                id,
                data: {
                    ok: false,
                    error: String(error?.message ?? error ?? "unknown_error")
                }
            });
        }
    }

    Spicetify.Player.addEventListener?.("songchange", rememberCurrentTrack);
    rememberCurrentTrack();

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
