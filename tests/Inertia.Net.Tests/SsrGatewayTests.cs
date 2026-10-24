using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
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
                o.Ssr.Mode = SsrMode.Preferred;
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Response_limit_covers_declared_and_streamed_success_and_error_bodies(bool streamed, bool error)
    {
        const string body = """{"head":[],"body":"SSR","error":"detail","type":"http"}""";
        var length = Encoding.UTF8.GetByteCount(body);
        var handler = new FakeHandler((_, _) =>
        {
            HttpContent content = streamed ? new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes(body))) : new StringContent(body);
            // A misleading short length must not bypass the stream limit.
            if (streamed) content.Headers.ContentLength = 1;
            return Task.FromResult(new HttpResponseMessage(error ? HttpStatusCode.InternalServerError : HttpStatusCode.OK) { Content = content });
        });
        var harness = Create(handler, s => s.MaxResponseBytes = length - 1);
        AssertClientRendered(await GetAsync(harness));
        Assert.Contains(harness.Logs.Entries, e => e.Message.Contains("response-too-large", StringComparison.Ordinal));
        var throwing = Create(handler, s => { s.MaxResponseBytes = length - 1; s.Mode = SsrMode.Required; });
        var exception = await Assert.ThrowsAsync<InertiaSsrException>(() => GetAsync(throwing));
        Assert.Equal("response-too-large", exception.Type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Response_at_or_below_the_limit_renders(int extra)
    {
        const string body = """{"head":[],"body":"SSR"}""";
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes(body))),
        }));
        var html = await GetAsync(Create(handler, s => s.MaxResponseBytes = Encoding.UTF8.GetByteCount(body) + extra));
        Assert.Contains("SSR", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-page", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Ssr_options_have_one_authoritative_timeout_and_validate_limits()
    {
        var harness = new Harness(o => o.Ssr.Timeout = TimeSpan.FromSeconds(180));
        Assert.Equal(Timeout.InfiniteTimeSpan, harness.Services.GetRequiredService<IHttpClientFactory>().CreateClient(SsrGateway.HttpClientName).Timeout);
        var invalid = new Harness(o => o.Ssr.MaxResponseBytes = 0);
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => invalid.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<InertiaOptions>>().Value);
        var infinite = new Harness(o => o.Ssr.Timeout = Timeout.InfiniteTimeSpan);
        Assert.Equal(Timeout.InfiniteTimeSpan, infinite.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<InertiaOptions>>().Value.Ssr.Timeout);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_json_does_not_bypass_the_actual_response_limit(bool error)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(error ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes("!" + new string('x', 40000)))),
        }));
        var throwing = Create(handler, s => { s.MaxResponseBytes = 20000; s.Mode = SsrMode.Required; });
        var exception = await Assert.ThrowsAsync<InertiaSsrException>(() => GetAsync(throwing));
        Assert.Equal("response-too-large", exception.Type);
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
        var html = await GetAsync(Create(handler, s => s.Urls.Add("http://ssr.test:1234/")));

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
    public async Task Misconfigured_url_falls_back_instead_of_failing_the_page()
    {
        // What a handler might throw for a request it cannot send at all.
        var harness = Create(new FakeHandler((_, _) => throw new NotSupportedException("The 'localhost' scheme is not supported.")));
        AssertClientRendered(await GetAsync(harness));

        Assert.Contains("(unknown)", Assert.Single(harness.Logs.Entries, e => e.Level == LogLevel.Warning).Message, StringComparison.Ordinal);
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
    public async Task Required_mode_throws_with_type_and_hint()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, """{"error":"boom","type":"component-resolution","hint":"Check the page path"}""")));
        var ex = await Assert.ThrowsAsync<InertiaSsrException>(() => GetAsync(Create(handler, s => s.Mode = SsrMode.Required)));

        Assert.Equal("component-resolution", ex.Type);
        Assert.Equal("Check the page path", ex.Hint);

        var connection = new FakeHandler((_, _) => throw new HttpRequestException("refused"));
        var inner = await Assert.ThrowsAsync<InertiaSsrException>(() => GetAsync(Create(connection, s => s.Mode = SsrMode.Required)));
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
        AssertClientRendered(await GetAsync(Create(handler, s => s.Mode = SsrMode.Disabled)));
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

        // An absolute path is used as it is, not under the content root.
        var absolute = Create(handler, s => s.BundlePath = Path.Join(Harness.DefaultContentRoot, "app.html"));
        await GetAsync(absolute);
        Assert.Equal(3, handler.Requests.Count);
        var absoluteMissing = Create(handler, s => s.BundlePath = Path.Join(Path.GetTempPath(), "inertia-missing-" + Guid.NewGuid().ToString("N"), "ssr.mjs"));
        AssertClientRendered(await GetAsync(absoluteMissing));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Bundle_presence_is_cached_and_a_late_bundle_is_picked_up()
    {
        var handler = Ok();
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var root = Path.Join(Path.GetTempPath(), "inertia-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.Copy(Path.Join(Harness.DefaultContentRoot, "app.html"), Path.Join(root, "app.html"));
        var harness = new Harness(
            o =>
            {
                o.Ssr.Mode = SsrMode.Preferred;
                o.Ssr.BundlePath = "ssr/ssr.mjs";
            },
            timeProvider: clock,
            contentRoot: root,
            configureServices: s => s.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));

        AssertClientRendered(await GetAsync(harness));
        Directory.CreateDirectory(Path.Join(root, "ssr"));
        File.WriteAllText(Path.Join(root, "ssr", "ssr.mjs"), "");
        AssertClientRendered(await GetAsync(harness)); // the miss is re-checked at most once per second
        Assert.Empty(handler.Requests);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Contains("data-server-rendered", await GetAsync(harness), StringComparison.Ordinal);
        Assert.Single(handler.Requests);
        Assert.Equal("http://127.0.0.1:13714/render", handler.Requests[0].Uri.ToString());
    }

    [Theory]
    [InlineData("""{"head":["<title>a</title>","<meta>"],"body":"<div>x</div>"}""", "<title>a</title>\n<meta>", "<div>x</div>")]
    [InlineData("""{"body":"\u003Cp class=\"a\"\u003E\ud83d\ude00 \u00e9\/\n</p>","head":[]}""", "", "<p class=\"a\">😀 é/\n</p>")]
    [InlineData("""{"extra":{"deep":[1,{"x":2}]},"head":[1,null,"<a>",{"x":1},"<b>"],"body":"y"}""", "<a>\n<b>", "y")]
    [InlineData("""{"head":null,"body":"first","body":"last"}""", "", "last")]
    public void Ssr_responses_parse_into_utf8_html(string json, string head, string body)
    {
        using var render = SsrGateway.ParseRender(Encoding.UTF8.GetBytes(json));
        Assert.NotNull(render);
        Assert.Equal(head, Encoding.UTF8.GetString(render.Head.Span));
        Assert.Equal(body, Encoding.UTF8.GetString(render.Body.Span));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"body\"")]
    [InlineData("""{"body":"unterminated""")]
    [InlineData("""{"body":"\ud800"}""")]
    public void Malformed_ssr_responses_parse_as_null(string json) =>
        Assert.Null(SsrGateway.ParseRender(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Invalid_utf8_in_an_ssr_response_parses_as_null() =>
        Assert.Null(SsrGateway.ParseRender([.. "{\"body\":\""u8, 0xC3, 0x28, .. "\"}"u8]));

    [Fact]
    public async Task Inertia_json_requests_never_call_ssr()
    {
        var handler = Ok();
        var body = await GetAsync(Create(handler), inertia: true);

        Assert.Empty(handler.Requests);
        Assert.Equal("Home", (string?)Harness.ParsePage(body)["component"]);
    }
}
