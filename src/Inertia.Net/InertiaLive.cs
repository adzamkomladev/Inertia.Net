using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net;

/// <summary>
/// Publishes live-prop events (see <see cref="InertiaProp.Live(LiveEvent)"/>). Registered by <c>services.AddInertiaLive()</c>; a transport
/// package (or the app) registers the <see cref="IInertiaLiveBroadcaster"/> that delivers the messages.
/// </summary>
#pragma warning disable CA1716 // Keep the documented event parameter name for named-argument callers.
public interface IInertiaLive
{
    /// <summary>
    /// Broadcasts <paramref name="event"/> without values: every client whose page has a live prop listening to it reloads that prop.
    /// </summary>
    /// <param name="event">The event and its channel.</param>
    /// <param name="exceptSocketId">A connection to leave out, usually the caller's <see cref="InertiaRequest.SocketId"/>.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    ValueTask PublishAsync(LiveEvent @event, string? exceptSocketId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts <paramref name="event"/> with prop values the clients apply without a request (props left out are reloaded). Every
    /// subscriber of the channel receives the same values, so only send what all of them may see. The values are written with
    /// <paramref name="propsTypeInfo"/> and its options, as <see cref="JsonSerializer"/> would: generate its context with
    /// <c>[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]</c> for the names page props get by default.
    /// </summary>
    /// <param name="event">The event and its channel.</param>
    /// <param name="props">An object or dictionary keyed by prop path (dot paths such as <c>"auth.user"</c> stay flat).</param>
    /// <param name="propsTypeInfo">The serialization metadata of <typeparamref name="TProps"/>.</param>
    /// <param name="exceptSocketId">A connection to leave out, usually the caller's <see cref="InertiaRequest.SocketId"/>.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    ValueTask PublishAsync<TProps>(LiveEvent @event, TProps props, JsonTypeInfo<TProps> propsTypeInfo, string? exceptSocketId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts <paramref name="event"/> with prop values, serialized with the page serializer options (the naming policy and
    /// resolvers the page props use), so they match what a reload returns. Under Native AOT the type must be in the
    /// <c>JsonSerializerContext</c> chained into the HTTP JSON options, as a page prop's would.
    /// </summary>
    /// <inheritdoc cref="PublishAsync{TProps}(LiveEvent, TProps, JsonTypeInfo{TProps}, string?, CancellationToken)"/>
    ValueTask PublishAsync<TProps>(LiveEvent @event, TProps props, string? exceptSocketId = null, CancellationToken cancellationToken = default);
}

#pragma warning restore CA1716

/// <summary>Delivers live messages to subscribed clients. Implemented by a transport package such as <c>Inertia.Net.Live.SignalR</c>.</summary>
public interface IInertiaLiveBroadcaster
{
    /// <summary>Sends <paramref name="message"/> to every subscriber of its channel except <see cref="LiveMessage.ExceptSocketId"/>.</summary>
    ValueTask PublishAsync(LiveMessage message, CancellationToken cancellationToken = default);
}

/// <summary>What a broadcaster delivers to every subscriber of a channel.</summary>
public sealed class LiveMessage
{
    /// <summary>Creates a message.</summary>
    /// <param name="event">The event and its channel.</param>
    /// <param name="payload">The complete payload, an object with an <c>__inertia</c> member.</param>
    /// <param name="exceptSocketId">A connection to leave out, or null.</param>
    public LiveMessage(LiveEvent @event, JsonElement payload, string? exceptSocketId = null)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Channel = @event.Channel;
        Event = @event.Name;
        Payload = payload;
        ExceptSocketId = string.IsNullOrWhiteSpace(exceptSocketId) ? null : exceptSocketId;
    }

    /// <summary>The channel to deliver to.</summary>
    public LiveChannel Channel { get; }

    /// <summary>The event name, also in the payload as <c>__inertia.event</c>.</summary>
    public string Event { get; }

    /// <summary>The complete payload: <c>{"__inertia":{"event":"...","props":{...}}}</c>, <c>props</c> only when values were published.</summary>
    public JsonElement Payload { get; }

    /// <summary>The connection to leave out (from the client-supplied <c>X-Socket-Id</c>), or null.</summary>
    public string? ExceptSocketId { get; }
}

/// <summary>Returned by <c>AddInertiaLive()</c>; transport packages add their registration methods to it.</summary>
public interface IInertiaLiveBuilder
{
    /// <summary>The application's services.</summary>
    IServiceCollection Services { get; }
}

internal sealed class InertiaLiveBuilder(IServiceCollection services) : IInertiaLiveBuilder
{
    public IServiceCollection Services { get; } = services;
}

internal sealed class InertiaLive(InertiaPageWriter pageWriter, IServiceProvider services) : IInertiaLive
{
    private IInertiaLiveBroadcaster? _broadcaster;

    public ValueTask PublishAsync(LiveEvent @event, string? exceptSocketId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var payload = LiveEnvelope.Build(@event, writeProps: null);
        return Broadcaster.PublishAsync(new LiveMessage(@event, payload, exceptSocketId), cancellationToken);
    }

    public ValueTask PublishAsync<TProps>(LiveEvent @event, TProps props, JsonTypeInfo<TProps> propsTypeInfo, string? exceptSocketId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(propsTypeInfo);
        LiveEnvelope.ThrowIfInvalidProps(props, propsTypeInfo);
        var payload = LiveEnvelope.Build(@event, props is null ? null : writer => JsonSerializer.Serialize(writer, props, propsTypeInfo));
        return Broadcaster.PublishAsync(new LiveMessage(@event, payload, exceptSocketId), cancellationToken);
    }

    public ValueTask PublishAsync<TProps>(LiveEvent @event, TProps props, string? exceptSocketId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (props is null)
        {
            return PublishAsync(@event, exceptSocketId, cancellationToken);
        }

        // The page serializer options, so values match a reload byte for byte (naming policy, big integers, the app's contexts).
        var type = typeof(TProps) == typeof(object) ? props.GetType() : typeof(TProps);
        var info = pageWriter.SerializerOptions.GetTypeInfo(type);
        LiveEnvelope.ThrowIfInvalidProps(props, info);
        var payload = LiveEnvelope.Build(@event, writer => JsonSerializer.Serialize(writer, props, info));
        return Broadcaster.PublishAsync(new LiveMessage(@event, payload, exceptSocketId), cancellationToken);
    }

    private IInertiaLiveBroadcaster Broadcaster => _broadcaster ??= services.GetService<IInertiaLiveBroadcaster>()
        ?? throw new InvalidOperationException(
            $"No {nameof(IInertiaLiveBroadcaster)} is registered. Add a transport (for example services.AddInertiaLive().AddSignalR() from Inertia.Net.Live.SignalR) or register your own {nameof(IInertiaLiveBroadcaster)}.");
}
