namespace Inertia.Net.Testing;

/// <summary>
/// An in-memory <see cref="IInertiaLiveBackplane"/> for tests: register the same instance in two hosts
/// (<c>services.AddSingleton&lt;IInertiaLiveBackplane&gt;(backplane)</c>) and a publish on one reaches the other's clients. It can
/// simulate a gap (<see cref="SimulateGap"/>), a size limit (<see cref="MaxMessageBytes"/>) and a store failure (<see cref="PublishFailure"/>).
/// </summary>
#pragma warning disable CA1001 // The SemaphoreSlim never allocates a wait handle (no AvailableWaitHandle), so it needs no Dispose.
public sealed class FakeBackplane : IInertiaLiveBackplane
#pragma warning restore CA1001
{
    private readonly Lock _gate = new();
    private readonly List<Subscriber> _subscribers = [];
    private readonly List<byte[]> _published = [];
    private readonly SemaphoreSlim _order = new(1, 1); // one delivery at a time, in publish order, like a real store

    /// <inheritdoc cref="IInertiaLiveBackplane.MaxMessageBytes" />
    public int? MaxMessageBytes { get; set; }

    /// <summary>When set, every publish throws it (the store is down).</summary>
    public Exception? PublishFailure { get; set; }

    /// <summary>Every message published so far, as the wire bytes.</summary>
    public IReadOnlyList<byte[]> Published
    {
        get
        {
            lock (_gate) return [.. _published];
        }
    }

    /// <summary>The number of open subscriptions (one per host with a transport).</summary>
    public int SubscriberCount
    {
        get
        {
            lock (_gate) return _subscribers.Count;
        }
    }

    /// <inheritdoc />
    public async ValueTask PublishAsync(LiveChannel channel, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PublishFailure is { } failure) throw failure;
        await _order.WaitAsync(cancellationToken);
        try
        {
            Subscriber[] subscribers;
            var copy = message.ToArray();
            lock (_gate)
            {
                _published.Add(copy);
                subscribers = [.. _subscribers];
            }

            foreach (var subscriber in subscribers) await subscriber.Deliver(copy);
        }
        finally
        {
            _order.Release();
        }
    }

    /// <summary>Delivers raw bytes to every subscriber, as if another process had published them (for malformed-message tests).</summary>
    public async ValueTask InjectAsync(ReadOnlyMemory<byte> message)
    {
        await _order.WaitAsync();
        try
        {
            Subscriber[] subscribers;
            lock (_gate) subscribers = [.. _subscribers];
            foreach (var subscriber in subscribers) await subscriber.Deliver(message);
        }
        finally
        {
            _order.Release();
        }
    }

    /// <summary>Reports a gap to every subscriber, or to the <paramref name="index"/>th one (in subscription order).</summary>
    public void SimulateGap(int? index = null)
    {
        Subscriber[] subscribers;
        lock (_gate) subscribers = [.. _subscribers];
        if (index is { } i) subscribers[i].Missed();
        else foreach (var subscriber in subscribers) subscriber.Missed();
    }

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> SubscribeAsync(Func<ReadOnlyMemory<byte>, ValueTask> deliver, Action missed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        ArgumentNullException.ThrowIfNull(missed);
        var subscriber = new Subscriber(this, deliver, missed);
        lock (_gate) _subscribers.Add(subscriber);
        return ValueTask.FromResult<IAsyncDisposable>(subscriber);
    }

    private sealed class Subscriber(FakeBackplane owner, Func<ReadOnlyMemory<byte>, ValueTask> deliver, Action missed) : IAsyncDisposable
    {
        public Func<ReadOnlyMemory<byte>, ValueTask> Deliver { get; } = deliver;

        public Action Missed { get; } = missed;

        public ValueTask DisposeAsync()
        {
            lock (owner._gate) owner._subscribers.Remove(this);
            return default;
        }
    }
}
