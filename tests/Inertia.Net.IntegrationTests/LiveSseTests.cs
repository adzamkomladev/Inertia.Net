using System.Net;
using System.Text.Json.Nodes;
using Inertia.Net.Live.Sse;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

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
}
