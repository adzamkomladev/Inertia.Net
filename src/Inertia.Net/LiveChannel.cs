namespace Inertia.Net;

/// <summary>The kind of a <see cref="LiveChannel"/>; transports use it to decide how a client subscribes.</summary>
public enum LiveChannelType
{
    /// <summary>Anyone can subscribe (<c>public</c>).</summary>
    Public,

    /// <summary>Subscribing needs authorization (<c>private</c>).</summary>
    Private,

    /// <summary>A private channel that also tracks who is subscribed, on transports that support it (<c>presence</c>).</summary>
    Presence,

    /// <summary>A private channel with end-to-end encrypted payloads, on transports that support it (<c>encrypted-private</c>).</summary>
    EncryptedPrivate,
}

/// <summary>
/// A broadcast channel live props listen on, such as <c>LiveChannel.Private($"orders.{id}")</c>. The name is unprefixed: transports
/// add any prefix their protocol needs (<c>private-</c>, <c>presence-</c>).
/// </summary>
public readonly record struct LiveChannel
{
    /// <summary>Creates a channel.</summary>
    /// <param name="name">The channel name, without a type prefix.</param>
    /// <param name="type">The channel type.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public LiveChannel(string name, LiveChannelType type)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown live channel type.");
        }

        Name = name;
        Type = type;
    }

    /// <summary>The channel name, without a type prefix.</summary>
    public string Name { get; }

    /// <summary>The channel type.</summary>
    public LiveChannelType Type { get; }

    /// <summary>The protocol name of the type: <c>public</c>, <c>private</c>, <c>presence</c> or <c>encrypted-private</c>.</summary>
    public string TypeName => NameOf(Type);

    /// <summary><c>"{type}:{name}"</c>, the key transports subscribe and route by, e.g. <c>private:orders.1</c>.</summary>
    public string Key => $"{TypeName}:{Name}";

    /// <summary>A channel anyone can subscribe to.</summary>
    public static LiveChannel Public(string name) => new(name, LiveChannelType.Public);

    /// <summary>A channel whose subscribers must be authorized.</summary>
    public static LiveChannel Private(string name) => new(name, LiveChannelType.Private);

    /// <summary>A presence channel (authorized like a private one).</summary>
    public static LiveChannel Presence(string name) => new(name, LiveChannelType.Presence);

    /// <summary>An encrypted private channel (authorized like a private one).</summary>
    public static LiveChannel EncryptedPrivate(string name) => new(name, LiveChannelType.EncryptedPrivate);

    /// <summary>Parses a protocol type name (<c>public</c>, <c>private</c>, <c>presence</c>, <c>encrypted-private</c>).</summary>
    /// <returns>False for any other value.</returns>
    public static bool TryParseType(string? value, out LiveChannelType type)
    {
        switch (value)
        {
            case "public":
                type = LiveChannelType.Public;
                return true;
            case "private":
                type = LiveChannelType.Private;
                return true;
            case "presence":
                type = LiveChannelType.Presence;
                return true;
            case "encrypted-private":
                type = LiveChannelType.EncryptedPrivate;
                return true;
            default:
                type = default;
                return false;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Key;

    private static string NameOf(LiveChannelType type) => type switch
    {
        LiveChannelType.Public => "public",
        LiveChannelType.Private => "private",
        LiveChannelType.Presence => "presence",
        LiveChannelType.EncryptedPrivate => "encrypted-private",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown live channel type."),
    };
}

/// <summary>
/// One broadcast event on one channel. Define it once and use it on both sides: to declare a live prop
/// (<see cref="InertiaProp.Live(LiveEvent)"/>) and to publish (<see cref="IInertiaLive"/>).
/// </summary>
/// <example><code>public static LiveEvent OrderUpdated(Guid id) => new("OrderUpdated", LiveChannel.Private($"orders.{id}"));</code></example>
public sealed record LiveEvent
{
    /// <summary>Creates an event.</summary>
    /// <param name="name">The event name, e.g. <c>OrderUpdated</c>.</param>
    /// <param name="channel">The channel it is broadcast on.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty, or <paramref name="channel"/> is the default value.</exception>
    public LiveEvent(string name, LiveChannel channel)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (channel.Name is null)
        {
            throw new ArgumentException("The channel must be created with a name.", nameof(channel));
        }

        Name = name;
        Channel = channel;
    }

    /// <summary>The event name.</summary>
    public string Name { get; }

    /// <summary>The channel the event is broadcast on.</summary>
    public LiveChannel Channel { get; }
}

/// <summary>A live prop's listeners and throttle, set by <see cref="InertiaProp.Live(LiveEvent)"/> and <see cref="InertiaProp.Throttle"/>.</summary>
internal sealed class LiveSpec
{
    public List<(LiveChannel Channel, string Event)> Listeners { get; } = [];

    public int? ThrottleMs { get; set; }
}
