using Microsoft.AspNetCore.SignalR;

namespace Inertia.Net.Live.SignalR;

/// <summary>Delivers live messages to the members of the channel's SignalR group (<c>"{type}:{name}"</c>).</summary>
internal sealed class SignalRLiveBroadcaster(IHubContext<InertiaLiveHub> hub) : IInertiaLiveBroadcaster
{
    public ValueTask PublishAsync(LiveMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var key = message.Channel.Key;
        var clients = message.ExceptSocketId is { } except ? hub.Clients.GroupExcept(key, [except]) : hub.Clients.Group(key);
        return new ValueTask(clients.SendAsync(InertiaLiveHub.ClientMethod, key, message.Payload, cancellationToken));
    }
}
