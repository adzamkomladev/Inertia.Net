using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Inertia.Net.Live.Sse;
using Inertia.Net.Live.SignalR;
using Inertia.Net.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net.IntegrationTests;

/// <summary>Two app instances (and a worker) on one <see cref="FakeBackplane"/>, over SSE and SignalR.</summary>
public sealed class LiveBackplaneTests
{
    private static readonly LiveEvent StatsChanged = new("StatsChanged", LiveChannel.Public("stats"));
    private static readonly LiveEvent Other = new("Other", LiveChannel.Public("other"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<WebApplication> SseHostAsync(FakeBackplane backplane, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddInertia();
        builder.Services.AddInertiaLive().AddSse();
        builder.Services.AddSingleton<IInertiaLiveBackplane>(backplane);
        configure?.Invoke(builder);
        var app = builder.Build();
        app.MapInertiaLiveSse();
        await app.StartAsync(Ct);
        return app;
    }

    private static async Task<WebApplication> SignalRHostAsync(FakeBackplane backplane, Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddInertia();
        builder.Services.AddInertiaLive().AddSignalR();
        builder.Services.AddSingleton<IInertiaLiveBackplane>(backplane);
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        app.MapInertiaLiveHub();
        try
        {
            await app.StartAsync(Ct);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private sealed class SseStream(HttpResponseMessage response, StreamReader reader) : IAsyncDisposable
    {
        public string SocketId { get; private set; } = "";

        public static async Task<SseStream> OpenAsync(WebApplication app, string channels)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/inertia-live?" + channels);
            var response = await app.GetTestClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
            var stream = new SseStream(response, new StreamReader(await response.Content.ReadAsStreamAsync(Ct)));
            var (type, data) = await stream.NextAsync();
            Assert.Equal("connected", type);
            stream.SocketId = JsonNode.Parse(data!)!["socketId"]!.GetValue<string>();
            return stream;
        }

        // The next event, or (null, null) when the server ended the stream.
        public async Task<(string? Event, string? Data)> NextAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                string? type = null, data = null;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(timeout.Token)))
                {
                    if (line.StartsWith("event: ", StringComparison.Ordinal)) type = line[7..];
                    else if (line.StartsWith("data: ", StringComparison.Ordinal)) data = line[6..];
                }

                if (line is null && type is null) return (null, null);
                if (type is null or "ping") continue;
                return (type, data);
            }
        }

        public async Task<string> NextEventNameAsync()
        {
            var (type, data) = await NextAsync();
            Assert.Equal("inertia", type);
            return JsonNode.Parse(data!)!["payload"]!["__inertia"]!["event"]!.GetValue<string>();
        }

        public ValueTask DisposeAsync()
        {
            reader.Dispose();
            response.Dispose();
            return default;
        }
    }

    [Fact]
    public async Task Sse_a_publish_on_one_host_reaches_the_other_once_and_respects_except()
    {
        var backplane = new FakeBackplane();
        await using var a = await SseHostAsync(backplane);
        await using var b = await SseHostAsync(backplane);
        await using var onA = await SseStream.OpenAsync(a, "channel=public:stats");
        await using var onB = await SseStream.OpenAsync(b, "channel=public:stats");
        await using var skipped = await SseStream.OpenAsync(b, "channel=public:stats&channel=public:other");
        var live = a.Services.GetRequiredService<IInertiaLive>();

        await live.PublishAsync(StatsChanged, exceptSocketId: skipped.SocketId, cancellationToken: Ct);
        await live.PublishAsync(Other, cancellationToken: Ct);

        Assert.Equal("StatsChanged", await onA.NextEventNameAsync());
        Assert.Equal("StatsChanged", await onB.NextEventNameAsync());
        Assert.Equal("Other", await skipped.NextEventNameAsync()); // left out of the first, one copy of the second

        await live.PublishAsync(StatsChanged, cancellationToken: Ct);
        Assert.Equal("StatsChanged", await onA.NextEventNameAsync()); // exactly once: the backplane copy of the first was skipped
    }

    [Fact]
    public async Task Sse_a_gap_closes_every_stream_on_that_host_only()
    {
        var backplane = new FakeBackplane();
        await using var a = await SseHostAsync(backplane);
        await using var b = await SseHostAsync(backplane);
        await using var onA = await SseStream.OpenAsync(a, "channel=public:stats");
        await using var onB1 = await SseStream.OpenAsync(b, "channel=public:stats");
        await using var onB2 = await SseStream.OpenAsync(b, "channel=public:other");

        backplane.SimulateGap(index: 1); // b subscribed second

        Assert.Equal((null, null), await onB1.NextAsync());
        Assert.Equal((null, null), await onB2.NextAsync());
        await a.Services.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, cancellationToken: Ct);
        Assert.Equal("StatsChanged", await onA.NextEventNameAsync());
    }

    [Fact]
    public async Task Sse_settings_from_a_configuration_reload_apply_to_the_next_stream_only()
    {
        var backplane = new FakeBackplane();
        var capacity = 1;
        await using var app = await SseHostAsync(backplane, builder => builder.Services.Configure<InertiaLiveSseOptions>(o => o.MaxQueuedEvents = capacity));
        var connections = app.Services.GetRequiredService<SseLiveConnections>();
        await using var first = await SseStream.OpenAsync(app, "channel=public:stats");

        // What a configuration reload does: the monitor rebuilds the options on the next read.
        capacity = 5;
        app.Services.GetRequiredService<IOptionsMonitorCache<InertiaLiveSseOptions>>().TryRemove(Options.DefaultName);
        Assert.Equal(5, app.Services.GetRequiredService<IOptionsMonitor<InertiaLiveSseOptions>>().CurrentValue.MaxQueuedEvents);
        await using var second = await SseStream.OpenAsync(app, "channel=public:stats");

        var capacities = connections.Snapshot().Select(c => c.Capacity).Order().ToArray();
        Assert.Equal([1, 5], capacities);
    }

    [Fact]
    public async Task Sse_the_channel_index_matches_a_scan_under_concurrent_joins_leaves_and_publishes()
    {
        var connections = new SseLiveConnections();
        var all = Enumerable.Range(0, 400).Select(i => new SseConnection($"c{i}", [$"public:{i % 7}", $"public:{i % 3}x"], 10_000)).ToArray();
        var live = all.Take(200).ToArray();
        foreach (var connection in live) connections.Add(connection);
        var payload = JsonDocument.Parse("""{"__inertia":{"event":"E"}}""").RootElement;

        await Parallel.ForEachAsync(Enumerable.Range(0, 2000), Ct, async (i, ct) =>
        {
            var connection = all[i % all.Length];
            if (i % 3 == 0) connections.Add(connection);
            else if (i % 3 == 1) connections.Remove(connection);
            else await connections.DeliverAsync(new LiveMessage(new LiveEvent("E", LiveChannel.Public($"{i % 7}")), payload), ct);
        });

        // After the churn, the index agrees with a brute-force scan of the registry.
        foreach (var connection in all) while (connection.Queue.Reader.TryRead(out _)) { }
        for (var channel = 0; channel < 7; channel++)
        {
            await connections.DeliverAsync(new LiveMessage(new LiveEvent("E", LiveChannel.Public($"{channel}")), payload), Ct);
            var key = $"public:{channel}";
            var expected = connections.Snapshot().Where(c => c.Channels.Contains(key)).Select(c => c.Id).Order();
            var received = all.Where(c => c.Queue.Reader.TryRead(out _)).Select(c => c.Id).Order();
            Assert.Equal(expected, received);
        }
    }

    [Fact]
    public async Task SignalR_a_publish_on_one_host_reaches_a_client_on_the_other_and_GroupExcept_skips_the_sender()
    {
        var backplane = new FakeBackplane();
        await using var a = await SignalRHostAsync(backplane);
        await using var b = await SignalRHostAsync(backplane);
        await using var onB = await HubClient.ConnectAsync(b);
        await using var sender = await HubClient.ConnectAsync(b);
        Assert.True(await onB.SubscribeAsync(StatsChanged.Channel));
        Assert.True(await sender.SubscribeAsync(StatsChanged.Channel));

        await a.Services.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, exceptSocketId: sender.Connection.ConnectionId, cancellationToken: Ct);
        await a.Services.GetRequiredService<IInertiaLive>().PublishAsync(new LiveEvent("Second", StatsChanged.Channel), cancellationToken: Ct);

        Assert.Equal("StatsChanged", await onB.NextEventNameAsync());
        Assert.Equal("Second", await sender.NextEventNameAsync());
    }

    [Fact]
    public async Task SignalR_a_gap_aborts_every_connection_on_that_host_and_a_new_one_rejoins()
    {
        var backplane = new FakeBackplane();
        await using var a = await SignalRHostAsync(backplane);
        await using var onA = await HubClient.ConnectAsync(a);
        Assert.True(await onA.SubscribeAsync(StatsChanged.Channel));

        backplane.SimulateGap();
        await onA.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        await using var again = await HubClient.ConnectAsync(a);
        Assert.True(await again.SubscribeAsync(StatsChanged.Channel));
        await a.Services.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, cancellationToken: Ct);
        Assert.Equal("StatsChanged", await again.NextEventNameAsync());
    }

    [Fact]
    public async Task SignalR_startup_fails_beside_a_non_default_hub_lifetime_manager_unless_allowed()
    {
        var backplane = new FakeBackplane();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => SignalRHostAsync(backplane,
            s => s.AddSingleton(typeof(HubLifetimeManager<>), typeof(ExternalLifetimeManager<>))));
        Assert.Contains("arrive twice", error.ToString(), StringComparison.Ordinal);

        await using var allowed = await SignalRHostAsync(backplane, s =>
        {
            s.AddSingleton(typeof(HubLifetimeManager<>), typeof(ExternalLifetimeManager<>));
            s.Configure<InertiaLiveSignalROptions>(o => o.AllowExternalHubLifetimeManager = true);
        });
    }

    [Fact]
    public async Task A_worker_reaches_an_SSE_host_and_a_SignalR_host_on_the_same_backplane()
    {
        var backplane = new FakeBackplane();
        await using var sse = await SseHostAsync(backplane);
        await using var signalR = await SignalRHostAsync(backplane);
        await using var stream = await SseStream.OpenAsync(sse, "channel=public:stats");
        await using var client = await HubClient.ConnectAsync(signalR);
        Assert.True(await client.SubscribeAsync(StatsChanged.Channel));

        var worker = Host.CreateApplicationBuilder();
        worker.Services.AddInertiaLive();
        worker.Services.AddSingleton<IInertiaLiveBackplane>(backplane);
        using var host = worker.Build();
        await host.StartAsync(Ct);
        await host.Services.GetRequiredService<IInertiaLive>().PublishAsync(StatsChanged, cancellationToken: Ct);
        await host.StopAsync(Ct);

        Assert.Equal("StatsChanged", await stream.NextEventNameAsync());
        Assert.Equal("StatsChanged", await client.NextEventNameAsync());
    }

    private sealed class HubClient : IAsyncDisposable
    {
        private readonly Channel<JsonElement> _received = Channel.CreateUnbounded<JsonElement>();

        private HubClient(HubConnection connection)
        {
            Connection = connection;
            connection.On<string, JsonElement>(InertiaLiveHub.ClientMethod, (_, payload) => _received.Writer.TryWrite(payload));
            connection.Closed += _ =>
            {
                Closed.TrySetResult();
                return Task.CompletedTask;
            };
        }

        public HubConnection Connection { get; }

        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<HubClient> ConnectAsync(WebApplication app)
        {
            var server = app.GetTestServer();
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(server.BaseAddress, "/inertia-live"), o =>
                {
                    o.Transports = HttpTransportType.LongPolling;
                    o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                })
                .Build();
            var client = new HubClient(connection);
            await connection.StartAsync(Ct);
            return client;
        }

        public Task<bool> SubscribeAsync(LiveChannel channel) => Connection.InvokeAsync<bool>("Subscribe", channel.TypeName, channel.Name, Ct);

        public async Task<string> NextEventNameAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var payload = await _received.Reader.ReadAsync(timeout.Token);
            return payload.GetProperty("__inertia").GetProperty("event").GetString()!;
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    // Stands in for SignalR's Redis backplane or Azure SignalR: any HubLifetimeManager that is not the default.
    private sealed class ExternalLifetimeManager<THub>(DefaultHubLifetimeManager<THub> inner) : HubLifetimeManager<THub> where THub : Hub
    {
        public ExternalLifetimeManager(ILogger<DefaultHubLifetimeManager<THub>> logger) : this(new DefaultHubLifetimeManager<THub>(logger)) { }

        public override Task OnConnectedAsync(HubConnectionContext connection) => inner.OnConnectedAsync(connection);
        public override Task OnDisconnectedAsync(HubConnectionContext connection) => inner.OnDisconnectedAsync(connection);
        public override Task SendAllAsync(string methodName, object?[] args, CancellationToken cancellationToken = default) => inner.SendAllAsync(methodName, args, cancellationToken);
        public override Task SendAllExceptAsync(string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default) => inner.SendAllExceptAsync(methodName, args, excludedConnectionIds, cancellationToken);
        public override Task SendConnectionAsync(string connectionId, string methodName, object?[] args, CancellationToken cancellationToken = default) => inner.SendConnectionAsync(connectionId, methodName, args, cancellationToken);
        public override Task SendConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object?[] args, CancellationToken cancellationToken = default) => inner.SendConnectionsAsync(connectionIds, methodName, args, cancellationToken);
        public override Task SendGroupAsync(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default) => inner.SendGroupAsync(groupName, methodName, args, cancellationToken);
        public override Task SendGroupsAsync(IReadOnlyList<string> groupNames, string methodName, object?[] args, CancellationToken cancellationToken = default) => inner.SendGroupsAsync(groupNames, methodName, args, cancellationToken);
        public override Task SendGroupExceptAsync(string groupName, string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default) => inner.SendGroupExceptAsync(groupName, methodName, args, excludedConnectionIds, cancellationToken);
        public override Task SendUserAsync(string userId, string methodName, object?[] args, CancellationToken cancellationToken = default) => inner.SendUserAsync(userId, methodName, args, cancellationToken);
        public override Task SendUsersAsync(IReadOnlyList<string> userIds, string methodName, object?[] args, CancellationToken cancellationToken = default) => inner.SendUsersAsync(userIds, methodName, args, cancellationToken);
        public override Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => inner.AddToGroupAsync(connectionId, groupName, cancellationToken);
        public override Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => inner.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
    }
}
