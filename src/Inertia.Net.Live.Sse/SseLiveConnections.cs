using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace Inertia.Net.Live.Sse;

/// <summary>The open event streams of this app instance, and the broadcaster that writes to them.</summary>
internal sealed class SseLiveConnections : IInertiaLiveBroadcaster
{
    // shortcut: one scan of this instance's connections per publish, no backplane; add a channel index (or a Redis backplane) when
    // apps run many connections or several instances.
    private readonly ConcurrentDictionary<string, SseConnection> _connections = new(StringComparer.Ordinal);

    public int Count => _connections.Count;

    public void Add(SseConnection connection) => _connections[connection.Id] = connection;

    public void Remove(SseConnection connection) => _connections.TryRemove(new(connection.Id, connection));

    public ValueTask PublishAsync(LiveMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var key = message.Channel.Key;
        string? data = null;
        foreach (var connection in _connections.Values.Where(c => c.Channels.Contains(key) && c.Id != message.ExceptSocketId))
        {
            data ??= Event(key, message.Payload);
            // A connection that cannot keep up is closed: its client reconnects and reloads its live props, so nothing is lost.
            if (!connection.Queue.Writer.TryWrite(data)) connection.Queue.Writer.TryComplete();
        }

        return default;
    }

    private static string Event(string channel, JsonElement payload)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("channel", channel);
            writer.WritePropertyName("payload");
            payload.WriteTo(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}

internal sealed class SseConnection(string id, HashSet<string> channels, int capacity)
{
    public string Id { get; } = id;

    public HashSet<string> Channels { get; } = channels;

    public Channel<string> Queue { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait, // TryWrite fails when full instead of dropping an event silently
    });
}

/// <summary>The stream request as the <see cref="Live.SignalR.ILiveChannelAuthorizer"/> sees a hub caller.</summary>
internal sealed class SseCallerContext(HttpContext http, string connectionId) : HubCallerContext
{
    public override string ConnectionId => connectionId;

    public override string? UserIdentifier => http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public override ClaimsPrincipal? User => http.User;

    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    public override IFeatureCollection Features => http.Features;

    public override CancellationToken ConnectionAborted => http.RequestAborted;

    public override void Abort() => http.Abort();
}
