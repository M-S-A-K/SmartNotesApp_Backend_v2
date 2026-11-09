using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace SmartNotes;

public static class Realtime
{
    static readonly ConcurrentDictionary<int, ConcurrentDictionary<WebSocket, byte>> Clients = new();

    // Phone connects here: ws://localhost:5262/ws  (header X-User-Id or ?userId=1)
    public static async Task Handle(HttpContext ctx)
    {
        var raw = ctx.Request.Headers["X-User-Id"].FirstOrDefault() ?? ctx.Request.Query["userId"].FirstOrDefault();
        if (!ctx.WebSockets.IsWebSocketRequest || !int.TryParse(raw, out var userId))
        {
            ctx.Response.StatusCode = 400;
            return;
        }

        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var mine = Clients.GetOrAdd(userId, _ => new());
        mine[ws] = 0;
        var buffer = new byte[256];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buffer, CancellationToken.None);
                if (r.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch { }
        finally { mine.TryRemove(ws, out _); }
    }

    // Tell these users "something changed, refresh"
    public static async Task Notify(params int[] userIds)
    {
        foreach (var id in userIds.Distinct())
            if (Clients.TryGetValue(id, out var sockets))
                foreach (var ws in sockets.Keys)
                    if (ws.State == WebSocketState.Open)
                        try { await ws.SendAsync(Encoding.UTF8.GetBytes("update"), WebSocketMessageType.Text, true, CancellationToken.None); }
                        catch { }
    }
}