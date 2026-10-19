namespace Inertia.Net;

/// <summary>The state of one SSR server's circuit.</summary>
internal enum SsrCircuitState
{
    Closed = 0,
    HalfOpen = 1,
    Open = 2,
}

/// <summary>
/// Consecutive-failure circuit breaker for one SSR server (docs/protocol.md §8). Closed: every render goes through. Open: renders skip
/// the server until the break elapses. Half-open: exactly one render probes the server; its result closes or reopens the circuit.
/// The closed-state hot path is a volatile read; the lock is only taken on failures and transitions.
/// </summary>
internal sealed class SsrCircuitBreaker(SsrCircuitBreakerOptions options, TimeProvider timeProvider, Action<SsrCircuitState>? transitioned = null)
{
    private readonly object _gate = new();
    private readonly bool _enabled = options.Enabled;
    private readonly int _threshold = Math.Max(1, options.FailureThreshold);
    private readonly TimeSpan _breakDuration = options.BreakDuration;
    private volatile int _state;
    private int _failures;
    private long _openUntil;

    public SsrCircuitState State => (SsrCircuitState)_state;

    public bool IsClosed => _state == (int)SsrCircuitState.Closed;

    /// <summary>Takes the probe when the circuit is open and the break has elapsed. The caller must report the probe's result.</summary>
    public bool TryBeginProbe()
    {
        if (_state != (int)SsrCircuitState.Open || timeProvider.GetUtcNow().UtcTicks < Volatile.Read(ref _openUntil))
        {
            return false;
        }

        lock (_gate)
        {
            if (_state != (int)SsrCircuitState.Open || timeProvider.GetUtcNow().UtcTicks < _openUntil)
            {
                return false;
            }

            Transition(SsrCircuitState.HalfOpen);
            return true;
        }
    }

    /// <summary>The server answered with a page (or a well-formed render error): it is up.</summary>
    public void RecordSuccess(bool probe)
    {
        if (probe)
        {
            lock (_gate)
            {
                _failures = 0;
                Transition(SsrCircuitState.Closed);
            }

            return;
        }

        if (Volatile.Read(ref _failures) != 0)
        {
            Volatile.Write(ref _failures, 0);
        }
    }

    /// <summary>A failure that affects every page (connection, timeout, unstructured HTTP error, malformed response).</summary>
    public void RecordFailure(bool probe)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (probe || ++_failures >= _threshold)
            {
                if (_state != (int)SsrCircuitState.Open)
                {
                    Volatile.Write(ref _openUntil, timeProvider.GetUtcNow().UtcTicks + _breakDuration.Ticks);
                    Transition(SsrCircuitState.Open);
                }
            }
        }
    }

    /// <summary>The render ended without telling anything about the server (client abort, overload): a probe can be retried.</summary>
    public void RecordNeutral(bool probe)
    {
        if (!probe)
        {
            return;
        }

        lock (_gate)
        {
            if (_state == (int)SsrCircuitState.HalfOpen)
            {
                Transition(SsrCircuitState.Open); // the break has elapsed, so the next render probes again
            }
        }
    }

    /// <summary>Closes the circuit, e.g. when the managed process behind it was restarted and is healthy again.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _failures = 0;
            Transition(SsrCircuitState.Closed);
        }
    }

    private void Transition(SsrCircuitState state)
    {
        if (_state == (int)state)
        {
            return;
        }

        _state = (int)state;
        transitioned?.Invoke(state);
    }
}
