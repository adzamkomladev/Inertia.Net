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

/// <summary>
/// The open event streams of this app instance, indexed by channel, and the local transport that writes to them. With a backplane
/// every instance gets every message, so a publish looks up its channel instead of scanning every connection.
/// </summary>
internal sealed class SseLiveConnections : IInertiaLiveLocalTransport
{
    private readonly ConcurrentDictionary<string, SseConnection> _connections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SseConnection>> _channels = new(StringComparer.Ordinal);
    private readonly Lock _gate = new(); // Add and Remove only: an empty channel entry is dropped without racing a join

    public int Count => _connections.Count;

    internal SseConnection[] Snapshot() => [.. _connections.Values];

    public void Add(SseConnection connection)
    {
        lock (_gate)
        {
            _connections[connection.Id] = connection;
            foreach (var key in connection.Channels)
            {
                _channels.GetOrAdd(key, static _ => new(StringComparer.Ordinal))[connection.Id] = connection;
            }
        }
    }

    public void Remove(SseConnection connection)
    {
        lock (_gate)
        {
            if (!_connections.TryRemove(new(connection.Id, connection))) return;
            // Add and Remove both hold _gate, so a channel found here is still there on the next line.
            foreach (var key in connection.Channels.Where(_channels.ContainsKey))
            {
                var members = _channels[key];
                members.TryRemove(new(connection.Id, connection));
                if (members.IsEmpty) _channels.TryRemove(key, out _);
            }
        }
    }

    public ValueTask DeliverAsync(LiveMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var key = message.Channel.Key;
        if (!_channels.TryGetValue(key, out var members)) return default;
        string? data = null;
        foreach (var member in members)
        {
            var connection = member.Value;
            if (connection.Id == message.ExceptSocketId) continue;
            data ??= Event(key, message.Payload);
            // A connection that cannot keep up is closed: its client reconnects and reloads its live props, so nothing is lost.
            if (!connection.Queue.Writer.TryWrite(data)) connection.Close();
        }

        return default;
    }

    public ValueTask ResyncAsync(CancellationToken cancellationToken = default)
    {
        // Ending every stream makes each EventSource reconnect, and a reconnect reloads the page's live props.
        foreach (var connection in _connections.Values) connection.Close();
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

    public int Capacity { get; } = capacity;

    /// <summary>How long a closed stream may take to finish writing before its connection is aborted.</summary>
    public TimeSpan AbortAfter { get; init; } = TimeSpan.FromSeconds(5);

    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _closed;

    public Channel<string> Queue { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait, // TryWrite fails when full instead of dropping an event silently
    });

    /// <summary>Aborts the HTTP connection (set by the endpoint).</summary>
    public Action? Abort { get; set; }

    /// <summary>
    /// Ends the stream, so its client reconnects and reloads. A write blocked on a stalled client never sees the end of the queue, so
    /// the connection is aborted if the stream has not ended after <see cref="AbortAfter"/>.
    /// </summary>
    public void Close()
    {
        Queue.Writer.TryComplete();
        if (Interlocked.Exchange(ref _closed, 1) != 0 || Abort is not { } abort) return;
        _ = AbortUnlessEndedAsync(_ended.Task, AbortAfter, abort);
    }

    private static async Task AbortUnlessEndedAsync(Task ended, TimeSpan after, Action abort)
    {
        try
        {
            await ended.WaitAsync(after);
        }
        catch (TimeoutException)
        {
            abort();
        }
    }

    /// <summary>Called when the stream has ended: no abort is needed any more.</summary>
    public void Ended()
    {
        _ended.TrySetResult();
    }
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
