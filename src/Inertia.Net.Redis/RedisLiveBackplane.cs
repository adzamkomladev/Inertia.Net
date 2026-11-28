using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Inertia.Net;

/// <summary>Settings of the Redis live backplane (see <see cref="InertiaRedisBackplaneExtensions.AddRedisBackplane"/>).</summary>
public sealed class InertiaRedisBackplaneOptions
{
    /// <summary>
    /// The StackExchange.Redis configuration string, e.g. <c>localhost:6379</c>. Leave null to use the <see cref="IConnectionMultiplexer"/>
    /// registered in the services (it is then not disposed).
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>Put before the pub/sub channel names (<c>{Prefix}live</c>), so several apps can share one Redis. Default <c>inertia:</c>.</summary>
    public string Prefix { get; set; } = "inertia:";

    /// <summary>
    /// Uses sharded pub/sub (<c>SPUBLISH</c>/<c>SSUBSCRIBE</c>, Redis 7.0 or later) for Redis Cluster: a message stays on the shard that
    /// owns its channel's slot instead of being copied to every node. Off by default.
    /// </summary>
    public bool UseShardedPubSub { get; set; }

    /// <summary>
    /// With <see cref="UseShardedPubSub"/>: how many sharded channels (<c>{Prefix}live:0</c> to <c>{Prefix}live:{N-1}</c>) to spread
    /// traffic over. A live channel always maps to the same one, so its events stay in order. Default 1; must be between 1 and 1024.
    /// </summary>
    public int ShardCount { get; set; } = 1;
}

/// <summary>Registers the Redis live backplane.</summary>
public static class InertiaRedisBackplaneExtensions
{
    /// <summary>
    /// Carries live props between app instances and workers over Redis pub/sub: <c>AddInertiaLive().AddSse().AddRedisBackplane(o =&gt;
    /// o.Configuration = "localhost:6379")</c> on every web instance, and <c>AddInertiaLive().AddRedisBackplane(...)</c> in a worker
    /// that only publishes. Leave <c>Configuration</c> unset to use the registered <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    /// <param name="builder">The live-props builder.</param>
    /// <param name="configure">Sets the connection, prefix and sharding.</param>
    public static IInertiaLiveBuilder AddRedisBackplane(this IInertiaLiveBuilder builder, Action<InertiaRedisBackplaneOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<InertiaRedisBackplaneOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.Validate(o => o.ShardCount is >= 1 and <= 1024, "InertiaRedisBackplaneOptions.ShardCount must be between 1 and 1024.")
            .Validate(o => o.Prefix is not null, "InertiaRedisBackplaneOptions.Prefix must not be null.")
            .ValidateOnStart();
        builder.Services.TryAddSingleton<RedisLiveBackplane>();
        builder.Services.Replace(ServiceDescriptor.Singleton<IInertiaLiveBackplane>(static sp => sp.GetRequiredService<RedisLiveBackplane>()));
        return builder;
    }
}

/// <summary>Redis pub/sub as the live backplane: one channel per app, or <see cref="InertiaRedisBackplaneOptions.ShardCount"/> sharded ones.</summary>
internal sealed partial class RedisLiveBackplane : IInertiaLiveBackplane, IDisposable
{
    private readonly InertiaRedisBackplaneOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private IConnectionMultiplexer? _connection;
    private bool _ownsConnection;
    private bool _disposed;

    public RedisLiveBackplane(IOptions<InertiaRedisBackplaneOptions> options, IServiceProvider services, ILoggerFactory loggers)
    {
        _options = options.Value;
        _services = services;
        _logger = loggers.CreateLogger("Inertia.Net.Live");
    }

    public int? MaxMessageBytes => null; // Redis carries large messages; LiveWire.MaxMessageBytes still applies

    // Connected on first use, so a worker that never publishes does not connect.
    private IConnectionMultiplexer Connection
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_connection is not null) return _connection;
                _ownsConnection = _options.Configuration is not null;
                _connection = _options.Configuration is { } configuration
                    ? ConnectionMultiplexer.Connect(configuration)
                    : _services.GetService<IConnectionMultiplexer>()
                      ?? throw new InvalidOperationException("Set InertiaRedisBackplaneOptions.Configuration or register an IConnectionMultiplexer.");
                return _connection;
            }
        }
    }

    internal RedisChannel ChannelFor(LiveChannel channel)
    {
        if (!_options.UseShardedPubSub) return RedisChannel.Literal(_options.Prefix + "live");
        // A stable hash (not string.GetHashCode, which differs per process), so every publisher picks the same shard for a channel.
        var shard = _options.ShardCount == 1 ? 0 : (int)(Fnv1a(channel.Key) % (uint)_options.ShardCount);
        return RedisChannel.Sharded($"{_options.Prefix}live:{shard}");
    }

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }

    private IEnumerable<RedisChannel> AllChannels => _options.UseShardedPubSub
        ? Enumerable.Range(0, _options.ShardCount).Select(i => RedisChannel.Sharded($"{_options.Prefix}live:{i}"))
        : [RedisChannel.Literal(_options.Prefix + "live")];

    public async ValueTask PublishAsync(LiveChannel channel, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Connection.GetSubscriber().PublishAsync(ChannelFor(channel), message);
    }

    public async ValueTask<IAsyncDisposable> SubscribeAsync(Func<ReadOnlyMemory<byte>, ValueTask> deliver, Action missed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        ArgumentNullException.ThrowIfNull(missed);
        var connection = Connection;
        var subscriber = connection.GetSubscriber();
        var subscription = new Subscription(connection, deliver, missed, _logger);
        try
        {
            foreach (var channel in AllChannels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                subscription.Add(await subscriber.SubscribeAsync(channel));
            }
        }
        catch
        {
            await subscription.DisposeAsync();
            throw;
        }

        return subscription;
    }

    private static bool IsResp3(IConnectionMultiplexer connection, System.Net.EndPoint? endPoint)
    {
        try
        {
            return endPoint is not null && connection.GetServer(endPoint).Protocol == RedisProtocol.Resp3;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or RedisException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            if (_ownsConnection) _connection?.Dispose();
        }
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private readonly IConnectionMultiplexer _connection;
        private readonly Func<ReadOnlyMemory<byte>, ValueTask> _deliver;
        private readonly Action _missed;
        private readonly ILogger _logger;
        private readonly Lock _gate = new();
        private sealed class Shard(ChannelMessageQueue queue)
        {
            public ChannelMessageQueue Queue { get; } = queue;
            public Task Pump { get; set; } = Task.CompletedTask;
            public int? Discard { get; set; } = 0;
        }

        private readonly List<Shard> _shards = [];
        private readonly LiveBackplaneInbox<byte[]> _inbox = new(256);
        private readonly CancellationTokenSource _stopping = new();
        private CancellationTokenSource _generation = new();
        private readonly Task _dispatch;

        public Subscription(IConnectionMultiplexer connection, Func<ReadOnlyMemory<byte>, ValueTask> deliver, Action missed, ILogger logger)
        {
            _connection = connection;
            _deliver = deliver;
            _missed = missed;
            _logger = logger;
            _connection.ConnectionRestored += Restored;
            _dispatch = DispatchAsync();
        }

        public void Add(ChannelMessageQueue queue)
        {
            var shard = new Shard(queue);
            lock (_gate)
            {
                _shards.Add(shard);
                shard.Pump = Task.Run(() => PumpAsync(shard));
            }
        }

        private async Task PumpAsync(Shard shard)
        {
            while (!_stopping.IsCancellationRequested)
            {
                CancellationTokenSource generation;
                ValueTask<ChannelMessage> next;
                lock (_gate)
                {
                    if (_stopping.IsCancellationRequested) return;
                    if (shard.Discard is null)
                    {
                        // Fallback for a driver queue without count support. Yield the lock between items.
                        if (shard.Queue.TryRead(out _)) continue;
                        shard.Discard = 0;
                        _inbox.Enqueue(null);
                    }

                    generation = _generation;
                    next = shard.Queue.ReadAsync(generation.Token);
                }

                try
                {
                    var message = await next;
                    lock (_gate)
                    {
                        if (generation != _generation) continue;
                        if (shard.Discard > 0) shard.Discard--;
                        else _inbox.Enqueue((byte[])message.Message!);
                    }
                }
                catch (OperationCanceledException) when (generation.IsCancellationRequested)
                {
                    // A restore retired this read. Start again after the queued gap.
                    await Task.Yield();
                }
                catch (System.Threading.Channels.ChannelClosedException)
                {
                    return;
                }
            }
        }

        private void Restored(object? sender, ConnectionFailedEventArgs e)
        {
            if (e.ConnectionType != ConnectionType.Subscription &&
                !(e.ConnectionType == ConnectionType.Interactive && IsResp3(_connection, e.EndPoint))) return;

            lock (_gate)
            {
                if (_stopping.IsCancellationRequested) return;
                // A slow transport may still be delivering an older event. Put the gap behind committed
                // deliveries, retire outstanding reads, and discard this generation's driver backlog.
                var previous = _generation;
                _generation = new CancellationTokenSource();
                previous.Cancel();
                foreach (var shard in _shards)
                {
                    // The pump discards exactly the backlog present now. New publications cannot prolong this cutoff.
                    shard.Discard = shard.Queue.TryGetCount(out var count) ? count : null;
                }

                previous.Dispose();
                _inbox.Enqueue(null);
            }
        }

        private async Task DispatchAsync()
        {
            try
            {
                await foreach (var message in _inbox.Reader.ReadAllAsync(_stopping.Token))
                {
                    try
                    {
                        if (message is null) _missed();
                        else await _deliver(message);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !_stopping.IsCancellationRequested)
                    {
                        LogDeliverFailed(_logger, exception);
                    }
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                // The subscription ended.
            }
        }

        public async ValueTask DisposeAsync()
        {
            _connection.ConnectionRestored -= Restored;
            await _stopping.CancelAsync();
            lock (_gate) _generation.Cancel();
            foreach (var shard in _shards)
            {
                await shard.Pump;
                try
                {
                    await shard.Queue.UnsubscribeAsync();
                }
                catch (ObjectDisposedException)
                {
                    // The connection was closed with the host.
                }
                catch (RedisConnectionException)
                {
                    // Unsubscribing from a server that is gone has nothing left to do.
                }
            }

            await _dispatch;
            _generation.Dispose();
            _stopping.Dispose();
        }
    }

    [LoggerMessage(10, LogLevel.Error, "Handling a message from the Redis live backplane failed.")]
    private static partial void LogDeliverFailed(ILogger logger, Exception exception);
}
