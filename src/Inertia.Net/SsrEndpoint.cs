using System.Threading.RateLimiting;

namespace Inertia.Net;

/// <summary>Why a server-side render did not produce a page. Maps to <see cref="InertiaSsrException.Type"/>.</summary>
internal enum SsrFailure
{
    Connection,
    Timeout,
    Http,
    RenderError,
    InvalidResponse,
    ResponseTooLarge,
    Unknown,
    Overloaded,
    CircuitOpen,
    NotReady,
    PageTooLarge,
    BundleMissing,
}

internal static class SsrFailures
{
    // Failures that say the server is unusable for every page; only these count toward the circuit breaker.
    public static bool IsSystemic(this SsrFailure failure) => failure is SsrFailure.Connection or SsrFailure.Timeout or SsrFailure.Http
        or SsrFailure.InvalidResponse or SsrFailure.Unknown;

    // The server answered: a rendered page, a structured render error or an oversized body all prove it is up.
    public static bool ServerResponded(this SsrFailure failure) => failure is SsrFailure.RenderError or SsrFailure.ResponseTooLarge;

    public static string Name(this SsrFailure failure) => failure switch
    {
        SsrFailure.Connection => "connection",
        SsrFailure.Timeout => "timeout",
        SsrFailure.Http => "http",
        SsrFailure.RenderError => "render",
        SsrFailure.InvalidResponse => "response",
        SsrFailure.ResponseTooLarge => "response-too-large",
        SsrFailure.Overloaded => "overloaded",
        SsrFailure.CircuitOpen => "circuit-open",
        SsrFailure.NotReady => "not-ready",
        SsrFailure.PageTooLarge => "page-too-large",
        SsrFailure.BundleMissing => "bundle-missing",
        _ => "unknown",
    };

    public const int Count = (int)SsrFailure.BundleMissing + 1;
}

/// <summary>One SSR server: its render URL, circuit breaker, render slots and renders in flight.</summary>
internal sealed class SsrEndpoint : IDisposable
{
    public SsrEndpoint(string name, string baseUrl, string renderPath, int? instance, SsrOptions ssr, TimeProvider timeProvider, InertiaMetrics metrics)
    {
        Name = name;
        Instance = instance;
        RenderUrl = $"{baseUrl.TrimEnd('/')}{renderPath}";
        DisplayUrl = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0
            ? new UriBuilder(uri) { UserName = "", Password = "" }.Uri.ToString()
            : baseUrl;
        Breaker = new SsrCircuitBreaker(ssr.CircuitBreaker, timeProvider, state => metrics.RecordTransition(name, state));
        Limiter = ssr.Concurrency.MaxActive > 0
            ? new ConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = ssr.Concurrency.MaxActive,
                QueueLimit = ssr.Concurrency.MaxQueued,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            })
            : null;
    }

    /// <summary>The metrics name: the index in <see cref="SsrOptions.Urls"/> (or of the managed instance), or <c>dev</c>.</summary>
    public string Name { get; }

    /// <summary>The managed instance behind this endpoint, if any.</summary>
    public int? Instance { get; }

    public string RenderUrl { get; }

    /// <summary>The base URL without user info, for logs and exception messages.</summary>
    public string DisplayUrl { get; }

    public SsrCircuitBreaker Breaker { get; }

    /// <summary>Render slots; null when <see cref="SsrConcurrencyOptions.MaxActive"/> is 0.</summary>
    public ConcurrencyLimiter? Limiter { get; }

    /// <summary>Renders queued or running on this server; used to pick the least busy one.</summary>
    public int InFlight;

    public void Dispose() => Limiter?.Dispose();
}
