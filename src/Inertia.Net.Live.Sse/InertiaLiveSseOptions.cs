using System.Diagnostics.CodeAnalysis;
using Inertia.Net.Live.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Live.Sse;

/// <summary>Options for the Server-Sent Events live-props transport, set in <c>AddInertiaLive().AddSse(o =&gt; ...)</c>.</summary>
public sealed class InertiaLiveSseOptions
{
    /// <summary>Asks the <see cref="ILiveChannelAuthorizer"/> about public channels too. Off by default: anyone may join a public channel.</summary>
    public bool AuthorizePublicChannels { get; set; }

    /// <summary>The maximum number of channels one connection may ask for. Defaults to 64; must be positive.</summary>
    public int MaxSubscriptionsPerConnection { get; set; } = 64;

    /// <summary>The maximum channel name length in characters, excluding the channel type prefix. Defaults to 200; must be positive.</summary>
    public int MaxChannelNameLength { get; set; } = 200;

    /// <summary>
    /// How many events wait for a slow connection before it is closed (the client reconnects and reloads its live props). Defaults to
    /// 256; must be positive.
    /// </summary>
    public int MaxQueuedEvents { get; set; } = 256;

    /// <summary>How often an idle connection gets a <c>ping</c> event, so proxies keep it open. Defaults to 15 seconds; must be positive.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Origins (<c>https://app.example.com</c>) allowed to connect besides the app's own. A request with an <c>Origin</c> header that is
    /// neither is refused with 403.
    /// </summary>
    public IList<string> AllowedOrigins { get; } = [];

    internal Func<IServiceProvider, ILiveChannelAuthorizer>? AuthorizerFactory { get; private set; }

    /// <summary>
    /// Uses <typeparamref name="TAuthorizer"/>, created per connection with constructor injection, to authorize channels. Its
    /// <see cref="HubCallerContext"/> wraps the stream request: <c>User</c>, <c>UserIdentifier</c> (the name identifier claim),
    /// <c>ConnectionId</c> (the socket id) and <c>ConnectionAborted</c>. The same authorizer can serve the SignalR transport.
    /// </summary>
    public InertiaLiveSseOptions Authorize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAuthorizer>()
        where TAuthorizer : class, ILiveChannelAuthorizer
    {
        var create = ActivatorUtilities.CreateFactory<TAuthorizer>([]);
        AuthorizerFactory = sp => create(sp, null);
        return this;
    }

    /// <summary>Authorizes channels with <paramref name="authorize"/>.</summary>
    /// <example><code>o.Authorize((ctx, channel, ct) =&gt; ValueTask.FromResult(channel.Name == $"users.{ctx.UserIdentifier}"))</code></example>
    public InertiaLiveSseOptions Authorize(Func<HubCallerContext, LiveChannel, CancellationToken, ValueTask<bool>> authorize)
    {
        ArgumentNullException.ThrowIfNull(authorize);
        var authorizer = new DelegateLiveChannelAuthorizer(authorize);
        AuthorizerFactory = _ => authorizer;
        return this;
    }
}
