using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Inertia.Net;

/// <summary>Names for collecting Inertia.Net telemetry, e.g. <c>.WithMetrics(m =&gt; m.AddMeter(InertiaDiagnostics.MeterName))</c> in OpenTelemetry.</summary>
public static class InertiaDiagnostics
{
    /// <summary>The <see cref="Meter"/> name: <c>inertia.page.*</c>, <c>inertia.props.*</c> and <c>inertia.ssr.*</c> instruments.</summary>
    public const string MeterName = "Inertia.Net";

    /// <summary>The <see cref="System.Diagnostics.ActivitySource"/> name: <c>inertia.render</c> and <c>inertia.ssr</c> activities.</summary>
    public const string ActivitySourceName = "Inertia.Net";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}

/// <summary>
/// The Inertia.Net instruments. Every tag value is bounded (no URLs, component names or user data); each recording is skipped
/// unless a listener enabled the instrument.
/// </summary>
internal sealed class InertiaMetrics
{
    private readonly Histogram<double> _pageDuration;
    private readonly Histogram<long> _pageSize;
    private readonly Histogram<double> _ssrDuration;
    private readonly Histogram<double> _ssrQueueDuration;
    private readonly UpDownCounter<long> _ssrActive;
    private readonly Counter<long> _ssrOutcomes;
    private readonly Histogram<long> _ssrResponseSize;
    private readonly Counter<long> _circuitTransitions;
    private readonly Counter<long> _processRestarts;
    private readonly Counter<long> _rescued;
    private Func<IEnumerable<Measurement<int>>>? _circuitStates;

    public InertiaMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(InertiaDiagnostics.MeterName);
        _pageDuration = meter.CreateHistogram<double>("inertia.page.duration", "s", "Time to compose and write an Inertia response.");
        _pageSize = meter.CreateHistogram<long>("inertia.page.size", "By", "Size of the page JSON.");
        _ssrDuration = meter.CreateHistogram<double>("inertia.ssr.duration", "s", "Time spent on server-side rendering, including the wait for a slot.");
        _ssrQueueDuration = meter.CreateHistogram<double>("inertia.ssr.queue.duration", "s", "Time a render waited for a free slot.");
        _ssrActive = meter.CreateUpDownCounter<long>("inertia.ssr.active", "{render}", "Renders in flight to SSR servers.");
        _ssrOutcomes = meter.CreateCounter<long>("inertia.ssr.outcomes", "{render}", "Server-side render outcomes by reason.");
        _ssrResponseSize = meter.CreateHistogram<long>("inertia.ssr.response.size", "By", "Size of SSR server responses.");
        _circuitTransitions = meter.CreateCounter<long>("inertia.ssr.circuit.transitions", "{transition}", "SSR circuit state changes.");
        _processRestarts = meter.CreateCounter<long>("inertia.ssr.process.restarts", "{restart}", "Managed Node SSR process restarts.");
        _rescued = meter.CreateCounter<long>("inertia.props.rescued", "{prop}", "Props whose loader failed and that were rescued (left out and listed in rescuedProps).");
        meter.CreateObservableGauge("inertia.ssr.circuit.state", () => _circuitStates?.Invoke() ?? [], "{state}", "SSR circuit state per server: 0 closed, 1 half-open, 2 open.");
    }

    /// <summary>Supplies the per-server circuit states for the observable gauge.</summary>
    public void ObserveCircuits(Func<IEnumerable<Measurement<int>>> states) => _circuitStates = states;

    public bool PageDurationEnabled => _pageDuration.Enabled;

    // Boxed once: a bool tag value would otherwise be boxed on every recorded render.
    private static readonly object True = true, False = false;

    public void RecordPage(double seconds, long bytes, bool json, bool partial)
    {
        var response = new KeyValuePair<string, object?>("inertia.response", json ? "json" : "html");
        if (_pageDuration.Enabled)
        {
            _pageDuration.Record(seconds, response, new KeyValuePair<string, object?>("inertia.partial", partial ? True : False));
        }

        if (_pageSize.Enabled)
        {
            _pageSize.Record(bytes, response);
        }
    }

    public bool SsrDurationEnabled => _ssrDuration.Enabled;

    public void RecordSsr(double seconds, string outcome)
    {
        if (_ssrDuration.Enabled)
        {
            _ssrDuration.Record(seconds, new KeyValuePair<string, object?>("inertia.ssr.outcome", outcome));
        }
    }

    public void RecordOutcome(string outcome, string reason)
    {
        if (_ssrOutcomes.Enabled)
        {
            _ssrOutcomes.Add(1, new KeyValuePair<string, object?>("inertia.ssr.outcome", outcome), new KeyValuePair<string, object?>("inertia.ssr.reason", reason));
        }
    }

    public bool QueueDurationEnabled => _ssrQueueDuration.Enabled;

    public void RecordQueue(double seconds) => _ssrQueueDuration.Record(seconds);

    public void SsrStarted() => _ssrActive.Add(1);

    public void SsrEnded() => _ssrActive.Add(-1);

    public void RecordResponseSize(long bytes)
    {
        if (_ssrResponseSize.Enabled)
        {
            _ssrResponseSize.Record(bytes);
        }
    }

    public void RecordTransition(string endpoint, SsrCircuitState state)
    {
        if (_circuitTransitions.Enabled)
        {
            _circuitTransitions.Add(1, new KeyValuePair<string, object?>("inertia.ssr.endpoint", endpoint), new KeyValuePair<string, object?>("to", StateName(state)));
        }
    }

    public void RecordRestart(string cause)
    {
        if (_processRestarts.Enabled)
        {
            _processRestarts.Add(1, new KeyValuePair<string, object?>("cause", cause));
        }
    }

    public void RecordRescued()
    {
        if (_rescued.Enabled)
        {
            _rescued.Add(1);
        }
    }

    internal static string StateName(SsrCircuitState state) => state switch
    {
        SsrCircuitState.Closed => "closed",
        SsrCircuitState.HalfOpen => "half-open",
        _ => "open",
    };
}
