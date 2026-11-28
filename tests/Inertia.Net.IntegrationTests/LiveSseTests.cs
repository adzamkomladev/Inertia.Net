using System.Net;
using System.Text.Json.Nodes;
using Inertia.Net.Live.Sse;
using Inertia.Net.Live.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Inertia.Net.IntegrationTests;

/// <summary><c>Inertia.Net.Live.Sse</c> end to end: a stream request names its channels, and <see cref="IInertiaLive"/> publishes.</summary>
public sealed class LiveSseTests
{
    private static readonly LiveEvent StatsChanged = new("StatsChanged", LiveChannel.Public("stats"));
    private static readonly LiveEvent OrderUpdated = new("OrderUpdated", LiveChannel.Private("orders.1"));
    private static readonly LiveEvent Unrelated = new("Unrelated", LiveChannel.Public("other"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<WebApplication> StartAsync(Action<InertiaLiveSseOptions>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddInertia();
        builder.Services.AddInertiaLive().AddSse(o =>
        {
            o.Authorize((_, channel, _) => ValueTask.FromResult(channel == OrderUpdated.Channel));
            configure?.Invoke(o);
        });
        var app = builder.Build();
        app.MapInertiaLiveSse();
        await app.StartAsync(Ct);
        return app;
    }

    private sealed class Stream(HttpResponseMessage response, StreamReader reader) : IAsyncDisposable
    {
        public HttpResponseMessage Response { get; } = response;

        public async Task<(string Event, string Data)> NextAsync(bool skipPings = true)
        {
            while (true)
            {
                string? type = null, data = null;
                for (var line = await reader.ReadLineAsync(Ct); !string.IsNullOrEmpty(line); line = await reader.ReadLineAsync(Ct))
                {
                    if (line.StartsWith("event: ", StringComparison.Ordinal)) type = line[7..];
                    else if (line.StartsWith("data: ", StringComparison.Ordinal)) data = line[6..];
                }

                if (type is null) continue;
                if (skipPings && type == "ping") continue;
                return (type, data ?? "");
            }
        }

        public ValueTask DisposeAsync()
        {
            reader.Dispose();
            Response.Dispose();
            return default;
        }
    }

    private static async Task<Stream> OpenAsync(WebApplication app, string query, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/inertia-live" + query);
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (origin is not null) request.Headers.Add("Origin", origin);
        var response = await app.GetTestClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        return new Stream(response, new StreamReader(await response.Content.ReadAsStreamAsync(Ct)));
    }

    [Fact]
    public async Task A_stream_gets_its_authorized_channels_events_and_a_socket_id_the_publisher_can_leave_out()
    {
        await using var app = await StartAsync();
        await using var stream = await OpenAsync(app, "?channel=public:stats&channel=private:orders.1&channel=private:orders.2&channel=nope:x");
        Assert.Equal(HttpStatusCode.OK, stream.Response.StatusCode);
        Assert.Equal("text/event-stream", stream.Response.Content.Headers.ContentType?.MediaType);

        var (type, data) = await stream.NextAsync();
        Assert.Equal("connected", type);
        var connected = JsonNode.Parse(data)!;
        var socketId = connected["socketId"]!.GetValue<string>();
        Assert.Equal(32, socketId.Length);
        Assert.Equal(["private:orders.2", "nope:x"], connected["refused"]!.AsArray().Select(n => n!.GetValue<string>()));

        var live = app.Services.GetRequiredService<IInertiaLive>();
        await live.PublishAsync(Unrelated, cancellationToken: Ct);
        await live.PublishAsync(StatsChanged, exceptSocketId: socketId, cancellationToken: Ct);
        await live.PublishAsync(OrderUpdated, new Dictionary<string, int> { ["total"] = 3 }, cancellationToken: Ct);

        (type, data) = await stream.NextAsync();
        Assert.Equal("inertia", type);
        var message = JsonNode.Parse(data)!;
        Assert.Equal("private:orders.1", message["channel"]!.GetValue<string>());
        Assert.Equal("OrderUpdated", message["payload"]!["__inertia"]!["event"]!.GetValue<string>());
        Assert.Equal(3, message["payload"]!["__inertia"]!["props"]!["total"]!.GetValue<int>());

        await live.PublishAsync(StatsChanged, cancellationToken: Ct);
        Assert.Equal("public:stats", JsonNode.Parse((await stream.NextAsync()).Data)!["channel"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_idle_stream_gets_pings()
    {
        await using var app = await StartAsync(o => o.HeartbeatInterval = TimeSpan.FromMilliseconds(50));
        await using var stream = await OpenAsync(app, "?channel=public:stats");
        Assert.Equal("connected", (await stream.NextAsync()).Event);
        Assert.Equal("ping", (await stream.NextAsync(skipPings: false)).Event);
    }

    [Fact]
    public async Task Another_origin_and_too_many_channels_are_refused()
    {
        await using var app = await StartAsync(o => o.MaxSubscriptionsPerConnection = 2);
        await using (var other = await OpenAsync(app, "?channel=public:stats", origin: "https://evil.example"))
            Assert.Equal(HttpStatusCode.Forbidden, other.Response.StatusCode);
        await using (var many = await OpenAsync(app, "?channel=public:a&channel=public:b&channel=public:c"))
            Assert.Equal(HttpStatusCode.BadRequest, many.Response.StatusCode);
    }

    [Fact]
    public async Task A_connection_that_falls_behind_is_closed_so_its_client_reconnects()
    {
        await using var app = await StartAsync(o => o.MaxQueuedEvents = 1);
        var connections = app.Services.GetRequiredService<SseLiveConnections>();
        var connection = new SseConnection("slow", ["public:stats"], 1);
        connections.Add(connection);
        var live = app.Services.GetRequiredService<IInertiaLive>();
        await live.PublishAsync(StatsChanged, cancellationToken: Ct);
        await live.PublishAsync(StatsChanged, cancellationToken: Ct);
        Assert.True(connection.Queue.Reader.Completion.IsCompleted || connection.Queue.Reader.Count == 1);
        Assert.True(connection.Queue.Reader.TryRead(out _));
        await connection.Queue.Reader.Completion.WaitAsync(Ct);
    }

    [Fact]
    public async Task A_closed_stream_that_does_not_end_is_aborted_and_one_that_ends_is_not()
    {
        var stalled = new SseConnection("stalled", ["public:stats"], 1) { AbortAfter = TimeSpan.FromMilliseconds(50) };
        var aborted = new TaskCompletionSource();
        stalled.Abort = () => aborted.TrySetResult();
        stalled.Close();
        await aborted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var finished = new SseConnection("finished", ["public:stats"], 1) { AbortAfter = TimeSpan.FromMilliseconds(50) };
        var abortedToo = false;
        finished.Abort = () => abortedToo = true;
        finished.Close();
        finished.Ended();
        await Task.Delay(200, Ct);
        Assert.False(abortedToo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sse_does_not_inherit_a_policy_configured_only_for_the_hub(bool hubFirst)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddInertia();
        var live = builder.Services.AddInertiaLive();
        if (!hubFirst) live.AddSse();
        live.AddSignalR(o => o.Authorize((_, _, _) => ValueTask.FromResult(true)));
        if (hubFirst) live.AddSse();
        await using var app = builder.Build();
        app.MapInertiaLiveSse();
        await app.StartAsync(Ct);

        await using var stream = await OpenAsync(app, "?channel=private:orders.1");
        var connected = JsonNode.Parse((await stream.NextAsync()).Data)!;
        Assert.Equal(["private:orders.1"], connected["refused"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task A_stalled_write_is_aborted_at_expiry()
    {
        var lifetime = new AbortRecorder();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature>(lifetime);
        var clock = new FakeTimeProvider();
        var expiry = InertiaLiveSseExtensions.AbortAtExpiryAsync(http, clock.GetUtcNow().AddMinutes(1), clock, Ct);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(lifetime.Aborted);
        clock.Advance(TimeSpan.FromSeconds(1));
        await expiry.WaitAsync(Ct);
        Assert.True(lifetime.Aborted);
    }

    private sealed class AbortRecorder : Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature
    {
        public bool Aborted { get; private set; }

        public CancellationToken RequestAborted { get; set; }

        public void Abort() => Aborted = true;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_event_stream_stops_at_expiry_while_waiting_or_draining(bool draining)
    {
        var connection = new SseConnection("stream", ["private:orders.1"], 2);
        var clock = new FakeTimeProvider();
        var deadline = clock.GetUtcNow().AddMinutes(1);
        await using var stream = InertiaLiveSseExtensions.StreamAsync(connection, "{}", TimeSpan.FromMinutes(1), deadline, clock, Ct)
            .GetAsyncEnumerator(Ct);
        Assert.True(await stream.MoveNextAsync()); // connected
        if (draining)
        {
            Assert.True(connection.Queue.Writer.TryWrite("before expiry"));
            Assert.True(connection.Queue.Writer.TryWrite("queued"));
            Assert.True(await stream.MoveNextAsync());
        }

        var next = draining ? default : stream.MoveNextAsync();
        clock.Advance(TimeSpan.FromMinutes(2));
        if (draining) next = stream.MoveNextAsync();
        else Assert.True(connection.Queue.Writer.TryWrite("after expiry"));
        Assert.False(await next);
    }

    [Fact]
    public async Task With_SignalR_registered_first_SSE_still_uses_its_own_authorizer()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddInertia();
        builder.Services.AddInertiaLive()
            .AddSignalR(o => o.Authorize((_, _, _) => ValueTask.FromResult(true)))   // permissive
            .AddSse(o => o.Authorize((_, channel, _) => ValueTask.FromResult(channel == OrderUpdated.Channel)));
        await using var app = builder.Build();
        app.MapInertiaLiveSse();
        await app.StartAsync(Ct);

        await using var stream = await OpenAsync(app, "?channel=private:orders.1&channel=private:orders.2");
        var connected = JsonNode.Parse((await stream.NextAsync()).Data)!;
        Assert.Equal(["private:orders.2"], connected["refused"]!.AsArray().Select(n => n!.GetValue<string>()));
    }
}
