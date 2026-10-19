using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

/// <summary>SSR modes, circuit breakers, render slots, several servers, logging, metrics and health (docs/protocol.md §8).</summary>
public sealed class SsrResilienceTests
{
    private const string Rendered = """{"head":["<title>SSR</title>"],"body":"<div data-server-rendered=\"true\" id=\"app\"></div>"}""";

    private sealed class Servers(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public ConcurrentQueue<Uri> Requests { get; } = new();

        public int To(string host) => Requests.Count(u => u.Host == host);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!);
            return respond(request, cancellationToken);
        }
    }

    private static Task<HttpResponseMessage> Ok() => Task.FromResult(Json(HttpStatusCode.OK, Rendered));

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static Servers Refused() => new((_, _) => throw new HttpRequestException("Connection refused"));

    private static Harness Create(Servers servers, Action<SsrOptions>? configure = null, TimeProvider? clock = null, Action<IServiceCollection>? services = null) =>
        new(
            o =>
            {
                o.Ssr.Mode = SsrMode.Preferred;
                configure?.Invoke(o.Ssr);
            },
            timeProvider: clock,
            configureServices: s =>
            {
                s.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => servers);
                services?.Invoke(s);
            });

    private static Task<string> GetAsync(Harness harness, string url = "/", Func<InertiaResult, InertiaResult>? result = null) =>
        Harness.ExecuteAsync(harness.Context(url), (result ?? (r => r))(Render("Home", new InertiaProps { ["a"] = 1 })));

    private static bool IsServerRendered(string html) => html.Contains("data-server-rendered", StringComparison.Ordinal);

    private static IEnumerable<string> Warnings(Harness harness) => harness.Logs.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

    [Fact]
    public void Breaker_opens_after_consecutive_failures_and_lets_one_probe_through_after_the_break()
    {
        var clock = new FakeTimeProvider();
        var transitions = new List<SsrCircuitState>();
        var breaker = new SsrCircuitBreaker(new SsrCircuitBreakerOptions { FailureThreshold = 3, BreakDuration = TimeSpan.FromSeconds(10) }, clock, transitions.Add);

        breaker.RecordFailure(probe: false);
        breaker.RecordFailure(probe: false);
        breaker.RecordSuccess(probe: false); // a success resets the count
        breaker.RecordFailure(probe: false);
        breaker.RecordFailure(probe: false);
        Assert.True(breaker.IsClosed);
        breaker.RecordFailure(probe: false);
        Assert.Equal(SsrCircuitState.Open, breaker.State);
        Assert.False(breaker.TryBeginProbe());

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(breaker.TryBeginProbe());
        Assert.False(breaker.TryBeginProbe()); // one probe at a time
        breaker.RecordFailure(probe: true);
        Assert.False(breaker.TryBeginProbe()); // a failed probe starts a new break

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(breaker.TryBeginProbe());
        breaker.RecordNeutral(probe: true); // an aborted probe says nothing about the server: the next render probes
        Assert.True(breaker.TryBeginProbe());
        breaker.RecordSuccess(probe: true);
        Assert.True(breaker.IsClosed);

        Assert.Equal(
            [SsrCircuitState.Open, SsrCircuitState.HalfOpen, SsrCircuitState.Open, SsrCircuitState.HalfOpen, SsrCircuitState.Open, SsrCircuitState.HalfOpen, SsrCircuitState.Closed],
            transitions);
    }

    [Fact]
    public void Disabled_breaker_never_opens_and_reset_closes_an_open_one()
    {
        var clock = new FakeTimeProvider();
        var disabled = new SsrCircuitBreaker(new SsrCircuitBreakerOptions { Enabled = false, FailureThreshold = 1 }, clock);
        for (var i = 0; i < 10; i++)
        {
            disabled.RecordFailure(probe: false);
        }

        Assert.True(disabled.IsClosed);

        var breaker = new SsrCircuitBreaker(new SsrCircuitBreakerOptions { FailureThreshold = 1 }, clock);
        breaker.RecordFailure(probe: false);
        Assert.Equal(SsrCircuitState.Open, breaker.State);
        breaker.Reset();
        Assert.True(breaker.IsClosed);
    }

    [Fact]
    public async Task Repeated_connection_failures_open_the_circuit_until_a_probe_succeeds()
    {
        var clock = new FakeTimeProvider();
        var down = true;
        var servers = new Servers((_, _) => down ? throw new HttpRequestException("Connection refused") : Ok());
        var harness = Create(servers, s => s.CircuitBreaker.FailureThreshold = 2, clock);

        for (var i = 0; i < 4; i++)
        {
            Assert.False(IsServerRendered(await GetAsync(harness)));
        }

        Assert.Equal(2, servers.Requests.Count); // the open circuit skips the server
        Assert.Contains(Warnings(harness), m => m.Contains("skipped (circuit-open)", StringComparison.Ordinal));

        down = false;
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(IsServerRendered(await GetAsync(harness))); // the probe
        Assert.True(IsServerRendered(await GetAsync(harness)));
        Assert.Equal(4, servers.Requests.Count);
    }

    [Fact]
    public async Task Render_errors_and_oversized_responses_never_open_the_circuit()
    {
        var oversized = false;
        var servers = new Servers((_, _) => Task.FromResult(oversized
            ? Json(HttpStatusCode.OK, Rendered.Replace("<div", new string(' ', 200) + "<div", StringComparison.Ordinal))
            : Json(HttpStatusCode.InternalServerError, """{"error":"window is not defined","type":"browser-api"}""")));
        var harness = Create(servers, s =>
        {
            s.CircuitBreaker.FailureThreshold = 1;
            s.MaxResponseBytes = 150;
        });

        for (var i = 0; i < 6; i++)
        {
            oversized = i % 2 == 1;
            Assert.False(IsServerRendered(await GetAsync(harness)));
        }

        Assert.Equal(6, servers.Requests.Count);
        Assert.Equal(SsrCircuitState.Closed, harness.Services.GetRequiredService<SsrGateway>().Endpoints[0].Breaker.State);
    }

    [Fact]
    public async Task Renders_spread_across_servers_and_fail_over_when_one_refuses_connections()
    {
        var aDown = false;
        var servers = new Servers((request, _) => aDown && request.RequestUri!.Host == "a.test" ? throw new HttpRequestException("Connection refused") : Ok());
        var harness = Create(servers, s =>
        {
            s.Urls.Add("http://a.test");
            s.Urls.Add("http://b.test");
        });

        for (var i = 0; i < 4; i++)
        {
            Assert.True(IsServerRendered(await GetAsync(harness)));
        }

        Assert.Equal(2, servers.To("a.test"));
        Assert.Equal(2, servers.To("b.test"));

        aDown = true;
        for (var i = 0; i < 20; i++)
        {
            Assert.True(IsServerRendered(await GetAsync(harness))); // every refused render is retried on b
        }

        Assert.Equal(2 + 5, servers.To("a.test")); // the default threshold of 5 opens a's circuit
        Assert.Equal(2 + 20, servers.To("b.test"));
        Assert.Equal(5, Warnings(harness).Count(m => m.Contains("(connection)", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Timeouts_are_not_retried_on_another_server()
    {
        var servers = new Servers(async (request, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null!;
        });
        var harness = Create(servers, s =>
        {
            s.Urls.Add("http://a.test");
            s.Urls.Add("http://b.test");
            s.Timeout = TimeSpan.FromMilliseconds(50);
        });

        Assert.False(IsServerRendered(await GetAsync(harness)));
        Assert.Single(servers.Requests);
    }

    [Fact]
    public async Task Full_render_slots_fall_back_at_once_without_opening_the_circuit()
    {
        var release = new TaskCompletionSource();
        var servers = new Servers(async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return await Ok();
        });
        var harness = Create(servers, s =>
        {
            s.Concurrency.MaxActive = 1;
            s.Concurrency.MaxQueued = 0;
            s.CircuitBreaker.FailureThreshold = 1;
        });

        var first = GetAsync(harness);
        await WaitUntilAsync(() => !servers.Requests.IsEmpty);
        Assert.False(IsServerRendered(await GetAsync(harness)));
        Assert.Contains(Warnings(harness), m => m.Contains("skipped (overloaded)", StringComparison.Ordinal));

        release.SetResult();
        Assert.True(IsServerRendered(await first));
        Assert.True(IsServerRendered(await GetAsync(harness))); // the slot was returned and the circuit stayed closed
        Assert.Equal(2, servers.Requests.Count);
    }

    [Fact]
    public async Task Queued_render_falls_back_after_the_queue_timeout()
    {
        var release = new TaskCompletionSource();
        var servers = new Servers(async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return await Ok();
        });
        var harness = Create(servers, s =>
        {
            s.Concurrency.MaxActive = 1;
            s.Concurrency.MaxQueued = 1;
            s.Concurrency.QueueTimeout = TimeSpan.FromMilliseconds(50);
        });

        var first = GetAsync(harness);
        await WaitUntilAsync(() => !servers.Requests.IsEmpty);
        Assert.False(IsServerRendered(await GetAsync(harness)));
        release.SetResult();
        Assert.True(IsServerRendered(await first));
        Assert.Single(servers.Requests);
    }

    [Fact]
    public async Task Failed_and_aborted_renders_return_their_slots()
    {
        var mode = "error";
        var servers = new Servers(async (_, ct) =>
        {
            switch (mode)
            {
                case "hang":
                    await Task.Delay(Timeout.Infinite, ct);
                    return null!;
                case "error":
                    return Json(HttpStatusCode.InternalServerError, """{"error":"boom"}""");
                default:
                    return await Ok();
            }
        });
        var harness = Create(servers, s =>
        {
            s.Concurrency.MaxActive = 1;
            s.Concurrency.MaxQueued = 0;
            s.Timeout = TimeSpan.FromMilliseconds(100);
        });

        for (var i = 0; i < 3; i++)
        {
            Assert.False(IsServerRendered(await GetAsync(harness)));
        }

        mode = "hang";
        Assert.False(IsServerRendered(await GetAsync(harness))); // timeout
        using var aborted = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var context = harness.Context();
        context.RequestAborted = aborted.Token;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Harness.ExecuteAsync(context, Render("Home")));

        mode = "ok";
        Assert.True(IsServerRendered(await GetAsync(harness)));
        Assert.Equal(0, harness.Services.GetRequiredService<SsrGateway>().Endpoints[0].InFlight);
    }

    [Theory]
    [InlineData(SsrMode.Disabled, null, false, SsrMode.Disabled)]
    [InlineData(SsrMode.Disabled, SsrMode.Required, false, SsrMode.Disabled)] // the global mode is a kill switch
    [InlineData(SsrMode.Disabled, SsrMode.Preferred, true, SsrMode.Disabled)]
    [InlineData(SsrMode.Preferred, null, false, SsrMode.Preferred)]
    [InlineData(SsrMode.Preferred, null, true, SsrMode.Disabled)]
    [InlineData(SsrMode.Preferred, SsrMode.Disabled, false, SsrMode.Disabled)]
    [InlineData(SsrMode.Preferred, SsrMode.Required, false, SsrMode.Required)]
    [InlineData(SsrMode.Preferred, SsrMode.Preferred, true, SsrMode.Preferred)] // the response wins over the excluded paths
    [InlineData(SsrMode.Required, null, false, SsrMode.Required)]
    [InlineData(SsrMode.Required, null, true, SsrMode.Disabled)]
    [InlineData(SsrMode.Required, SsrMode.Preferred, false, SsrMode.Preferred)]
    [InlineData(SsrMode.Required, SsrMode.Disabled, false, SsrMode.Disabled)]
    public void Ssr_mode_resolution(SsrMode global, SsrMode? response, bool excluded, SsrMode expected) =>
        Assert.Equal(expected, SsrGateway.ResolveMode(global, response, excluded));

    [Fact]
    public async Task Responses_choose_their_own_ssr_mode()
    {
        var servers = Refused();
        var harness = Create(servers, s => s.ExcludePaths.Add("/admin/*"));

        Assert.False(IsServerRendered(await GetAsync(harness, result: r => r.WithoutSsr())));
        Assert.Empty(servers.Requests);

        await GetAsync(harness, "/admin/users", r => r.WithSsr(SsrMode.Preferred));
        Assert.Single(servers.Requests);

        var context = harness.Context();
        var error = await Assert.ThrowsAsync<InertiaSsrException>(() => Render("Home").WithSsr(SsrMode.Required).ExecuteAsync(context));
        Assert.Equal("connection", error.Type);
        Assert.Equal(0, context.Response.Body.Length); // thrown before the first byte, so error pages and retries still work

        var disabled = Create(servers, s => s.Mode = SsrMode.Disabled);
        await GetAsync(disabled, result: r => r.WithSsr(SsrMode.Required));
        Assert.Equal(2, servers.Requests.Count);
    }

    [Fact]
    public async Task Pages_over_the_size_limit_render_on_the_client_without_a_call()
    {
        var servers = new Servers((_, _) => Ok());
        var harness = Create(servers, s => s.MaxPageBytes = 10);

        Assert.False(IsServerRendered(await GetAsync(harness)));
        Assert.Empty(servers.Requests);
        Assert.Contains(Warnings(harness), m => m.Contains("skipped (page-too-large)", StringComparison.Ordinal));

        var required = Create(servers, s => { s.MaxPageBytes = 10; s.Mode = SsrMode.Required; });
        Assert.Equal("page-too-large", (await Assert.ThrowsAsync<InertiaSsrException>(() => GetAsync(required))).Type);
    }

    [Fact]
    public async Task Repeated_skips_are_logged_once_per_interval_with_a_count()
    {
        var clock = new FakeTimeProvider();
        var harness = Create(new Servers((_, _) => Ok()), s => s.MaxPageBytes = 10, clock);

        for (var i = 0; i < 3; i++)
        {
            await GetAsync(harness);
        }

        Assert.Single(Warnings(harness));
        clock.Advance(TimeSpan.FromSeconds(31));
        await GetAsync(harness);
        var warnings = Warnings(harness).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains("(2 similar skips not logged", warnings[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logged_ssr_errors_are_truncated_and_urls_lose_their_credentials()
    {
        var servers = new Servers((request, _) => request.RequestUri!.Host == "a.test"
            ? Task.FromResult(Json(HttpStatusCode.InternalServerError, $$"""{"error":"{{new string('x', 5000)}}"}"""))
            : throw new HttpRequestException("Connection refused"));
        var render = Create(servers, s => s.Urls.Add("http://a.test"));
        await GetAsync(render);
        var warning = Assert.Single(Warnings(render));
        Assert.Contains(new string('x', 2048) + "…", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 2049), warning, StringComparison.Ordinal);

        var credentials = Create(servers, s => s.Urls.Add("http://user:secret@b.test"));
        await GetAsync(credentials);
        Assert.DoesNotContain("secret", Assert.Single(Warnings(credentials)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metrics_record_outcomes_page_sizes_and_circuit_states()
    {
        var fail = false;
        var harness = Create(new Servers((_, _) => fail ? throw new HttpRequestException("Connection refused") : Ok()));
        var factory = harness.Services.GetRequiredService<IMeterFactory>();
        var measurements = new ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == InertiaDiagnostics.MeterName && ReferenceEquals(instrument.Meter.Scope, factory))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((i, v, tags, _) => measurements.Enqueue((i.Name, v, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<int>((i, v, tags, _) => measurements.Enqueue((i.Name, v, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<double>((i, v, tags, _) => measurements.Enqueue((i.Name, v, ToDictionary(tags))));
        listener.Start();

        await GetAsync(harness);
        fail = true;
        await GetAsync(harness);
        listener.RecordObservableInstruments();

        var outcomes = measurements.Where(m => m.Name == "inertia.ssr.outcomes").Select(m => $"{m.Tags["inertia.ssr.outcome"]}:{m.Tags["inertia.ssr.reason"]}");
        Assert.Equal(["rendered:ok", "fallback:connection"], outcomes);
        Assert.Equal(2, measurements.Count(m => m is { Name: "inertia.page.duration" } && (string?)m.Tags["inertia.response"] == "html"));
        Assert.Equal(2, measurements.Count(m => m.Name == "inertia.page.size" && m.Value > 0));
        Assert.Equal(2, measurements.Count(m => m.Name == "inertia.ssr.duration"));
        Assert.Contains(measurements, m => m.Name == "inertia.ssr.circuit.state" && (string?)m.Tags["inertia.ssr.endpoint"] == "0" && m.Value == 0);
        Assert.Equal(0, measurements.Where(m => m.Name == "inertia.ssr.active").Sum(m => m.Value));

        static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var result = new Dictionary<string, object?>();
            foreach (var (key, value) in tags)
            {
                result[key] = value;
            }

            return result;
        }
    }

    [Fact]
    public async Task Health_check_reports_the_gateway_state_without_calling_the_servers()
    {
        static async Task<HealthReportEntry> CheckAsync(Harness harness) =>
            (await harness.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken)).Entries["inertia-ssr"];

        var aDown = false;
        var servers = new Servers((request, _) => aDown && request.RequestUri!.Host == "a.test" ? throw new HttpRequestException("Connection refused") : Ok());
        var harness = Create(
            servers,
            s =>
            {
                s.Urls.Add("http://a.test");
                s.Urls.Add("http://b.test");
                s.CircuitBreaker.FailureThreshold = 1;
            },
            services: s => s.AddHealthChecks().AddInertiaSsr());
        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(harness)).Status);
        Assert.Empty(servers.Requests);

        aDown = true;
        for (var i = 0; i < 2; i++)
        {
            await GetAsync(harness); // one of them goes to a first and opens its circuit
        }

        var degraded = await CheckAsync(harness);
        Assert.Equal(HealthStatus.Degraded, degraded.Status);
        Assert.Equal("open", degraded.Data["0"]);
        Assert.Equal("closed", degraded.Data["1"]);

        var single = Create(Refused(), s => s.CircuitBreaker.FailureThreshold = 1, services: s => s.AddHealthChecks().AddInertiaSsr());
        await GetAsync(single);
        Assert.Equal(HealthStatus.Unhealthy, (await CheckAsync(single)).Status);

        var disabled = Create(Refused(), s => s.Mode = SsrMode.Disabled, services: s => s.AddHealthChecks().AddInertiaSsr());
        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(disabled)).Status);
    }

    [Fact]
    public void Default_ssr_client_neither_follows_redirects_nor_uses_a_proxy()
    {
        var harness = new Harness(o => o.Ssr.Mode = SsrMode.Preferred);
        HttpMessageHandler handler = harness.Services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(InertiaSsr.HttpClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(sockets.AllowAutoRedirect);
        Assert.False(sockets.UseProxy);
    }

    [Theory]
    [InlineData("Ssr.Urls entries must be absolute", nameof(InvalidUrl))]
    [InlineData("Ssr.ExcludePaths entries must start with '/'", nameof(RelativeExcludePath))]
    [InlineData("Ssr.Urls is not used with Ssr.UseNodeProcess()", nameof(UrlsWithNodeProcess))]
    [InlineData("Ssr.NodeProcess.Instances", nameof(NoInstances))]
    [InlineData("Ssr.NodeProcess.BasePort", nameof(PortOverflow))]
    [InlineData("Ssr.Concurrency.MaxActive", nameof(NegativeSlots))]
    [InlineData("Ssr.CircuitBreaker.FailureThreshold", nameof(ZeroThreshold))]
    [InlineData("Ssr.MaxPageBytes", nameof(ZeroPageBytes))]
    [InlineData("Ssr.Timeout", nameof(ZeroTimeout))]
    public void Invalid_ssr_options_fail_validation(string message, string configuration)
    {
        Action<SsrOptions> configure = configuration switch
        {
            nameof(InvalidUrl) => InvalidUrl,
            nameof(RelativeExcludePath) => RelativeExcludePath,
            nameof(UrlsWithNodeProcess) => UrlsWithNodeProcess,
            nameof(NoInstances) => NoInstances,
            nameof(PortOverflow) => PortOverflow,
            nameof(NegativeSlots) => NegativeSlots,
            nameof(ZeroThreshold) => ZeroThreshold,
            nameof(ZeroPageBytes) => ZeroPageBytes,
            _ => ZeroTimeout,
        };
        var harness = new Harness(o => configure(o.Ssr));
        var error = Assert.Throws<OptionsValidationException>(() => harness.Services.GetRequiredService<IOptions<InertiaOptions>>().Value);
        Assert.Contains(error.Failures, f => f.StartsWith(message, StringComparison.Ordinal));
    }

    private static void InvalidUrl(SsrOptions s) => s.Urls.Add("localhost:13714");

    private static void RelativeExcludePath(SsrOptions s) => s.ExcludePaths.Add("admin/*");

    private static void UrlsWithNodeProcess(SsrOptions s) => s.UseNodeProcess().Urls.Add("http://127.0.0.1:13714");

    private static void NoInstances(SsrOptions s) => s.UseNodeProcess(n => n.Instances = 0);

    private static void PortOverflow(SsrOptions s) => s.UseNodeProcess(n => { n.BasePort = 65535; n.Instances = 2; });

    private static void NegativeSlots(SsrOptions s) => s.Concurrency.MaxActive = -1;

    private static void ZeroThreshold(SsrOptions s) => s.CircuitBreaker.FailureThreshold = 0;

    private static void ZeroPageBytes(SsrOptions s) => s.MaxPageBytes = 0;

    private static void ZeroTimeout(SsrOptions s) => s.Timeout = TimeSpan.Zero;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!condition())
        {
            Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10), "Timed out.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
