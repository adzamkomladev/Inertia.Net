using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using Inertia.Net.Live.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inertia.Net.IntegrationTests;

/// <summary><c>Inertia.Net.Live.SignalR</c> end to end: a real hub connection subscribes, and <see cref="IInertiaLive"/> publishes.</summary>
public sealed class LiveSignalRTests
{
    private static readonly LiveEvent StatsChanged = new("StatsChanged", LiveChannel.Public("stats"));
    private static readonly LiveEvent OrderUpdated = new("OrderUpdated", LiveChannel.Private("orders.1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_public_subscriber_receives_published_events_with_values()
    {
        await using var app = await LiveApp.StartAsync();
        await using var client = await app.ConnectAsync();

        Assert.True(await client.SubscribeAsync(StatsChanged.Channel));
        await app.Live.PublishAsync(StatsChanged, new Dictionary<string, int> { ["stats.count"] = 3 }, cancellationToken: Ct);

        var (key, payload) = await client.NextAsync();
        Assert.Equal("public:stats", key);
        Assert.Equal("""{"__inertia":{"event":"StatsChanged","props":{"stats.count":3}}}""", payload.GetRawText());
    }

    [Fact]
    public async Task Private_channels_are_denied_by_default()
    {
        await using var app = await LiveApp.StartAsync();
        await using var client = await app.ConnectAsync();

        Assert.False(await client.SubscribeAsync(OrderUpdated.Channel));
        await app.Live.PublishAsync(OrderUpdated, cancellationToken: Ct);

        // The next message is the later public one: the refused channel delivered nothing.
        Assert.True(await client.SubscribeAsync(StatsChanged.Channel));
        await app.Live.PublishAsync(StatsChanged, cancellationToken: Ct);
        Assert.Equal("public:stats", (await client.NextAsync()).Key);
    }

    [Fact]
    public async Task A_custom_authorizer_allows_private_channels_and_can_cover_public_ones()
    {
        await using var app = await LiveApp.StartAsync(o =>
        {
            o.AuthorizePublicChannels = true;
            o.Authorize<OrdersOnly>();
        });
        await using var client = await app.ConnectAsync();

        Assert.True(await client.SubscribeAsync(OrderUpdated.Channel));
        Assert.False(await client.SubscribeAsync(LiveChannel.Private("orders.2")));
        Assert.False(await client.SubscribeAsync(StatsChanged.Channel));

        await app.Live.PublishAsync(OrderUpdated, cancellationToken: Ct);
        var (key, payload) = await client.NextAsync();
        Assert.Equal("private:orders.1", key);
        Assert.Equal("OrderUpdated", payload.GetProperty("__inertia").GetProperty("event").GetString());
    }

    [Fact]
    public async Task A_delegate_authorizer_sees_the_caller()
    {
        await using var app = await LiveApp.StartAsync(o => o.Authorize((context, channel, _) =>
            ValueTask.FromResult(channel.Name == $"users.{context.GetHttpContext()!.Request.Query["user"]}")));
        await using var client = await app.ConnectAsync("?user=7");

        Assert.True(await client.SubscribeAsync(LiveChannel.Private("users.7")));
        Assert.False(await client.SubscribeAsync(LiveChannel.Private("users.8")));
    }

    [Fact]
    public async Task Publishing_with_an_except_socket_id_skips_that_connection()
    {
        await using var app = await LiveApp.StartAsync();
        await using var publisher = await app.ConnectAsync();
        await using var other = await app.ConnectAsync();
        Assert.True(await publisher.SubscribeAsync(StatsChanged.Channel));
        Assert.True(await other.SubscribeAsync(StatsChanged.Channel));

        await app.Live.PublishAsync(StatsChanged, exceptSocketId: publisher.Connection.ConnectionId, cancellationToken: Ct);
        await app.Live.PublishAsync(new LiveEvent("Second", StatsChanged.Channel), cancellationToken: Ct);

        Assert.Equal("StatsChanged", Event((await other.NextAsync()).Payload));
        Assert.Equal("Second", Event((await other.NextAsync()).Payload));
        Assert.Equal("Second", Event((await publisher.NextAsync()).Payload)); // the first one never reached the publisher
    }

    [Fact]
    public async Task Unsubscribing_stops_delivery_and_unknown_types_are_refused()
    {
        await using var app = await LiveApp.StartAsync();
        await using var client = await app.ConnectAsync();

        Assert.False(await client.Connection.InvokeAsync<bool>("Subscribe", "secret", "stats", Ct));
        Assert.False(await client.Connection.InvokeAsync<bool>("Subscribe", "public", "", Ct));
        Assert.True(await client.SubscribeAsync(StatsChanged.Channel));
        await client.Connection.InvokeAsync("Unsubscribe", "public", "stats", Ct);
        await app.Live.PublishAsync(StatsChanged, cancellationToken: Ct);

        Assert.True(await client.SubscribeAsync(LiveChannel.Public("other")));
        await app.Live.PublishAsync(new LiveEvent("Other", LiveChannel.Public("other")), cancellationToken: Ct);
        Assert.Equal("public:other", (await client.NextAsync()).Key);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.OK)]
    [InlineData("http://localhost", HttpStatusCode.OK)]
    [InlineData("ws://localhost", HttpStatusCode.OK)]
    [InlineData("https://trusted.example", HttpStatusCode.OK)]
    [InlineData("https://evil.example", HttpStatusCode.Forbidden)]
    [InlineData("http://localhost:8080", HttpStatusCode.Forbidden)]
    [InlineData("null", HttpStatusCode.Forbidden)]
    public async Task Negotiate_rejects_other_origins_and_needs_no_antiforgery_token(string? origin, HttpStatusCode expected)
    {
        await using var app = await LiveApp.StartAsync(o => o.AllowedOrigins.Add("https://trusted.example"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/inertia-live/negotiate?negotiateVersion=1");
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        using var response = await app.Server.CreateClient().SendAsync(request, Ct);
        Assert.Equal(expected, response.StatusCode);
    }

    private static string? Event(JsonElement payload) => payload.GetProperty("__inertia").GetProperty("event").GetString();

    private sealed class OrdersOnly : ILiveChannelAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(HubCallerContext context, LiveChannel channel, CancellationToken cancellationToken) =>
            ValueTask.FromResult(channel == LiveChannel.Private("orders.1"));
    }

    private sealed class LiveApp(WebApplication app) : IAsyncDisposable
    {
        public TestServer Server { get; } = app.GetTestServer();

        public IInertiaLive Live { get; } = app.Services.GetRequiredService<IInertiaLive>();

        public static async Task<LiveApp> StartAsync(Action<InertiaLiveSignalROptions>? configure = null)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddInertia();
            builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
            builder.Services.AddInertiaLive().AddSignalR(configure);
            var app = builder.Build();
            app.UseInertia();
            app.UseInertiaAntiforgery(); // the hub is exempt: its origin check stands in for the token
            app.MapInertiaLiveHub();
            await app.StartAsync(Ct);
            return new LiveApp(app);
        }

        public async Task<LiveClient> ConnectAsync(string query = "")
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(Server.BaseAddress, "/inertia-live" + query), o =>
                {
                    o.Transports = HttpTransportType.LongPolling;
                    o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                })
                .Build();
            var client = new LiveClient(connection);
            await connection.StartAsync(Ct);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class LiveClient : IAsyncDisposable
    {
        private readonly Channel<(string Key, JsonElement Payload)> _received = Channel.CreateUnbounded<(string, JsonElement)>();

        public LiveClient(HubConnection connection)
        {
            Connection = connection;
            connection.On<string, JsonElement>(InertiaLiveHub.ClientMethod, (key, payload) => _received.Writer.TryWrite((key, payload)));
        }

        public HubConnection Connection { get; }

        public Task<bool> SubscribeAsync(LiveChannel channel) => Connection.InvokeAsync<bool>("Subscribe", channel.TypeName, channel.Name, Ct);

        public async Task<(string Key, JsonElement Payload)> NextAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            return await _received.Reader.ReadAsync(timeout.Token);
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }
}
