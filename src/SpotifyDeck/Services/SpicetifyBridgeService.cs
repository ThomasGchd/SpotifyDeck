using System.IO;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SpotifyDeck.Models;

namespace SpotifyDeck.Services;

public sealed class SpicetifyBridgeService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private WebApplication? _app;
    private WebSocket? _socket;

    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public event Action<bool>? ConnectionChanged;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:5544");

        _app = builder.Build();
        _app.UseWebSockets();

        _app.Map("/spotifydeck", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            _socket = await context.WebSockets.AcceptWebSocketAsync();
            ConnectionChanged?.Invoke(true);

            try
            {
                await ReceiveLoopAsync(_socket);
            }
            finally
            {
                _socket?.Dispose();
                _socket = null;
                ConnectionChanged?.Invoke(false);
            }
        });

        await _app.StartAsync();
    }

    public async Task<IReadOnlyList<SpotifyItem>> SearchAsync(string query)
    {
        var data = await SendAsync("search", new { query });
        return DeserializeItems(data);
    }

    public async Task<IReadOnlyList<SpotifyItem>> GetPlaylistsAsync()
    {
        var data = await SendAsync("playlists", new { });
        return DeserializeItems(data);
    }

    public async Task<IReadOnlyList<SpotifyItem>> GetRecentAsync()
    {
        var data = await SendAsync("recent", new { });
        return DeserializeItems(data);
    }

    public async Task<bool> PlayAsync(SpotifyItem item)
    {
        var data = await SendAsync("play", new { uri = item.Uri, type = item.Type });
        return data is { ValueKind: JsonValueKind.Object } &&
               data.Value.TryGetProperty("ok", out var ok) &&
               ok.GetBoolean();
    }

    private async Task<JsonElement?> SendAsync(string type, object payload)
    {
        if (!IsConnected || _socket is null)
            return null;

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var json = JsonSerializer.Serialize(new { id, type, payload });
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var registration = timeout.Token.Register(() => tcs.TrySetCanceled(timeout.Token));

        try
        {
            return await tcs.Task;
        }
        catch
        {
            _pending.TryRemove(id, out _);
            return null;
        }
    }

    private async Task ReceiveLoopAsync(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];

        while (socket.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    return;
                }
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(ms.ToArray());
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id", out var idNode))
                continue;

            var id = idNode.GetString();
            if (id is null || !_pending.TryRemove(id, out var tcs))
                continue;

            if (root.TryGetProperty("data", out var data))
                tcs.TrySetResult(data.Clone());
            else
                tcs.TrySetResult(JsonDocument.Parse("{}").RootElement.Clone());
        }
    }

    private static IReadOnlyList<SpotifyItem> DeserializeItems(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Array })
            return [];

        var items = new List<SpotifyItem>();
        foreach (var node in data.Value.EnumerateArray())
        {
            items.Add(new SpotifyItem(
                node.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                node.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                node.TryGetProperty("subtitle", out var subtitle) ? subtitle.GetString() ?? "" : "",
                node.TryGetProperty("uri", out var uri) ? uri.GetString() ?? "" : "",
                node.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                node.TryGetProperty("imageUrl", out var image) ? image.GetString() : null
            ));
        }
        return items;
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is { State: WebSocketState.Open })
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None);

        if (_app is not null)
            await _app.StopAsync();
    }
}
