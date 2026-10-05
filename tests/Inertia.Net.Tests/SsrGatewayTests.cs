using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

public sealed class SsrGatewayTests
{
    private const string SsrBody = "<script data-page=\"app\" type=\"application/json\">{\"component\":\"Home\"}</script><div data-server-rendered=\"true\" id=\"app\"><h1>Hi</h1></div>";

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string Body, string? ContentType)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!, body, request.Content?.Headers.ContentType?.ToString()));
            return await respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static FakeHandler Ok(string json = $$"""{"head":["<title>SSR</title>","<meta name=\"a\">"],"body":{{SsrBodyJson}}}""") => new((_, _) => Task.FromResult(Json(HttpStatusCode.OK, json)));

    private const string SsrBodyJson = "\"<script data-page=\\\"app\\\" type=\\\"application/json\\\">{\\\"component\\\":\\\"Home\\\"}</script><div data-server-rendered=\\\"true\\\" id=\\\"app\\\"><h1>Hi</h1></div>\"";

    private static Harness Create(FakeHandler handler, Action<SsrOptions>? configure = null, Action<InertiaOptions>? options = null) =>
        new(
            o =>
            {
                o.Ssr.Enabled = true;
                configure?.Invoke(o.Ssr);
                options?.Invoke(o);
            },
            configureServices: s => s.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));

    private static async Task<string> GetAsync(Harness harness, string url = "/", bool inertia = false)
    {
        var context = harness.Context(url);
        if (inertia)
        {
            context.AsInertia();
        }

        return await Harness.ExecuteAsync(context, Render("Home", new InertiaProps { ["a"] = 1 }));
    }

    private static void AssertClientRendered(string html)
    {
        Assert.Contains("<script data-page=\"app\" type=\"application/json\">", html, StringComparison.Ordinal);
        Assert.EndsWith("</script><div id=\"app\"></div></body></html>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-server-rendered", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Success_joins_head_and_echoes_the_body_verbatim()
    {
        var handler = Ok();
        var html = await GetAsync(Create(handler));

        Assert.Equal($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>SSR</title>\n<meta name=\"a\"></head><body>{SsrBody}</body></html>", html);
        Assert.Equal(1, html.Split("<script data-page").Length - 1);
    }

    [Fact]
    public async Task Posts_the_page_json_to_render()
    {
        var handler = Ok();
        var html = await GetAsync(Create(handler, s => s.Url = "http://ssr.test:1234/"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://ssr.test:1234/render", request.Uri.ToString());
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        var page = Harness.ParsePage(await GetAsync(new Harness(), "/"));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(page, System.Text.Json.Nodes.JsonNode.Parse(request.Body)));
        Assert.Contains("data-server-rendered", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dev_server_renders_through_the_vite_endpoint()
    {
        var handler = Ok();
        await GetAsync(Create(handler, options: o => o.Vite.DevServerUrl = "http://localhost:5173"));

        Assert.Equal("http://localhost:5173/__inertia_ssr", Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task Structured_500_falls_back_to_client_rendering_and_logs_a_warning()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, """{"error":"window is not defined","type":"browser-api","hint":"Guard it with typeof window","browserApi":"window","sourceLocation":"Home.tsx:4"}""")));
        var harness = Create(handler);
        AssertClientRendered(await GetAsync(harness));

        var warning = Assert.Single(harness.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("browser-api", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Guard it with typeof window", warning.Message, StringComparison.Ordinal);
        Assert.Contains("window is not defined", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unstructured_failure_and_invalid_success_fall_back()
    {
        foreach (var handler in new[]
        {
            new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>nginx</html>") })),
            Ok(""),
            Ok("{not json"),
            Ok("""{"head":[],"body":""}"""),
        })
        {
            var harness = Create(handler);
            AssertClientRendered(await GetAsync(harness));
            Assert.Single(harness.Logs.Entries, e => e.Level == LogLevel.Warning);
        }
    }

    [Fact]
    public async Task Timeout_falls_back_and_logs_the_type()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null!;
        });
        var harness = Create(handler, s => s.Timeout = TimeSpan.FromMilliseconds(50));
        AssertClientRendered(await GetAsync(harness));

        Assert.Contains("(timeout)", Assert.Single(harness.Logs.Entries, e => e.Level == LogLevel.Warning).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connection_refused_falls_back_and_logs_the_type()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("Connection refused"));
        var harness = Create(handler);
        AssertClientRendered(await GetAsync(harness));

        Assert.Contains("(connection)", Assert.Single(harness.Logs.Entries, e => e.Level == LogLevel.Warning).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aborted_request_is_not_treated_as_a_timeout()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null!;
        });
        var harness = Create(handler);
        var context = harness.Context();
        using var aborted = new CancellationTokenSource(50);
        context.RequestAborted = aborted.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Harness.ExecuteAsync(context, Render("Home", new InertiaProps())));
    }

    [Fact]
    public async Task ThrowOnError_throws_with_type_and_hint()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, """{"error":"boom","type":"component-resolution","hint":"Check the page path"}""")));
        var ex = await Assert.ThrowsAsync<InertiaSsrException>(() => GetAsync(Create(handler, s => s.ThrowOnError = true)));

        Assert.Equal("component-resolution", ex.Type);
        Assert.Equal("Check the page path", ex.Hint);

        var connection = new FakeHandler((_, _) => throw new HttpRequestException("refused"));
        var inner = await Assert.ThrowsAsync<InertiaSsrException>(() => GetAsync(Create(connection, s => s.ThrowOnError = true)));
        Assert.Equal("connection", inner.Type);
        Assert.IsType<HttpRequestException>(inner.InnerException);
    }

    [Fact]
    public async Task Exclude_paths_match_exactly_or_by_prefix_wildcard()
    {
        var handler = Ok();
        var harness = Create(handler, s =>
        {
            s.ExcludePaths.Add("/login");
            s.ExcludePaths.Add("/admin/*");
        });

        AssertClientRendered(await GetAsync(harness, "/login"));
        AssertClientRendered(await GetAsync(harness, "/admin/users/1"));
        Assert.Empty(handler.Requests);

        await GetAsync(harness, "/login/other");
        await GetAsync(harness, "/administrator");
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Disabled_makes_no_http_call()
    {
        var handler = Ok();
        AssertClientRendered(await GetAsync(Create(handler, s => s.Enabled = false)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Missing_bundle_skips_ssr_unless_the_dev_server_runs()
    {
        var handler = Ok();
        var harness = Create(handler, s => s.BundlePath = "bootstrap/ssr/missing.mjs");
        AssertClientRendered(await GetAsync(harness));
        Assert.Empty(handler.Requests);

        var dev = Create(handler, s => s.BundlePath = "bootstrap/ssr/missing.mjs", o => o.Vite.DevServerUrl = "http://localhost:5173");
        await GetAsync(dev);
        Assert.Single(handler.Requests);

        var existing = Create(handler, s => s.BundlePath = "app.html");
        await GetAsync(existing);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Inertia_json_requests_never_call_ssr()
    {
        var handler = Ok();
        var body = await GetAsync(Create(handler), inertia: true);

        Assert.Empty(handler.Requests);
        Assert.Equal("Home", (string?)Harness.ParsePage(body)["component"]);
    }

    private static async Task<HealthCheckResult> CheckAsync(FakeHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddInertiaSsr();
        services.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddInertia(o => o.Ssr.Timeout = TimeSpan.FromMilliseconds(200));
        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        var entry = report.Entries["inertia-ssr"];
        return new HealthCheckResult(entry.Status, entry.Description, entry.Exception);
    }

    [Fact]
    public async Task Health_check_is_healthy_on_2xx()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await CheckAsync(handler);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("http://127.0.0.1:13714/health", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Health_check_is_unhealthy_on_error_status_connection_failure_or_timeout()
    {
        var statuses = new[]
        {
            new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
            new FakeHandler((_, _) => throw new HttpRequestException("refused")),
            new FakeHandler(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null!;
            }),
        };

        foreach (var handler in statuses)
        {
            Assert.Equal(HealthStatus.Unhealthy, (await CheckAsync(handler)).Status);
        }
    }
}
