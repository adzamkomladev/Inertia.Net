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
        var queues = new List<ChannelMessageQueue>();

        // The connection carrying pub/sub came back: messages published meanwhile are lost. That is the subscription connection, or
        // under RESP3 the interactive one, which then carries pub/sub too.
        void Restored(object? sender, ConnectionFailedEventArgs e)
        {
            if (e.ConnectionType == ConnectionType.Subscription
                || (e.ConnectionType == ConnectionType.Interactive && IsResp3(connection, e.EndPoint)))
            {
                missed();
            }
        }

        // Registered first, so a restore while the shards are being subscribed is not missed.
        connection.ConnectionRestored += Restored;
        var subscription = new Subscription(connection, queues, Restored);
        try
        {
            foreach (var channel in AllChannels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var queue = await subscriber.SubscribeAsync(channel);
                queues.Add(queue);
                // One message at a time per channel, in publish order.
                queue.OnMessage(async message =>
                {
                    try
                    {
                        await deliver((byte[])message.Message!);
                    }
                    catch (Exception exception)
                    {
                        LogDeliverFailed(_logger, exception);
                    }
                });
            }
        }
        catch
        {
            await subscription.DisposeAsync(); // unwind the shards already subscribed
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

    private sealed class Subscription(IConnectionMultiplexer connection, List<ChannelMessageQueue> queues, EventHandler<ConnectionFailedEventArgs> restored)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            connection.ConnectionRestored -= restored;
            foreach (var queue in queues)
            {
                try
                {
                    await queue.UnsubscribeAsync();
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
        }
    }

    [LoggerMessage(10, LogLevel.Error, "Handling a message from the Redis live backplane failed.")]
    private static partial void LogDeliverFailed(ILogger logger, Exception exception);
}
