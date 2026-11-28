using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Inertia.Net.Redis.Tests;

/// <summary>The Redis live backplane against a real server (<c>REDIS_URL</c>; skipped without it). Each test uses a prefix of its own.</summary>
public sealed class RedisBackplaneTests
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("REDIS_URL");
    private static readonly LiveEvent StatsChanged = new("StatsChanged", LiveChannel.Public("stats"));
    private readonly string _prefix = "inertia-test:" + Guid.NewGuid().ToString("N") + ":";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal sealed class RecordingTransport : IInertiaLiveLocalTransport
    {
        public Channel<LiveMessage> Delivered { get; } = Channel.CreateUnbounded<LiveMessage>();

        public Channel<bool> Resyncs { get; } = Channel.CreateUnbounded<bool>();

        public ValueTask DeliverAsync(LiveMessage message, CancellationToken cancellationToken = default)
        {
            Delivered.Writer.TryWrite(message);
            return default;
        }

        public ValueTask ResyncAsync(CancellationToken cancellationToken = default)
        {
            Resyncs.Writer.TryWrite(true);
            return default;
        }

        public async Task<LiveMessage> NextAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            return await Delivered.Reader.ReadAsync(timeout.Token);
        }
    }

    private async Task<ServiceProvider> HostAsync(RecordingTransport? transport, Action<InertiaRedisBackplaneOptions>? configure = null,
        Action<IServiceCollection>? services = null)
    {
        Assert.SkipWhen(Url is null, "REDIS_URL is not set");
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddOptions();
        if (transport is not null) collection.AddSingleton<IInertiaLiveLocalTransport>(transport);
        collection.AddInertiaLive().AddRedisBackplane(o =>
        {
            o.Configuration = Url;
            o.Prefix = _prefix;
            configure?.Invoke(o);
        });
        services?.Invoke(collection);
        var provider = collection.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>()) await hosted.StartAsync(Ct);
        return provider;
    }

    private static async Task StopAsync(ServiceProvider provider)
    {
        foreach (var hosted in provider.GetServices<IHostedService>()) await hosted.StopAsync(Ct);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task One_event_from_a_publish_only_process_reaches_both_web_hosts()
    {
        var (a, b) = (new RecordingTransport(), new RecordingTransport());
        var hostA = await HostAsync(a);
        var hostB = await HostAsync(b);
        var worker = await HostAsync(null);
        try
        {
            await worker.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, new Dictionary<string, int> { ["n"] = 1 }, cancellationToken: Ct);
            Assert.Equal(1, (await a.NextAsync()).Payload.GetProperty("__inertia").GetProperty("props").GetProperty("n").GetInt32());
            Assert.Equal("StatsChanged", (await b.NextAsync()).Event);

            // A web host's own publish is delivered locally once, not again from Redis.
            await hostA.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, cancellationToken: Ct);
            await a.NextAsync();
            await b.NextAsync();
            await Task.Delay(200, Ct);
            Assert.False(a.Delivered.Reader.TryRead(out _));
        }
        finally
        {
            await StopAsync(hostA);
            await StopAsync(hostB);
            await StopAsync(worker);
        }
    }

    [Theory]
    [InlineData("resp2")]
    [InlineData("resp3")] // pub/sub then rides on the interactive connection, so its restore must count as a gap too
    public async Task Killing_the_pub_sub_connection_fires_a_gap_and_the_next_event_arrives(string protocol)
    {
        var transport = new RecordingTransport();
        // Kill only this host's connections by client name: tests running alongside keep theirs.
        var name = "gap-" + Guid.NewGuid().ToString("N");
        var host = await HostAsync(transport, o => o.Configuration = Url + ",name=" + name + ",protocol=" + protocol);
        try
        {
            await using var admin = await ConnectionMultiplexer.ConnectAsync(Url! + ",allowAdmin=true");
            var server = admin.GetServer(admin.GetEndPoints()[0]);
            var clients = (string)(await server.ExecuteAsync("CLIENT", "LIST"))!;
            var mine = clients.Split('\n').Where(line => line.Contains(" name=" + name + " ", StringComparison.Ordinal)).ToList();
            // Under RESP3 the subscription is on the interactive connection: kill that one; under RESP2, the pub/sub one.
            var targets = mine.Where(line => protocol == "resp3" ? line.Contains(" resp=3", StringComparison.Ordinal) : line.Contains(" sub=", StringComparison.Ordinal) && !line.Contains(" sub=0 ", StringComparison.Ordinal))
                .Select(line => line.Split(' ')[0]["id=".Length..]).ToList();
            Assert.NotEmpty(targets);
            foreach (var id in targets) await server.ExecuteAsync("CLIENT", "KILL", "ID", id);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await transport.Resyncs.Reader.ReadAsync(timeout.Token);

            var publisher = await HostAsync(null);
            // The subscription may still be restoring right after the gap: publish until it delivers.
            for (var i = 0; ; i++)
            {
                await publisher.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, cancellationToken: Ct);
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(Ct);
                wait.CancelAfter(TimeSpan.FromSeconds(1));
                try
                {
                    Assert.Equal("StatsChanged", (await transport.Delivered.Reader.ReadAsync(wait.Token)).Event);
                    break;
                }
                catch (OperationCanceledException) when (i < 10 && !Ct.IsCancellationRequested)
                {
                }
            }

            await StopAsync(publisher);
        }
        finally
        {
            await StopAsync(host);
        }
    }

    [Fact]
    public async Task A_prefix_keeps_two_apps_on_one_Redis_apart()
    {
        var mine = new RecordingTransport();
        var host = await HostAsync(mine);
        var otherApp = await HostAsync(null, o => o.Prefix = _prefix + "other:");
        try
        {
            await otherApp.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, cancellationToken: Ct);
            await Task.Delay(300, Ct);
            Assert.False(mine.Delivered.Reader.TryRead(out _));
        }
        finally
        {
            await StopAsync(host);
            await StopAsync(otherApp);
        }
    }

    [Fact]
    public async Task Sharded_pub_sub_keeps_a_channel_in_order_and_spreads_channels_across_shards()
    {
        var transport = new RecordingTransport();
        var host = await HostAsync(transport, o => (o.UseShardedPubSub, o.ShardCount) = (true, 4));
        var publisher = await HostAsync(null, o => (o.UseShardedPubSub, o.ShardCount) = (true, 4));
        try
        {
            var live = publisher.GetRequiredService<IInertiaLive>();
            for (var i = 0; i < 1000; i++)
            {
                await live.PublishAsync(StatsChanged, new Dictionary<string, int> { ["i"] = i }, cancellationToken: Ct);
            }

            for (var i = 0; i < 1000; i++)
            {
                Assert.Equal(i, (await transport.NextAsync()).Payload.GetProperty("__inertia").GetProperty("props").GetProperty("i").GetInt32());
            }

            var backplane = publisher.GetRequiredService<RedisLiveBackplane>();
            var shards = Enumerable.Range(0, 40).Select(i => backplane.ChannelFor(LiveChannel.Public($"c{i}")).ToString()).Distinct().Count();
            Assert.Equal(4, shards);
            Assert.Equal(backplane.ChannelFor(StatsChanged.Channel), backplane.ChannelFor(StatsChanged.Channel));
        }
        finally
        {
            await StopAsync(host);
            await StopAsync(publisher);
        }
    }

    [Fact]
    public async Task A_registered_multiplexer_is_borrowed_and_a_created_one_is_owned()
    {
        Assert.SkipWhen(Url is null, "REDIS_URL is not set");
        await using var redis = await ConnectionMultiplexer.ConnectAsync(Url!);
        var borrowed = await HostAsync(null, o => o.Configuration = null, s => s.AddSingleton<IConnectionMultiplexer>(redis));
        await borrowed.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, cancellationToken: Ct);
        await StopAsync(borrowed);
        Assert.True(redis.IsConnected);

        var owned = await HostAsync(null);
        var backplane = owned.GetRequiredService<RedisLiveBackplane>();
        await owned.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, cancellationToken: Ct);
        await StopAsync(owned);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await backplane.PublishAsync(StatsChanged.Channel, new byte[] { 1 }, Ct));
    }

    [Fact]
    public async Task A_1_MB_payload_arrives_intact()
    {
        var transport = new RecordingTransport();
        var host = await HostAsync(transport);
        var publisher = await HostAsync(null);
        try
        {
            var big = new string('x', 1_000_000);
            await publisher.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, new Dictionary<string, string> { ["big"] = big }, cancellationToken: Ct);
            Assert.Equal(big, (await transport.NextAsync()).Payload.GetProperty("__inertia").GetProperty("props").GetProperty("big").GetString());
        }
        finally
        {
            await StopAsync(host);
            await StopAsync(publisher);
        }
    }

    private static readonly string? ClusterUrl = Environment.GetEnvironmentVariable("REDIS_CLUSTER_URL");

    [Fact]
    public async Task Sharded_pub_sub_keeps_delivering_after_its_slot_moves_to_another_cluster_node()
    {
        Assert.SkipWhen(ClusterUrl is null, "REDIS_CLUSTER_URL is not set (a Redis Cluster, e.g. 127.0.0.1:7101,127.0.0.1:7102,127.0.0.1:7103)");
        var transport = new RecordingTransport();
        var host = await HostAsync(transport, o => (o.Configuration, o.UseShardedPubSub, o.ShardCount) = (ClusterUrl, true, 1));
        var publisher = await HostAsync(null, o => (o.Configuration, o.UseShardedPubSub, o.ShardCount) = (ClusterUrl, true, 1));
        try
        {
            var live = publisher.GetRequiredService<IInertiaLive>();
            await live.PublishAsync(StatsChanged, new Dictionary<string, int> { ["i"] = 0 }, cancellationToken: Ct);
            Assert.Equal(0, Index(await transport.NextAsync()));

            // Move the shard channel's slot to another node, as resharding does.
            await using var admin = await ConnectionMultiplexer.ConnectAsync(ClusterUrl! + ",allowAdmin=true");
            var channel = host.GetRequiredService<RedisLiveBackplane>().ChannelFor(StatsChanged.Channel).ToString();
            var slot = admin.HashSlot(channel);
            var cluster = admin.GetServer(admin.GetEndPoints()[0]).ClusterConfiguration!;
            var source = cluster.GetBySlot(slot)!;
            var target = cluster.Nodes.First(n => !n.IsReplica && n.NodeId != source.NodeId);
            await admin.GetServer(target.EndPoint!).ExecuteAsync("CLUSTER", "SETSLOT", slot, "IMPORTING", source.NodeId);
            await admin.GetServer(source.EndPoint!).ExecuteAsync("CLUSTER", "SETSLOT", slot, "MIGRATING", target.NodeId);
            foreach (var node in cluster.Nodes.Where(n => !n.IsReplica))
            {
                await admin.GetServer(node.EndPoint!).ExecuteAsync("CLUSTER", "SETSLOT", slot, "NODE", target.NodeId);
            }

            // Delivery resumes on the new owner, possibly after a gap: publish until an event arrives.
            for (var i = 1; ; i++)
            {
                try
                {
                    await live.PublishAsync(StatsChanged, new Dictionary<string, int> { ["i"] = i }, cancellationToken: Ct);
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(Ct);
                    wait.CancelAfter(TimeSpan.FromSeconds(1));
                    Assert.True(Index(await transport.Delivered.Reader.ReadAsync(wait.Token)) >= 1);
                    break;
                }
                catch (Exception exception) when (i < 30 && exception is OperationCanceledException or RedisException && !Ct.IsCancellationRequested)
                {
                }
            }

            // A subscriber that starts after the move reaches the new owner too.
            var late = new RecordingTransport();
            var lateHost = await HostAsync(late, o => (o.Configuration, o.UseShardedPubSub, o.ShardCount) = (ClusterUrl, true, 1));
            try
            {
                await live.PublishAsync(StatsChanged, new Dictionary<string, int> { ["i"] = 1000 }, cancellationToken: Ct);
                Assert.Equal(1000, Index(await late.NextAsync()));
            }
            finally
            {
                await StopAsync(lateHost);
            }
        }
        finally
        {
            await StopAsync(host);
            await StopAsync(publisher);
        }

        static int Index(LiveMessage message) => message.Payload.GetProperty("__inertia").GetProperty("props").GetProperty("i").GetInt32();
    }
}
