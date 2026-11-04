using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Live.SignalR;

/// <summary>Options for the SignalR live-props transport, set in <c>AddInertiaLive().AddSignalR(o =&gt; ...)</c>.</summary>
public sealed class InertiaLiveSignalROptions
{
    /// <summary>Asks the <see cref="ILiveChannelAuthorizer"/> about public channels too. Off by default: anyone may join a public channel.</summary>
    public bool AuthorizePublicChannels { get; set; }

    /// <summary>The maximum number of distinct channels a connection may subscribe to. Defaults to 64; must be positive.</summary>
    public int MaxSubscriptionsPerConnection { get; set; } = 64;

    /// <summary>The maximum channel name length in characters, excluding the channel type prefix. Defaults to 200; must be positive.</summary>
    public int MaxChannelNameLength { get; set; } = 200;

    /// <summary>
    /// Origins (<c>https://app.example.com</c>) allowed to connect besides the app's own. A request with an <c>Origin</c> header that is
    /// neither is refused with 403: cookie-authenticated WebSockets are not protected by CORS.
    /// </summary>
    public IList<string> AllowedOrigins { get; } = [];

    internal Func<IServiceProvider, ILiveChannelAuthorizer>? AuthorizerFactory { get; private set; }

    /// <summary>Uses <typeparamref name="TAuthorizer"/>, created per hub invocation with constructor injection, to authorize channels.</summary>
    public InertiaLiveSignalROptions Authorize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAuthorizer>()
        where TAuthorizer : class, ILiveChannelAuthorizer
    {
        AuthorizerFactory = static sp => ActivatorUtilities.CreateInstance<TAuthorizer>(sp);
        return this;
    }

    /// <summary>Authorizes channels with <paramref name="authorize"/>.</summary>
    /// <example><code>o.Authorize((ctx, channel, ct) =&gt; ValueTask.FromResult(channel.Name == $"users.{ctx.UserIdentifier}"))</code></example>
    public InertiaLiveSignalROptions Authorize(Func<HubCallerContext, LiveChannel, CancellationToken, ValueTask<bool>> authorize)
    {
        ArgumentNullException.ThrowIfNull(authorize);
        var authorizer = new DelegateLiveChannelAuthorizer(authorize);
        AuthorizerFactory = _ => authorizer;
        return this;
    }
}
