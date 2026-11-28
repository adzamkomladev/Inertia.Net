using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Inertia.Net.Live.Sse;
using Inertia.Net.Live.SignalR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Registers and maps the Server-Sent Events live-props transport.</summary>
public static class InertiaLiveSseExtensions
{
    /// <summary>
    /// Delivers live props over Server-Sent Events: registers the open-stream registry as this instance's local transport, and the
    /// channel authorizer. Map the endpoint with <see cref="MapInertiaLiveSse"/>. Events reach the clients connected to this app
    /// instance; with several instances or a separate worker, add a backplane (<c>.AddRedisBackplane()</c>,
    /// <c>.AddPostgresBackplane()</c>) and every instance delivers every event. No sticky sessions are needed.
    /// </summary>
    public static IInertiaLiveBuilder AddSse(this IInertiaLiveBuilder builder, Action<InertiaLiveSseOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        var options = services.AddOptions<InertiaLiveSseOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.Validate(o => o.MaxSubscriptionsPerConnection > 0, "InertiaLiveSseOptions.MaxSubscriptionsPerConnection must be positive.")
            .Validate(o => o.MaxChannelNameLength > 0, "InertiaLiveSseOptions.MaxChannelNameLength must be positive.")
            .Validate(o => o.MaxQueuedEvents > 0, "InertiaLiveSseOptions.MaxQueuedEvents must be positive.")
            .Validate(o => o.HeartbeatInterval > TimeSpan.Zero, "InertiaLiveSseOptions.HeartbeatInterval must be positive.")
            .ValidateOnStart();
        services.TryAddSingleton<SseLiveConnections>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInertiaLiveLocalTransport, SseLiveConnections>(static sp => sp.GetRequiredService<SseLiveConnections>()));
        services.TryAddSingleton<LiveOrigin>();
        return builder;
    }

    /// <summary>
    /// Maps the event stream at <paramref name="pattern"/>: <c>GET {pattern}?channel=public:posts&amp;channel=private:users.1</c> opens a
    /// <c>text/event-stream</c> for the channels the <see cref="ILiveChannelAuthorizer"/> allows. Its first event, <c>connected</c>,
    /// carries <c>{ "socketId", "refused": [channel keys] }</c>; every live event follows as <c>inertia</c> with
    /// <c>{ "channel", "payload" }</c>, and an idle stream gets <c>ping</c>. A request whose <c>Origin</c> is neither the app's own nor in
    /// <see cref="InertiaLiveSseOptions.AllowedOrigins"/> gets 403. The stream ends when the sign-in's cookie expires, so the client
    /// reconnects and is authorized again. Chain <c>RequireAuthorization()</c> to require a signed-in user.
    /// </summary>
    public static IEndpointConventionBuilder MapInertiaLiveSse(this IEndpointRouteBuilder endpoints, string pattern = "/inertia-live")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        var services = endpoints.ServiceProvider;
        // Read per connection: a configuration reload applies to new streams; open ones keep the settings they started with.
        var options = services.GetRequiredService<IOptionsMonitor<InertiaLiveSseOptions>>();
        var origin = services.GetRequiredService<LiveOrigin>();
        var connections = services.GetRequiredService<SseLiveConnections>();
        var stopping = services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        return endpoints.MapGet(pattern, http => ConnectAsync(http, options.CurrentValue, origin, connections, stopping));
    }

    private static async Task ConnectAsync(HttpContext http, InertiaLiveSseOptions options, LiveOrigin origin, SseLiveConnections connections, CancellationToken stopping)
    {
        if (!origin.IsAllowed(http.Request, options.AllowedOrigins))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var requested = http.Request.Query["channel"];
        if (requested.Count > options.MaxSubscriptionsPerConnection)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var caller = new SseCallerContext(http, id);
        // SSE's own Authorize(...) first, so a SignalR policy registered alongside never decides for SSE; then an app-registered
        // ILiveChannelAuthorizer (shared by both transports); otherwise only public channels.
        var authorizer = options.AuthorizerFactory?.Invoke(http.RequestServices)
            ?? http.RequestServices.GetService<ILiveChannelAuthorizer>()
            ?? DenyingLiveChannelAuthorizer.Instance;
        var channels = new HashSet<string>(StringComparer.Ordinal);
        var refused = new List<string>();
        foreach (var key in requested.OfType<string>())
        {
            var separator = key.IndexOf(':', StringComparison.Ordinal);
            var name = separator < 0 ? "" : key[(separator + 1)..];
            if (name.Length == 0 || name.Length > options.MaxChannelNameLength || !LiveChannel.TryParseType(key[..separator], out var type))
            {
                refused.Add(key);
                continue;
            }

            var channel = new LiveChannel(name, type);
            if ((type != LiveChannelType.Public || options.AuthorizePublicChannels)
                && !await authorizer.AuthorizeAsync(caller, channel, http.RequestAborted))
            {
                refused.Add(key);
                continue;
            }

            channels.Add(channel.Key);
        }

        var connection = new SseConnection(id, channels, options.MaxQueuedEvents) { Abort = http.Abort };
        // Set by UseAuthentication(); without it (or without a sign-in) the stream has no expiry.
        var expires = http.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Properties?.ExpiresUtc;
        using var aborted = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, stopping);
        connections.Add(connection);
        try
        {
            await TypedResults.ServerSentEvents(StreamAsync(connection, Connected(id, refused), options.HeartbeatInterval, expires, aborted.Token))
                .ExecuteAsync(http);
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            // The client went away, or the app is stopping.
        }
        finally
        {
            connections.Remove(connection);
            connection.Ended();
        }
    }

    private static async IAsyncEnumerable<SseItem<string>> StreamAsync(SseConnection connection, string connected, TimeSpan heartbeat,
        DateTimeOffset? expires, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new SseItem<string>(connected, "connected");
        var reader = connection.Queue.Reader;
        while (expires is null || expires > DateTimeOffset.UtcNow)
        {
            var ready = await WaitAsync(reader, heartbeat, cancellationToken);
            if (ready is false) yield break; // closed for falling behind
            if (ready is null)
            {
                yield return new SseItem<string>("", "ping");
                continue;
            }

            while (reader.TryRead(out var data))
            {
                yield return new SseItem<string>(data, "inertia");
            }
        }
    }

    // True when events are queued, false when the queue was closed, null when the heartbeat interval passed first.
    private static async ValueTask<bool?> WaitAsync(ChannelReader<string> reader, TimeSpan heartbeat, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(heartbeat);
        try
        {
            return await reader.WaitToReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string Connected(string id, List<string> refused)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("socketId", id);
            writer.WriteStartArray("refused");
            foreach (var key in refused) writer.WriteStringValue(key);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }
}
