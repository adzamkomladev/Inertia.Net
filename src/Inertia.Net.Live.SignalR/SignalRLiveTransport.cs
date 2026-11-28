using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net.Live.SignalR;

/// <summary>
/// This instance's SignalR transport: delivers live messages to the members of the channel's group (<c>"{type}:{name}"</c>), and after
/// a backplane gap aborts every connection, so each client reconnects, rejoins its channels and reloads its live props.
/// </summary>
internal sealed partial class SignalRLiveTransport : IInertiaLiveLocalTransport
{
    private readonly IHubContext<InertiaLiveHub> _hub;
    private readonly SignalRLiveConnections _connections;

    public SignalRLiveTransport(IHubContext<InertiaLiveHub> hub, SignalRLiveConnections connections, IServiceProvider services,
        IOptions<InertiaLiveSignalROptions> options, ILogger<SignalRLiveTransport> logger)
    {
        _hub = hub;
        _connections = connections;
        // Groups are local to the instance with the default lifetime manager; SignalR's own backplane (Redis, Azure SignalR) would
        // deliver every backplane message a second time.
        if (services.GetService<IInertiaLiveBackplane>() is not null
            && services.GetService<HubLifetimeManager<InertiaLiveHub>>() is { } manager
            && manager.GetType() != typeof(DefaultHubLifetimeManager<InertiaLiveHub>))
        {
            if (!options.Value.AllowExternalHubLifetimeManager)
            {
                throw new InvalidOperationException(
                    $"Both an Inertia.Net live backplane and SignalR's own scale-out ({manager.GetType().Name}) are registered, so every live event would arrive twice. Remove one: drop AddStackExchangeRedis()/AddAzureSignalR() for InertiaLiveHub, or the Inertia backplane. Set InertiaLiveSignalROptions.AllowExternalHubLifetimeManager to run both deliberately.");
            }

            LogExternalManager(logger, manager.GetType().Name);
        }
    }

    public ValueTask DeliverAsync(LiveMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var key = message.Channel.Key;
        var clients = message.ExceptSocketId is { } except ? _hub.Clients.GroupExcept(key, [except]) : _hub.Clients.Group(key);
        return new ValueTask(clients.SendAsync(InertiaLiveHub.ClientMethod, key, message.Payload, cancellationToken));
    }

    public ValueTask ResyncAsync(CancellationToken cancellationToken = default)
    {
        foreach (var connection in _connections.All) connection.Abort();
        return default;
    }

    [LoggerMessage(1, LogLevel.Warning, "InertiaLiveHub uses {Manager} beside an Inertia.Net live backplane: live events can arrive twice (AllowExternalHubLifetimeManager is set).")]
    private static partial void LogExternalManager(ILogger logger, string manager);
}

/// <summary>The hub connections open on this instance, so a backplane gap can abort them.</summary>
internal sealed class SignalRLiveConnections
{
    private readonly ConcurrentDictionary<string, HubCallerContext> _connections = new(StringComparer.Ordinal);

    public ICollection<HubCallerContext> All => _connections.Values;

    public void Add(HubCallerContext context) => _connections[context.ConnectionId] = context;

    public void Remove(HubCallerContext context) => _connections.TryRemove(context.ConnectionId, out _);
}
