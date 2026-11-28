using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inertia.Net;

/// <summary>
/// Carries live messages between processes (app instances and workers), so a publish reaches every client whichever instance it is
/// connected to. Implemented by <c>Inertia.Net.Redis</c> (<c>AddRedisBackplane()</c>) and <c>Inertia.Net.Postgres</c>
/// (<c>AddPostgresBackplane()</c>); register a custom one as a singleton <see cref="IInertiaLiveBackplane"/>. A backplane only moves
/// bytes: Inertia.Net writes and reads the messages (<see cref="LiveWire"/>) and hands every message it receives, its own included, to
/// the local transports (<see cref="IInertiaLiveLocalTransport"/>). Delivering from the store's single order keeps every instance's
/// clients on the same final value when two instances publish at once.
/// </summary>
public interface IInertiaLiveBackplane
{
    /// <summary>
    /// The largest message, in bytes, the store carries, or null for no limit besides <see cref="LiveWire.MaxMessageBytes"/>. A larger
    /// message is sent without values, so its clients reload the prop.
    /// </summary>
    int? MaxMessageBytes { get; }

    /// <summary>Sends a message (UTF-8 JSON written by <see cref="LiveWire"/>) to every subscribed process, this one included.</summary>
    /// <param name="channel">The message's channel, for stores that route or shard by it.</param>
    /// <param name="message">The serialized message.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    /// <exception cref="Exception">The store is unreachable: the caller sees the failure, as with any other write.</exception>
    ValueTask PublishAsync(LiveChannel channel, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);

    /// <summary>Receives every message published by any process, in publish order per channel.</summary>
    /// <param name="deliver">Called with each message, one at a time.</param>
    /// <param name="missed">Called when the subscription was cut off and came back, so messages may have been lost.</param>
    /// <param name="cancellationToken">Cancels subscribing.</param>
    /// <returns>Disposed to end the subscription.</returns>
    ValueTask<IAsyncDisposable> SubscribeAsync(Func<ReadOnlyMemory<byte>, ValueTask> deliver, Action missed, CancellationToken cancellationToken = default);
}

/// <summary>
/// A transport (SSE, SignalR) as the backplane relay sees it: it reaches the clients connected to this app instance. Without a backplane
/// the relay delivers each publish here directly; with one, it delivers what the <see cref="IInertiaLiveBackplane"/> subscription receives.
/// </summary>
public interface IInertiaLiveLocalTransport
{
    /// <summary>Sends <paramref name="message"/> to this instance's subscribers of its channel, except <see cref="LiveMessage.ExceptSocketId"/>.</summary>
    ValueTask DeliverAsync(LiveMessage message, CancellationToken cancellationToken = default);

    /// <summary>After a backplane gap: makes this instance's clients reconnect, so they reload their live props.</summary>
    ValueTask ResyncAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The backplane wire format, version 1: one UTF-8 JSON object,
/// <c>{"v":1,"from":"…","event":"…","channel":"type:name","except":"…","ref":42,"payload":{"__inertia":{…}}}</c>. <c>from</c> (the
/// sending instance), <c>except</c> and <c>ref</c> (a store-specific reference to the full message, see <c>Inertia.Net.Postgres</c>) are
/// optional; unknown members are ignored.
/// </summary>
public static class LiveWire
{
    /// <summary>The version this release writes and reads.</summary>
    public const int Version = 1;

    /// <summary>The largest message read (1 MiB): a larger one is dropped, and a larger publish is sent without values.</summary>
    public const int MaxMessageBytes = 1024 * 1024;

    private const int MaxOriginLength = 64;

    /// <summary>Serializes <paramref name="message"/>.</summary>
    /// <param name="message">The message.</param>
    /// <param name="origin">The sending instance or another short diagnostic id (at most 64 characters), or null.</param>
    /// <param name="reference">A store-specific reference to the full message, or null.</param>
    public static byte[] Write(LiveMessage message, string? origin = null, long? reference = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            if (origin is not null) writer.WriteString("from", origin);
            writer.WriteString("event", message.Event);
            writer.WriteString("channel", message.Channel.Key);
            if (message.ExceptSocketId is { } except) writer.WriteString("except", except);
            if (reference is { } id) writer.WriteNumber("ref", id);
            writer.WritePropertyName("payload");
            // Raw, so the payload's bytes reach every instance unchanged (WriteTo would re-escape its strings).
            writer.WriteRawValue(JsonMarshal.GetRawUtf8Value(message.Payload), skipInputValidation: true);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The same event without prop values: its clients reload the prop instead.</summary>
    public static LiveMessage WithoutValues(LiveMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var @event = new LiveEvent(message.Event, message.Channel);
        return new LiveMessage(@event, LiveEnvelope.Build(@event, writeProps: null), message.ExceptSocketId);
    }

    /// <summary>Reads a message written by <see cref="Write"/>.</summary>
    /// <param name="utf8">The serialized message.</param>
    /// <param name="message">The message.</param>
    /// <param name="origin">The sending instance, or null.</param>
    /// <param name="reference">The store-specific reference, or null.</param>
    /// <returns>False when the data is not a valid version 1 message, or is larger than <see cref="MaxMessageBytes"/>.</returns>
    public static bool TryRead(ReadOnlySpan<byte> utf8, [NotNullWhen(true)] out LiveMessage? message, out string? origin, out long? reference) =>
        TryRead(utf8, out message, out origin, out reference, out _);

    internal static bool TryRead(ReadOnlySpan<byte> utf8, [NotNullWhen(true)] out LiveMessage? message, out string? origin, out long? reference,
        out string? error)
    {
        message = null;
        origin = null;
        reference = null;
        if (utf8.Length > MaxMessageBytes)
        {
            error = "too large";
            return false;
        }

        var reader = new Utf8JsonReader(utf8);
        JsonDocument document;
        try
        {
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            error = "invalid JSON";
            return false;
        }

        using (document)
        {
            if (HasTrailingData(ref reader))
            {
                error = "trailing data";
                return false;
            }

            var root = document.RootElement;
            try
            {
                error = Validate(root, out var @event, out var channel, out origin, out var except, out reference);
                if (error is null)
                {
                    message = new LiveMessage(new LiveEvent(@event!, channel), root.GetProperty("payload").Clone(), except);
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                error = "invalid string"; // GetString on text that is not valid UTF-16, such as a lone surrogate escape
            }

            origin = null;
            reference = null;
            return false;
        }
    }

    private static bool HasTrailingData(ref Utf8JsonReader reader)
    {
        try
        {
            return reader.Read();
        }
        catch (JsonException)
        {
            return true; // something that is not even JSON follows the object
        }
    }

    private static string? Validate(JsonElement root, out string? @event, out LiveChannel channel, out string? origin, out string? except, out long? reference)
    {
        @event = origin = except = null;
        channel = default;
        reference = null;
        if (root.ValueKind != JsonValueKind.Object) return "not an object";
        if (!root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) || version != Version)
        {
            return "unknown version";
        }

        if (root.TryGetProperty("from", out var from))
        {
            if (from.ValueKind != JsonValueKind.String || from.GetString() is not { Length: > 0 and <= MaxOriginLength } value) return "invalid from";
            origin = value;
        }

        if (!root.TryGetProperty("event", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() is not { Length: > 0 } eventName)
        {
            return "invalid event";
        }

        @event = eventName;
        if (!root.TryGetProperty("channel", out var key) || key.ValueKind != JsonValueKind.String || !TryParseChannel(key.GetString()!, out channel))
        {
            return "invalid channel";
        }

        if (root.TryGetProperty("except", out var skip) && skip.ValueKind != JsonValueKind.Null)
        {
            if (skip.ValueKind != JsonValueKind.String) return "invalid except";
            except = skip.GetString();
        }

        if (root.TryGetProperty("ref", out var id))
        {
            if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var value)) return "invalid ref";
            reference = value;
        }

        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("__inertia", out var inertia) || inertia.ValueKind != JsonValueKind.Object
            || !inertia.TryGetProperty("event", out var inner) || inner.ValueKind != JsonValueKind.String || inner.GetString() != eventName
            || (inertia.TryGetProperty("props", out var props) && props.ValueKind != JsonValueKind.Object))
        {
            return "invalid payload";
        }

        return null;
    }

    private static bool TryParseChannel(string key, out LiveChannel channel)
    {
        var separator = key.IndexOf(':', StringComparison.Ordinal);
        if (separator > 0 && separator < key.Length - 1 && LiveChannel.TryParseType(key[..separator], out var type))
        {
            channel = new LiveChannel(key[(separator + 1)..], type);
            return true;
        }

        channel = default;
        return false;
    }
}

// The last slot is reserved for a gap. If a burst ends on a dropped message the reader
// still gets its gap, after every previously accepted message and before any later one.
internal sealed class LiveBackplaneInbox<T>(int capacity) where T : class
{
    private readonly Channel<T?> _channel = Channel.CreateBounded<T?>(new BoundedChannelOptions(capacity + 1) { SingleReader = true });
    private readonly Lock _gate = new();
    private bool _lastWasGap;

    public ChannelReader<T?> Reader => _channel.Reader;

    public void Enqueue(T? message)
    {
        lock (_gate)
        {
            if (message is not null && Reader.Count < capacity)
            {
                _channel.Writer.TryWrite(message);
                _lastWasGap = false;
            }
            else if (!_lastWasGap || Reader.Count == 0)
            {
                _channel.Writer.TryWrite(null);
                _lastWasGap = true;
            }
        }
    }
}

/// <summary>
/// The broadcaster <c>AddInertiaLive()</c> registers. Without a backplane it delivers each publish to this instance's transports. With
/// one it only publishes, and its subscription hands every message, this instance's own included, to the transports in the store's
/// order, and a backplane gap to <see cref="IInertiaLiveLocalTransport.ResyncAsync"/>.
/// </summary>
internal sealed partial class LiveBackplaneRelay : IInertiaLiveBroadcaster, IAsyncDisposable
{
    private readonly IInertiaLiveLocalTransport[] _locals;
    private readonly IInertiaLiveBackplane? _backplane;
    private readonly InertiaMetrics _metrics;
    private readonly ILogger _logger;
    private readonly string _tag;
    private IAsyncDisposable? _subscription;
    private CancellationTokenSource? _deliveryStopping;

    public LiveBackplaneRelay(IEnumerable<IInertiaLiveLocalTransport> locals, IServiceProvider services, InertiaMetrics metrics, ILoggerFactory loggers)
    {
        _locals = [.. locals];
        _backplane = services.GetService<IInertiaLiveBackplane>();
        _metrics = metrics;
        _logger = loggers.CreateLogger("Inertia.Net.Live");
        _tag = _backplane is null ? "" : BackplaneName(_backplane);
    }

    /// <summary>This instance's random id, the <c>from</c> of the messages it sends.</summary>
    public string InstanceId { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public async ValueTask PublishAsync(LiveMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_locals.Length == 0 && _backplane is null)
        {
            throw new InvalidOperationException(
                $"No {nameof(IInertiaLiveBroadcaster)} is registered. Add a transport (for example services.AddInertiaLive().AddSse() from Inertia.Net.Live.Sse, or .AddSignalR() from Inertia.Net.Live.SignalR), a backplane, or register your own {nameof(IInertiaLiveBroadcaster)}.");
        }

        // Until the subscription runs (no hosted services started), deliver here so a publish is never silently dropped.
        if (_backplane is null || _subscription is null)
        {
            foreach (var local in _locals)
            {
                await local.DeliverAsync(message, cancellationToken);
            }
        }

        if (_backplane is null) return;
        var data = Encode(message, _backplane.MaxMessageBytes);
        try
        {
            await _backplane.PublishAsync(message.Channel, data, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _metrics.RecordBackplane(InertiaMetrics.BackplaneErrors, _tag, "publish");
            LogPublishFailed(_logger, exception, _tag);
            throw;
        }

        _metrics.RecordBackplane(InertiaMetrics.BackplanePublished, _tag);
    }

    private byte[] Encode(LiveMessage message, int? storeLimit)
    {
        var data = LiveWire.Write(message, InstanceId);
        var limit = Math.Min(storeLimit ?? LiveWire.MaxMessageBytes, LiveWire.MaxMessageBytes);
        if (data.Length <= limit) return data;
        LogOversize(_logger, message.Event, message.Channel.Key, data.Length, limit);
        _metrics.RecordBackplane(InertiaMetrics.BackplaneOversize, _tag, "reload");
        data = LiveWire.Write(LiveWire.WithoutValues(message), InstanceId);
        if (data.Length <= limit) return data;
        // Even without values (a very long event name, channel or socket id): fail like any failed publish.
        _metrics.RecordBackplane(InertiaMetrics.BackplaneErrors, _tag, "oversize");
        throw new InvalidOperationException(
            $"The live message '{message.Event}' on '{message.Channel.Key}' is {data.Length} bytes without values, over the backplane's {limit}-byte limit.");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // A process without a transport (a worker) only publishes.
        if (_backplane is null || _locals.Length == 0 || _subscription is not null) return;
        var stopping = new CancellationTokenSource();
        try
        {
            _subscription = await _backplane.SubscribeAsync(data => ReceiveAsync(data, stopping.Token), Missed, cancellationToken);
            _deliveryStopping = stopping;
        }
        catch
        {
            stopping.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var subscription = Interlocked.Exchange(ref _subscription, null);
        if (subscription is null) return;
        using var stopping = Interlocked.Exchange(ref _deliveryStopping, null);
        if (stopping is not null) await stopping.CancelAsync();
        await subscription.DisposeAsync();
    }

    private async ValueTask ReceiveAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (!LiveWire.TryRead(data.Span, out var message, out _, out var reference, out var error))
        {
            _metrics.RecordBackplane(InertiaMetrics.BackplaneErrors, _tag, "malformed");
            LogMalformed(_logger, _tag, error!);
            return;
        }

        _metrics.RecordBackplane(InertiaMetrics.BackplaneReceived, _tag);
        if (reference is not null)
        {
            // The store could not resolve its reference (the stored message was swept or lost): the event goes out without values.
            _metrics.RecordBackplane(InertiaMetrics.BackplaneOversize, _tag, "missing");
        }

        foreach (var local in _locals)
        {
            try
            {
                await local.DeliverAsync(message, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _metrics.RecordBackplane(InertiaMetrics.BackplaneErrors, _tag, "deliver");
                LogDeliverFailed(_logger, exception, message.Event, message.Channel.Key);
            }
        }
    }

    internal void Missed()
    {
        _metrics.RecordBackplane(InertiaMetrics.BackplaneResyncs, _tag);
        LogGap(_logger, _tag);
        _ = ResyncAsync();
    }

    private async Task ResyncAsync()
    {
        foreach (var local in _locals)
        {
            try
            {
                await local.ResyncAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _metrics.RecordBackplane(InertiaMetrics.BackplaneErrors, _tag, "resync");
                LogResyncFailed(_logger, exception);
            }
        }
    }

    // A bounded tag value: the backplanes this repository ships, or "custom".
    private static string BackplaneName(IInertiaLiveBackplane backplane) => backplane.GetType().FullName switch
    {
        "Inertia.Net.RedisLiveBackplane" => "redis",
        "Inertia.Net.Postgres.PostgresLiveBackplane" => "postgres",
        _ => "custom",
    };

    [LoggerMessage(1, LogLevel.Error, "Publishing a live message to the {Backplane} backplane failed.")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, string backplane);

    [LoggerMessage(2, LogLevel.Debug, "Live event {Event} on {Channel} is {Size} bytes, over the backplane limit of {Limit}: sent without values, so clients reload the prop.")]
    private static partial void LogOversize(ILogger logger, string @event, string channel, int size, int limit);

    [LoggerMessage(3, LogLevel.Warning, "Dropped a malformed message from the {Backplane} backplane: {Reason}.")]
    private static partial void LogMalformed(ILogger logger, string backplane, string reason);

    [LoggerMessage(4, LogLevel.Warning, "The {Backplane} backplane subscription was cut off and restored; live clients on this instance reconnect and reload.")]
    private static partial void LogGap(ILogger logger, string backplane);

    [LoggerMessage(5, LogLevel.Error, "Delivering live event {Event} on {Channel} from the backplane failed.")]
    private static partial void LogDeliverFailed(ILogger logger, Exception exception, string @event, string channel);

    [LoggerMessage(6, LogLevel.Error, "Resyncing live clients after a backplane gap failed.")]
    private static partial void LogResyncFailed(ILogger logger, Exception exception);
}

/// <summary>Starts and ends the relay's backplane subscription with the host.</summary>
internal sealed class LiveBackplaneSubscription(LiveBackplaneRelay relay) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => relay.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => relay.DisposeAsync().AsTask();
}
