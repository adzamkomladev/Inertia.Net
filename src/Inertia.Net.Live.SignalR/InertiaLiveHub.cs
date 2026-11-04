using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net.Live.SignalR;

/// <summary>
/// The hub the client transport connects to. A client joins a channel with <c>Subscribe(type, name)</c> and receives every event on it
/// as <c>inertia(channelKey, payload)</c>. Map it with <c>app.MapInertiaLiveHub()</c>.
/// </summary>
public sealed partial class InertiaLiveHub(ILiveChannelAuthorizer authorizer, IOptions<InertiaLiveSignalROptions> options, ILogger<InertiaLiveHub> logger) : Hub
{
    private static readonly object SubscriptionsKey = new();

    /// <summary>The client method every event is sent to, with the channel key and the payload.</summary>
    public const string ClientMethod = "inertia";

    /// <summary>Joins the channel when the connection may receive it.</summary>
    /// <param name="type">The channel type: <c>public</c>, <c>private</c>, <c>presence</c> or <c>encrypted-private</c>.</param>
    /// <param name="name">The channel name.</param>
    /// <returns>False when the channel is unknown, refused or exceeds the configured channel name or subscription limit; the client logs a warning.</returns>
    public async Task<bool> Subscribe(string type, string name)
    {
        var limits = options.Value;
        if (name?.Length > limits.MaxChannelNameLength)
        {
            LogNameLimit(logger, limits.MaxChannelNameLength, Context.ConnectionId);
            return false;
        }

        if (!TryGetChannel(type, name, out var channel))
        {
            LogInvalid(logger, type, name, Context.ConnectionId);
            return false;
        }

        var key = channel.Key;
        var subscriptions = GetSubscriptions();
        await subscriptions.Gate.WaitAsync(Context.ConnectionAborted);
        try
        {
            if (!subscriptions.Channels.Contains(key) && subscriptions.Channels.Count >= limits.MaxSubscriptionsPerConnection)
            {
                LogSubscriptionLimit(logger, limits.MaxSubscriptionsPerConnection, Context.ConnectionId);
                return false;
            }

            if (channel.Type != LiveChannelType.Public || limits.AuthorizePublicChannels)
            {
                if (!await authorizer.AuthorizeAsync(Context, channel, Context.ConnectionAborted))
                {
                    LogRefused(logger, key, Context.ConnectionId);
                    return false;
                }
            }

            if (subscriptions.Channels.Contains(key)) return true;
            await Groups.AddToGroupAsync(Context.ConnectionId, key, Context.ConnectionAborted);
            subscriptions.Channels.Add(key);
            return true;
        }
        finally
        {
            subscriptions.Gate.Release();
        }
    }

    /// <summary>Leaves the channel.</summary>
    public async Task Unsubscribe(string type, string name)
    {
        if (!TryGetChannel(type, name, out var channel)) return;
        var key = channel.Key;
        var subscriptions = GetSubscriptions();
        await subscriptions.Gate.WaitAsync(Context.ConnectionAborted);
        try
        {
            if (!subscriptions.Channels.Contains(key)) return;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, key, Context.ConnectionAborted);
            subscriptions.Channels.Remove(key);
        }
        finally
        {
            subscriptions.Gate.Release();
        }
    }

    private Subscriptions GetSubscriptions()
    {
        lock (Context.Items)
        {
            if (Context.Items.TryGetValue(SubscriptionsKey, out var value)) return (Subscriptions)value!;
            var subscriptions = new Subscriptions();
            Context.Items[SubscriptionsKey] = subscriptions;
            return subscriptions;
        }
    }

    private sealed class Subscriptions
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public HashSet<string> Channels { get; } = new(StringComparer.Ordinal);
    }

    private static bool TryGetChannel(string? type, string? name, out LiveChannel channel)
    {
        if (string.IsNullOrEmpty(name) || !LiveChannel.TryParseType(type, out var parsed))
        {
            channel = default;
            return false;
        }

        channel = new LiveChannel(name, parsed);
        return true;
    }

    [LoggerMessage(LogLevel.Debug, "Live channel subscription refused: '{Channel}' for connection {ConnectionId}.")]
    private static partial void LogRefused(ILogger logger, string channel, string connectionId);

    [LoggerMessage(LogLevel.Debug, "Invalid live channel '{Type}:{Name}' requested by connection {ConnectionId}.")]
    private static partial void LogInvalid(ILogger logger, string? type, string? name, string connectionId);

    [LoggerMessage(LogLevel.Debug, "Live channel name exceeds the limit of {Limit} characters for connection {ConnectionId}.")]
    private static partial void LogNameLimit(ILogger logger, int limit, string connectionId);

    [LoggerMessage(LogLevel.Debug, "Live channel subscription limit of {Limit} reached for connection {ConnectionId}.")]
    private static partial void LogSubscriptionLimit(ILogger logger, int limit, string connectionId);
}
