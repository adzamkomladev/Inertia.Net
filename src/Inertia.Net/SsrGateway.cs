using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Thrown when server-side rendering fails and the page's SSR mode is <see cref="SsrMode.Required"/>.</summary>
public sealed class InertiaSsrException : Exception
{
    /// <summary>Creates the exception.</summary>
    public InertiaSsrException(string message, string? type = null, string? hint = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Type = type;
        Hint = hint;
    }

    /// <summary>
    /// The failure kind: the SSR server's error <c>type</c>, or <c>startup</c>, <c>connection</c>, <c>timeout</c>, <c>http</c>, <c>response</c>,
    /// <c>response-too-large</c>, <c>overloaded</c>, <c>circuit-open</c>, <c>not-ready</c>, <c>page-too-large</c>, <c>bundle-missing</c> or <c>unknown</c>.
    /// </summary>
    public string? Type { get; }

    /// <summary>The SSR server's hint on how to fix the error, when it sent one.</summary>
    public string? Hint { get; }
}

/// <summary>Names for configuring the SSR HTTP client.</summary>
public static class InertiaSsr
{
    /// <summary>
    /// The named <see cref="HttpClient"/> used for SSR requests, e.g. to add an auth header for a remote renderer with
    /// <c>services.AddHttpClient(InertiaSsr.HttpClientName).AddHttpMessageHandler(...)</c>. Its primary handler does not follow redirects
    /// and does not use a proxy; keep both when replacing it.
    /// </summary>
    public const string HttpClientName = "Inertia.Net.Ssr";
}

internal sealed record SsrError(
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("hint")] string? Hint,
    [property: JsonPropertyName("browserApi")] JsonElement? BrowserApi,
    [property: JsonPropertyName("sourceLocation")] JsonElement? SourceLocation);

/// <summary>
/// Renders pages on the Inertia SSR servers (or the Vite dev server) and falls back to client-side rendering on failure: policy, then
/// endpoint selection (circuit breakers), then render slots, then the HTTP call (docs/protocol.md §8).
/// </summary>
internal sealed partial class SsrGateway : IInertiaSsrRenderer, IDisposable
{
    internal const string HttpClientName = InertiaSsr.HttpClientName;
    private const int MaxLoggedChars = 2048;
    private static readonly TimeSpan SkipLogInterval = TimeSpan.FromSeconds(30);

    private readonly SsrOptions _ssr;
    private readonly ViteAssets _vite;
    private readonly IHttpClientFactory _httpClients;
    private readonly IHostEnvironment _environment;
    private readonly SsrNodeProcess _nodeProcess;
    private readonly TimeProvider _timeProvider;
    private readonly InertiaMetrics _metrics;
    private readonly ILogger _logger;
    private readonly bool _managed;
    private readonly SsrEndpoint[] _endpoints;
    private readonly long[] _nextSkipLog = new long[SsrFailures.Count];
    private readonly int[] _suppressedSkips = new int[SsrFailures.Count];
    private SsrEndpoint[]? _dev;
    private uint _next;

    // Bundle presence is cached like the manifest: a present bundle is never stat-ed again in production, a missing one is re-checked
    // at most once per second (every second in Development), instead of a File.Exists on every server-rendered page.
    private readonly FileCache<string> _bundles;

    public SsrGateway(
        IOptions<InertiaOptions> options,
        ViteAssets vite,
        IHttpClientFactory httpClients,
        IHostEnvironment environment,
        SsrNodeProcess nodeProcess,
        TimeProvider timeProvider,
        InertiaMetrics metrics,
        ILogger<SsrGateway> logger)
    {
        _ssr = options.Value.Ssr;
        _vite = vite;
        _httpClients = httpClients;
        _environment = environment;
        _nodeProcess = nodeProcess;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _logger = logger;
        _managed = _ssr.NodeProcess is not null;
        _bundles = new(static path => path, environment.IsDevelopment(), timeProvider);
        _endpoints = [.. EndpointUrls(_ssr).Select((url, i) => new SsrEndpoint(
            i.ToString(System.Globalization.CultureInfo.InvariantCulture), url, "/render", _managed ? i : null, _ssr, timeProvider, metrics))];
        nodeProcess.InstanceReady += instance =>
        {
            if (_managed && instance < _endpoints.Length) _endpoints[instance].Breaker.Reset();
        };
        metrics.ObserveCircuits(ObserveCircuits);
    }

    /// <summary>The production SSR server base URLs: the managed instances, or <see cref="SsrOptions.Urls"/> (default <see cref="SsrOptions.DefaultUrl"/>).</summary>
    internal static IEnumerable<string> EndpointUrls(SsrOptions ssr) => ssr.NodeProcess is { } node
        ? Enumerable.Range(0, Math.Max(1, node.Instances)).Select(i => SsrNodeProcess.InstanceUrl(node.BasePort + i))
        : ssr.Urls.Count > 0 ? ssr.Urls : [SsrOptions.DefaultUrl];

    internal IReadOnlyList<SsrEndpoint> Endpoints => _endpoints;

    internal SsrOptions Options => _ssr;

    internal bool IsManaged => _managed;

    internal bool IsInstanceReady(int instance) => _nodeProcess.IsInstanceReady(instance);

    internal bool DevServerRunning => _vite.DevServerOrigin is not null;

    /// <summary>The effective mode: the global mode is a kill switch; a response's own mode wins over the excluded paths.</summary>
    internal static SsrMode ResolveMode(SsrMode global, SsrMode? response, bool excluded) =>
        global == SsrMode.Disabled ? SsrMode.Disabled : response ?? (excluded ? SsrMode.Disabled : global);

    public async ValueTask<SsrRender?> RenderAsync(InertiaRootViewContext context)
    {
        var http = context.HttpContext;
        var mode = ResolveMode(_ssr.Mode, context.SsrMode, IsExcluded(_ssr.ExcludePaths, http.Request.Path.Value));
        if (mode == SsrMode.Disabled)
        {
            return null;
        }

        http.RequestAborted.ThrowIfCancellationRequested();
        var devOrigin = _vite.DevServerOrigin;
        var endpoints = devOrigin is null ? _endpoints : DevEndpoints(devOrigin);
        if (devOrigin is null && _ssr.BundlePath is { Length: > 0 } bundle && _bundles.Get(Path.Combine(_environment.ContentRootPath, bundle)) is null)
        {
            return Skip(mode, SsrFailure.BundleMissing, "The SSR bundle does not exist.", log: false);
        }

        if (context.PageJson.Length > _ssr.MaxPageBytes)
        {
            return Skip(mode, SsrFailure.PageTooLarge, $"The page JSON is larger than Ssr.MaxPageBytes ({_ssr.MaxPageBytes} bytes).");
        }

        using var activity = InertiaDiagnostics.ActivitySource.StartActivity("inertia.ssr");
        var started = Stopwatch.GetTimestamp();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        deadline.CancelAfter(_ssr.Timeout);
        SsrEndpoint? failed = null;
        Attempt? previous = null;
        while (true)
        {
            var endpoint = Select(endpoints, failed, managed: devOrigin is null && _managed, out var probe, out var notReady);
            if (endpoint is null)
            {
                if (previous is { } last)
                {
                    return Complete(mode, activity, started, last); // no other server can take the render
                }

                Complete(activity, started, null, notReady ? SsrFailure.NotReady : SsrFailure.CircuitOpen);
                return notReady
                    ? Skip(mode, SsrFailure.NotReady, "No managed SSR process is ready.")
                    : Skip(mode, SsrFailure.CircuitOpen, "Every SSR server's circuit is open after repeated failures.");
            }

            if (previous is { } earlier)
            {
                LogAttempt(earlier); // failing over to another server
            }

            activity?.SetTag("inertia.ssr.endpoint", endpoint.Name);
            var attempt = await AttemptAsync(endpoint, probe, context.PageJson, deadline, http.RequestAborted);
            if (attempt.Render is { } render)
            {
                Complete(activity, started, render, null);
                return render;
            }

            // A refused connection received nothing, so one other server can take the render within the same deadline.
            if (attempt is { Failure: SsrFailure.Connection, Retryable: true } && previous is null && endpoints.Length > 1 && !deadline.IsCancellationRequested)
            {
                failed = endpoint;
                previous = attempt;
                continue;
            }

            return Complete(mode, activity, started, attempt);
        }
    }

    private SsrRender? Complete(SsrMode mode, Activity? activity, long started, Attempt attempt)
    {
        Complete(activity, started, null, attempt.Failure);
        if (attempt.Failure == SsrFailure.Overloaded)
        {
            return Skip(mode, SsrFailure.Overloaded, attempt.Message);
        }

        LogAttempt(attempt);
        return Fail(mode, attempt.Failure, attempt.Type, attempt.Message, attempt.Hint, attempt.Inner);
    }

    private void Complete(Activity? activity, long started, SsrRender? render, SsrFailure? failure)
    {
        var outcome = render is not null ? "rendered" : "fallback";
        if (_metrics.SsrDurationEnabled)
        {
            _metrics.RecordSsr(Stopwatch.GetElapsedTime(started).TotalSeconds, outcome);
        }

        if (render is not null)
        {
            _metrics.RecordOutcome(outcome, "ok");
        }

        if (activity is not null)
        {
            activity.SetTag("inertia.ssr.outcome", outcome);
            if (failure is { } reason)
            {
                activity.SetTag("inertia.ssr.reason", reason.Name());
            }
        }
    }

    // Probes first (an open circuit whose break elapsed gets one render even while other servers are healthy), then the closed server
    // with the fewest renders in flight, starting at a rotating index so ties spread evenly.
    private SsrEndpoint? Select(SsrEndpoint[] endpoints, SsrEndpoint? exclude, bool managed, out bool probe, out bool notReady)
    {
        probe = false;
        notReady = false;
        var count = endpoints.Length;
        var start = count == 1 ? 0 : (int)(Interlocked.Increment(ref _next) % (uint)count);
        var anyReady = !managed;
        SsrEndpoint? best = null;
        for (var i = 0; i < count; i++)
        {
            var endpoint = endpoints[(start + i) % count];
            if (ReferenceEquals(endpoint, exclude) || (managed && endpoint.Instance is { } instance && !_nodeProcess.IsInstanceReady(instance)))
            {
                continue;
            }

            anyReady = true;
            if (endpoint.Breaker.TryBeginProbe())
            {
                probe = true;
                return endpoint;
            }

            if (endpoint.Breaker.IsClosed && (best is null || Volatile.Read(ref endpoint.InFlight) < Volatile.Read(ref best.InFlight)))
            {
                best = endpoint;
            }
        }

        notReady = !anyReady;
        return best;
    }

    private async ValueTask<Attempt> AttemptAsync(SsrEndpoint endpoint, bool probe, ReadOnlyMemory<byte> pageJson, CancellationTokenSource deadline, CancellationToken aborted)
    {
        Interlocked.Increment(ref endpoint.InFlight);
        RateLimitLease? lease = null;
        try
        {
            if (endpoint.Limiter is { } limiter)
            {
                lease = limiter.AttemptAcquire();
                if (!lease.IsAcquired)
                {
                    lease.Dispose();
                    lease = await WaitForSlotAsync(limiter, deadline.Token, aborted);
                    if (lease is null)
                    {
                        endpoint.Breaker.RecordNeutral(probe);
                        return new Attempt(SsrFailure.Overloaded, "overloaded", $"No free SSR render slot on {endpoint.DisplayUrl}.");
                    }
                }
            }

            _metrics.SsrStarted();
            Attempt attempt;
            try
            {
                attempt = await SendAsync(endpoint, pageJson, deadline, aborted);
            }
            finally
            {
                _metrics.SsrEnded();
            }

            if (attempt.Render is not null || attempt.Failure.ServerResponded())
            {
                endpoint.Breaker.RecordSuccess(probe);
            }
            else if (attempt.Failure.IsSystemic())
            {
                endpoint.Breaker.RecordFailure(probe);
            }
            else
            {
                endpoint.Breaker.RecordNeutral(probe);
            }

            return attempt;
        }
        catch
        {
            endpoint.Breaker.RecordNeutral(probe); // the client aborted
            throw;
        }
        finally
        {
            lease?.Dispose();
            Interlocked.Decrement(ref endpoint.InFlight);
        }
    }

    // Waits for a render slot until the queue timeout or the render deadline; null when none was free in time (or the queue is full).
    private async ValueTask<RateLimitLease?> WaitForSlotAsync(ConcurrencyLimiter limiter, CancellationToken deadline, CancellationToken aborted)
    {
        var queued = Stopwatch.GetTimestamp();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline);
        wait.CancelAfter(_ssr.Concurrency.QueueTimeout);
        RateLimitLease? lease = null;
        try
        {
            lease = await limiter.AcquireAsync(1, wait.Token);
        }
        catch (OperationCanceledException) when (!aborted.IsCancellationRequested)
        {
        }
        finally
        {
            if (_metrics.QueueDurationEnabled)
            {
                _metrics.RecordQueue(Stopwatch.GetElapsedTime(queued).TotalSeconds);
            }
        }

        if (lease is { IsAcquired: true })
        {
            return lease;
        }

        lease?.Dispose();
        return null;
    }

    private async ValueTask<Attempt> SendAsync(SsrEndpoint endpoint, ReadOnlyMemory<byte> pageJson, CancellationTokenSource deadline, CancellationToken aborted)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.RenderUrl) { Content = new ReadOnlyMemoryContent(pageJson) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            HttpResponseMessage response;
            try
            {
                response = await _httpClients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            }
            catch (HttpRequestException ex)
            {
                // Nothing was received, so another server can take the render.
                return new Attempt(SsrFailure.Connection, "connection", $"Could not reach the SSR server at {endpoint.DisplayUrl}: {ex.Message}", Inner: ex, Retryable: true);
            }

            using (response)
            {
                var length = response.Content.Headers.ContentLength;
                if (length > _ssr.MaxResponseBytes) throw new SsrResponseTooLargeException(_ssr.MaxResponseBytes);
                using var stream = new SsrResponseStream(await response.Content.ReadAsStreamAsync(deadline.Token), _ssr.MaxResponseBytes);
                if (!response.IsSuccessStatusCode)
                {
                    return await FailedResponseAsync((int)response.StatusCode, stream, deadline.Token);
                }

                // The bounded body goes into a pooled buffer and head/body are unescaped once into a pooled UTF-8 array that the root view
                // writes as-is: no UTF-16 strings (twice the size), no per-render allocation of the body and no re-encoding on the way out.
                using var buffer = new PooledBufferWriter(InitialBufferSize(length));
                var known = length is not null;
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.GetMemory(known ? 1 : 4096), deadline.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    buffer.Advance(read);
                }

                _metrics.RecordResponseSize(buffer.WrittenMemory.Length);
                return ParseRender(buffer.WrittenMemory.Span) is { } page
                    ? page.Body.Length > 0 ? new Attempt(page) : Invalid(page)
                    : new Attempt(SsrFailure.InvalidResponse, "response", "The SSR server returned an empty or invalid response.");
            }
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            throw;
        }
        catch (SsrResponseTooLargeException ex)
        {
            return new Attempt(SsrFailure.ResponseTooLarge, "response-too-large", ex.Message, Inner: ex);
        }
        catch (OperationCanceledException ex)
        {
            return new Attempt(SsrFailure.Timeout, "timeout", $"The SSR server at {endpoint.DisplayUrl} did not respond within {_ssr.Timeout}.", Inner: ex);
        }
        catch (HttpRequestException ex)
        {
            return new Attempt(SsrFailure.Connection, "connection", $"The connection to the SSR server at {endpoint.DisplayUrl} failed: {ex.Message}", Inner: ex);
        }
        catch (Exception ex) when (ex is not InertiaSsrException)
        {
            // e.g. a malformed URL; SSR problems never take the page down unless the page requires SSR.
            return new Attempt(SsrFailure.Unknown, "unknown", $"SSR request to {endpoint.DisplayUrl} failed: {ex.Message}", Inner: ex);
        }

        static Attempt Invalid(SsrRender page)
        {
            page.Dispose();
            return new Attempt(SsrFailure.InvalidResponse, "response", "The SSR server returned an empty or invalid response.");
        }
    }

    // Exact for a declared length (plus one byte, so the final zero-length read does not grow the buffer); 16 KiB otherwise.
    private static int InitialBufferSize(long? contentLength) =>
        contentLength is { } length ? (int)Math.Clamp(length + 1, 1, 64 * 1024 * 1024) : 16 * 1024;

    /// <summary>
    /// Reads <c>{"head": string[], "body": string}</c> into UTF-8 HTML in one pooled array: head entries joined with <c>\n</c>, then the
    /// body. Unescaping only shrinks, so the array never needs more than the JSON's length. Null when the JSON is malformed, not an object,
    /// or not valid UTF-8. The caller disposes the result to return the array.
    /// </summary>
    internal static SsrRender? ParseRender(ReadOnlySpan<byte> json)
    {
        var output = ArrayPool<byte>.Shared.Rent(Math.Max(json.Length, 1));
        try
        {
            var reader = new Utf8JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            var written = 0;
            Range head = default, body = default;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("body"u8))
                {
                    reader.Read();
                    body = reader.TokenType == JsonTokenType.String ? Unescape(ref reader, output, ref written) : default;
                    reader.Skip();
                }
                else if (reader.ValueTextEquals("head"u8))
                {
                    reader.Read();
                    head = reader.TokenType == JsonTokenType.StartArray ? ReadHead(ref reader, output, ref written) : default;
                    reader.Skip();
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (!System.Text.Unicode.Utf8.IsValid(output.AsSpan(head)) || !System.Text.Unicode.Utf8.IsValid(output.AsSpan(body)))
            {
                return null;
            }

            var render = new SsrRender(output, head, body);
            output = null;
            return render;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) // InvalidOperationException: an unpaired \uD800 escape
        {
            return null;
        }
        finally
        {
            if (output is not null)
            {
                ArrayPool<byte>.Shared.Return(output);
            }
        }
    }

    private static Range Unescape(ref Utf8JsonReader reader, byte[] output, ref int written)
    {
        var start = written;
        written += reader.ValueIsEscaped ? reader.CopyString(output.AsSpan(written)) : Copy(reader.ValueSpan, output.AsSpan(written));
        return start..written;

        static int Copy(ReadOnlySpan<byte> value, Span<byte> destination)
        {
            value.CopyTo(destination);
            return value.Length;
        }
    }

    // Each entry's quotes and comma in the JSON leave room for the joining newline.
    private static Range ReadHead(ref Utf8JsonReader reader, byte[] output, ref int written)
    {
        var start = written;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                continue;
            }

            if (written > start)
            {
                output[written++] = (byte)'\n';
            }

            Unescape(ref reader, output, ref written);
        }

        return start..written;
    }

    private SsrEndpoint[] DevEndpoints(string origin)
    {
        if (_dev is { } dev && dev[0].RenderUrl.StartsWith(origin, StringComparison.Ordinal) && dev[0].RenderUrl.Length == origin.Length + "/__inertia_ssr".Length)
        {
            return dev;
        }

        dev = [new SsrEndpoint("dev", origin, "/__inertia_ssr", null, _ssr, _timeProvider, _metrics)];
        _dev = dev;
        return dev;
    }

    private IEnumerable<System.Diagnostics.Metrics.Measurement<int>> ObserveCircuits()
    {
        foreach (var endpoint in _endpoints)
        {
            yield return new((int)endpoint.Breaker.State, new KeyValuePair<string, object?>("inertia.ssr.endpoint", endpoint.Name));
        }

        if (_dev is { } dev)
        {
            yield return new((int)dev[0].Breaker.State, new KeyValuePair<string, object?>("inertia.ssr.endpoint", "dev"));
        }
    }

    /// <summary>Matches the request path against exact entries and entries ending in <c>*</c> (prefix match).</summary>
    internal static bool IsExcluded(IList<string> patterns, string? path)
    {
        path ??= "/";
        foreach (var pattern in patterns)
        {
            if (pattern.EndsWith('*') ? path.StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal) : path == pattern)
            {
                return true;
            }
        }

        return false;
    }

    private static async ValueTask<Attempt> FailedResponseAsync(int status, Stream body, CancellationToken cancellationToken)
    {
        SsrError? error = null;
        try
        {
            error = await JsonSerializer.DeserializeAsync(body, InertiaJsonContext.Default.SsrError, cancellationToken);
        }
        catch (JsonException)
        {
            await body.CopyToAsync(Stream.Null, cancellationToken); // drain within the size limit
        }

        // A structured error is the renderer reporting one page's problem (a component threw); anything else is the server or a proxy.
        return error is { Error: not null } or { Type: not null }
            ? new Attempt(SsrFailure.RenderError, error.Type ?? "render", error.Error ?? $"The SSR server responded with HTTP {status}.", error.Hint, Details: error)
            : new Attempt(SsrFailure.Http, "http", $"The SSR server responded with HTTP {status}.");
    }

    private SsrRender? Skip(SsrMode mode, SsrFailure failure, string message, bool log = true)
    {
        _metrics.RecordOutcome(mode == SsrMode.Required ? "error" : "fallback", failure.Name());
        if (mode == SsrMode.Required)
        {
            throw new InertiaSsrException($"Inertia SSR failed ({failure.Name()}): {message}", failure.Name());
        }

        if (log && ShouldLogSkip(failure, out var suppressed))
        {
            LogSkipped(_logger, failure.Name(), message, suppressed);
        }

        return null;
    }

    private SsrRender? Fail(SsrMode mode, SsrFailure failure, string type, string message, string? hint, Exception? inner)
    {
        _metrics.RecordOutcome(mode == SsrMode.Required ? "error" : "fallback", failure.Name());
        return mode == SsrMode.Required ? throw new InertiaSsrException($"Inertia SSR failed ({type}): {message}", type, hint, inner) : null;
    }

    private void LogAttempt(Attempt attempt) =>
        LogFailure(_logger, attempt.Inner, attempt.Type, Truncate(attempt.Message)!, Truncate(attempt.Hint),
            Truncate(attempt.Details?.BrowserApi?.ToString()), Truncate(attempt.Details?.SourceLocation?.ToString()));

    // SSR error text comes from the renderer and can be large (stack traces, serialized props): logs keep the first 2 KB.
    private static string? Truncate(string? text) => text is { Length: > MaxLoggedChars } ? string.Concat(text.AsSpan(0, MaxLoggedChars), "…") : text;

    // Repeating skip reasons (no process ready, open circuit, overload) are logged at most once per 30 seconds each, with a count.
    private bool ShouldLogSkip(SsrFailure failure, out int suppressed)
    {
        var index = (int)failure;
        var now = _timeProvider.GetTimestamp();
        var next = Volatile.Read(ref _nextSkipLog[index]);
        if (next != 0 && now < next)
        {
            Interlocked.Increment(ref _suppressedSkips[index]);
            suppressed = 0;
            return false;
        }

        var due = now + (long)(SkipLogInterval.TotalSeconds * _timeProvider.TimestampFrequency);
        if (Interlocked.CompareExchange(ref _nextSkipLog[index], due, next) != next)
        {
            Interlocked.Increment(ref _suppressedSkips[index]);
            suppressed = 0;
            return false;
        }

        suppressed = Interlocked.Exchange(ref _suppressedSkips[index], 0);
        return true;
    }

    public void Dispose()
    {
        foreach (var endpoint in _endpoints)
        {
            endpoint.Dispose();
        }

        _dev?[0].Dispose();
    }

    private readonly record struct Attempt(
        SsrFailure Failure,
        string Type,
        string Message,
        string? Hint = null,
        Exception? Inner = null,
        SsrError? Details = null,
        bool Retryable = false)
    {
        public Attempt(SsrRender render)
            : this(default, "", "")
        {
            Render = render;
        }

        public SsrRender? Render { get; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inertia SSR failed ({Type}), falling back to client-side rendering: {Error} Hint: {Hint} Browser API: {BrowserApi} Source: {SourceLocation}")]
    private static partial void LogFailure(ILogger logger, Exception? exception, string type, string error, string? hint, string? browserApi, string? sourceLocation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inertia SSR skipped ({Reason}), rendering on the client: {Detail} ({Suppressed} similar skips not logged since the last message)")]
    private static partial void LogSkipped(ILogger logger, string reason, string detail, int suppressed);
}
