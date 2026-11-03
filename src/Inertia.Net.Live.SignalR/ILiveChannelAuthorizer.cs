using Microsoft.AspNetCore.SignalR;

namespace Inertia.Net.Live.SignalR;

/// <summary>
/// Decides whether a connection may subscribe to a live channel. Called for private, presence and encrypted-private channels (and for
/// public ones when <see cref="InertiaLiveSignalROptions.AuthorizePublicChannels"/> is set). Resolved per hub invocation, so an
/// implementation can use scoped services such as a <c>DbContext</c>. The default denies every channel it is asked about.
/// </summary>
public interface ILiveChannelAuthorizer
{
    /// <summary>Returns true when the connection may receive the channel's events.</summary>
    /// <param name="context">The hub caller: <see cref="HubCallerContext.User"/> is the authenticated user.</param>
    /// <param name="channel">The channel the client asks for.</param>
    /// <param name="cancellationToken">Cancelled when the connection closes.</param>
    ValueTask<bool> AuthorizeAsync(HubCallerContext context, LiveChannel channel, CancellationToken cancellationToken);
}

/// <summary>The default authorizer: denies every channel it is asked about (public channels are not asked by default).</summary>
internal sealed class DenyingLiveChannelAuthorizer : ILiveChannelAuthorizer
{
    public static DenyingLiveChannelAuthorizer Instance { get; } = new();

    public ValueTask<bool> AuthorizeAsync(HubCallerContext context, LiveChannel channel, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}

internal sealed class DelegateLiveChannelAuthorizer(Func<HubCallerContext, LiveChannel, CancellationToken, ValueTask<bool>> authorize) : ILiveChannelAuthorizer
{
    public ValueTask<bool> AuthorizeAsync(HubCallerContext context, LiveChannel channel, CancellationToken cancellationToken) =>
        authorize(context, channel, cancellationToken);
}
