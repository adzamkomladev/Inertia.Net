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
    /// <summary>The client method every event is sent to, with the channel key and the payload.</summary>
    public const string ClientMethod = "inertia";

    /// <summary>Joins the channel when the connection may receive it.</summary>
    /// <param name="type">The channel type: <c>public</c>, <c>private</c>, <c>presence</c> or <c>encrypted-private</c>.</param>
    /// <param name="name">The channel name.</param>
    /// <returns>False when the channel is unknown or refused; the client logs a warning.</returns>
    public async Task<bool> Subscribe(string type, string name)
    {
        if (!TryGetChannel(type, name, out var channel))
        {
            LogInvalid(logger, type, name, Context.ConnectionId);
            return false;
        }

        if (channel.Type != LiveChannelType.Public || options.Value.AuthorizePublicChannels)
        {
            if (!await authorizer.AuthorizeAsync(Context, channel, Context.ConnectionAborted))
            {
                LogRefused(logger, channel.Key, Context.ConnectionId);
                return false;
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, channel.Key, Context.ConnectionAborted);
        return true;
    }

    /// <summary>Leaves the channel.</summary>
    public Task Unsubscribe(string type, string name) =>
        TryGetChannel(type, name, out var channel)
            ? Groups.RemoveFromGroupAsync(Context.ConnectionId, channel.Key, Context.ConnectionAborted)
            : Task.CompletedTask;

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
}
