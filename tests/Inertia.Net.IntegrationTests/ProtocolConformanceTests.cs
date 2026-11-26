using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

/// <summary>
/// The Inertia v3 protocol conformance suite (docs/protocol.md §1, §3, §6, §7), run against every framework host.
/// <para>
/// <b>Route contract.</b> A host (<see cref="IConformanceHost"/>) maps exactly these routes; the base app calls
/// <c>AddInertia</c>, <c>UseInertia</c> (and <c>UseSession</c>/<c>UsePathBase</c> for some cases) and registers
/// <see cref="ConformanceCounters"/>:
/// </para>
/// <list type="table">
/// <item><term>GET /page</term><description><c>counters.HitPage()</c>; render <c>Page</c> with <c>{ message: "hello" }</c>.</description></item>
/// <item><term>GET /café</term><description>render <c>Page</c> (no props).</description></item>
/// <item><term>GET /partial</term><description>render <c>Partial</c> with <c>{ a: 1, b: Prop(() =&gt; 2), c: Always(3), d: Optional(() =&gt; 4) }</c>.</description></item>
/// <item><term>GET /paged, /paged/scroll, /paged/cursor</term><description><c>Paged</c> with 25 numbered items,
/// ten per page, through the core delegate paginators. The latter two wrap their values with <c>Scroll</c>.</description></item>
/// <item><term>GET /live</term><description>share <c>bell = LivePage.Bell()</c>; render <c>Live</c> with
/// <c>LivePage.Props(InertiaRequest.SocketId)</c>.</description></item>
/// <item><term>GET /deferred</term><description>render <c>Deferred</c> with <c>{ a: 1, b: Defer(() =&gt; "b"), c: Defer(() =&gt; "c", "other") }</c>.</description></item>
/// <item><term>POST|PUT|PATCH|DELETE /form</term><description>JSON body <c>{ name }</c>, name required. Invalid: on an Inertia request,
/// redirect back (302) with the errors keyed by the JSON field name (<c>name</c>) in the bag named by <c>X-Inertia-Error-Bag</c>
/// (else <c>default</c>). Valid: flash <c>success = "Saved {name}"</c> and 302 to <c>/page</c>.</description></item>
/// <item><term>POST|PUT|PATCH|DELETE /redirect</term><description>302 to <c>/page</c>.</description></item>
/// <item><term>POST /flash/{n}</term><description>flash <c>n = n</c> (int); 302 to <c>/isolated/{n}</c>.</description></item>
/// <item><term>GET /isolated/{n}</term><description>share <c>shared = n</c>, <c>EncryptHistory(n % 2 == 0)</c>; render <c>Isolated</c> with
/// <c>{ n: async loader returning n }</c>.</description></item>
/// <item><term>GET /fragment-redirect</term><description>302 to <c>/page#section</c>.</description></item>
/// <item><term>GET /external</term><description><c>Location("https://external.example/path")</c>.</description></item>
/// <item><term>GET|POST|PUT|PATCH|DELETE /empty</term><description>200 with an empty body.</description></item>
/// <item><term>GET /clear-history</term><description><c>ClearHistory()</c>; 302 to <c>/page</c>.</description></item>
/// <item><term>GET /preserve-fragment</term><description><c>PreserveFragment()</c>; 302 to <c>/page</c>.</description></item>
/// <item><term>GET /encrypt</term><description><c>EncryptHistory()</c>; render <c>Page</c>.</description></item>
/// <item><term>GET /error-page</term><description>render <c>Error</c> with <c>{ status: 404 }</c> and status code 404.</description></item>
/// <item><term>GET /ssr-off</term><description>render <c>Page</c> with <c>WithoutSsr()</c>.</description></item>
/// <item><term>GET /ssr-required</term><description>render <c>Page</c> with <c>WithSsr(SsrMode.Required)</c>.</description></item>
/// </list>
/// </summary>
public abstract class ProtocolConformanceTests<THost>
    where THost : IConformanceHost
{
    [Theory]
    [InlineData("POST", 302)]
    [InlineData("PUT", 303)]
    public async Task Back_without_referer_uses_the_last_page_url(string method, int status)
    {
        await using var app = await ConformanceApp.StartAsync<THost>(new ConformanceSetup
        {
            Inertia = o => o.State.RememberPreviousUrl = true,
        });
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.InertiaGetAsync("/users?x=1")).StatusCode);
        var response = await client.InertiaAsync(new HttpMethod(method), "/back-test");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("/users?x=1", response.Headers.Location?.OriginalString);
    }

    private const string Base = "http://localhost";

    [Fact]
    public async Task A_page_record_renders_with_the_component_it_names()
    {
        await using var app = await StartAsync();
        var page = await (await app.CreateClient().InertiaGetAsync("/typed/7")).PageAsync();
        Assert.Equal("Typed/Show", (string?)page["component"]);
        Assert.Equal(7, (int?)page["props"]!["id"]);
    }

    [Fact]
    public async Task Routes_are_generated_from_the_named_endpoints_when_the_app_starts()
    {
        var path = Path.Join(Path.GetTempPath(), "inertia-routes-" + Guid.NewGuid().ToString("N"), "routes.ts");
        try
        {
            // Written by the hosted service during StartAsync: the endpoints must already be known then, for every host.
            await using var app = await StartAsync(new ConformanceSetup
            {
                EnvironmentName = "Development",
                Inertia = o => o.TypeScript.RoutesOutputPath = path,
            });
            var routes = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("definition: { methods: ['get'] as const, url: '/typed/{id}' }", routes, StringComparison.Ordinal);
            Assert.Contains("args: { id: number } | [id: number] | number", routes, StringComparison.Ordinal);
            Assert.Contains("typed: {\n    show: r", routes.ReplaceLineEndings("\n"), StringComparison.Ordinal);
            Assert.Contains("definition: { methods: ['post'] as const, url: '/typed' }", routes, StringComparison.Ordinal);
            // Only the two named endpoints: FastEndpoints' class-derived names and unnamed endpoints are skipped.
            Assert.Equal(2, Regex.Count(routes, @"^const r\d+ = ", RegexOptions.Multiline));
            Assert.Contains("export interface RouteBodies {\n  'typed.store': TypedStoreInput\n}\n\nexport interface TypedStoreInput {\n  id: number\n  title: string | null\n}", routes.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    protected static Task<ConformanceApp> StartAsync(ConformanceSetup? setup = null) => ConformanceApp.StartAsync<THost>(setup);

    [Theory]
    [InlineData("/paged")]
    [InlineData("/paged/scroll")]
    public async Task Pagination_responses_cover_html_visits_and_scroll_reload_metadata(string route)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var html = await (await client.GetAsync(route, TestContext.Current.CancellationToken)).PageAsync();
        Assert.Equal("Paged", (string?)html["component"]);
        JsonAssert.Equal("[1,2,3,4,5,6,7,8,9,10]", html["props"]!["posts"]!["data"]);
        if (route == "/paged/scroll")
        {
            Assert.Equal("[\"posts.data\"]", html["mergeProps"]!.ToJsonString());
        }
        else
        {
            Assert.Null(html["scrollProps"]);
            Assert.Null(html["mergeProps"]);
        }

        for (var page = 1; page <= 4; page++)
        {
            var response = await (await client.InertiaGetAsync($"{route}?page={page}")).PageAsync();
            var rows = string.Join(',', Enumerable.Range((page - 1) * 10 + 1, page < 3 ? 10 : page == 3 ? 5 : 0));
            var from = page <= 3 ? ((page - 1) * 10 + 1).ToString() : "null";
            var to = page <= 3 ? Math.Min(page * 10, 25).ToString() : "null";
            var more = page < 3 ? "true" : "false";
            Assert.Equal($"{{\"data\":[{rows}],\"currentPage\":{page},\"perPage\":10,\"total\":25,\"lastPage\":3,\"from\":{from},\"to\":{to},\"hasMore\":{more},\"pageName\":\"page\"}}", response["props"]!["posts"]!.ToJsonString());
        }

        var append = await (await client.InertiaGetAsync("/paged/scroll?page=2", r =>
        {
            r.WithHeader(InertiaHeaders.PartialComponent, "Paged");
            r.WithHeader(InertiaHeaders.PartialData, "posts");
        })).PageAsync();
        Assert.Null(append["props"]!["other"]);
        Assert.Equal("[11,12,13,14,15,16,17,18,19,20]", append["props"]!["posts"]!["data"]!.ToJsonString());
        Assert.Equal("[\"posts.data\"]", append["mergeProps"]!.ToJsonString());
        Assert.Equal("{\"posts\":{\"pageName\":\"page\",\"previousPage\":1,\"nextPage\":3,\"currentPage\":2,\"reset\":false}}", append["scrollProps"]!.ToJsonString());

        var partial = await (await client.InertiaGetAsync("/paged/scroll?page=2", r =>
        {
            r.WithHeader(InertiaHeaders.PartialComponent, "Paged");
            r.WithHeader(InertiaHeaders.PartialData, "posts");
            r.WithHeader(InertiaHeaders.InfiniteScrollMergeIntent, "prepend");
        })).PageAsync();
        JsonAssert.Equal("[\"posts.data\"]", partial["prependProps"]);
        JsonAssert.Equal("{\"posts\":{\"pageName\":\"page\",\"previousPage\":1,\"nextPage\":3,\"currentPage\":2,\"reset\":false}}", partial["scrollProps"]);
        var reset = await (await client.InertiaGetAsync("/paged/scroll?page=2", r => r.WithHeader(InertiaHeaders.Reset, "posts"))).PageAsync();
        Assert.Null(reset["mergeProps"]);
        Assert.True((bool)reset["scrollProps"]!["posts"]!["reset"]!);
    }

    [Fact]
    public async Task Cursor_scroll_follows_next_and_previous_in_display_order()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var initial = await (await client.GetAsync("/paged/cursor", TestContext.Current.CancellationToken)).PageAsync();
        Assert.Equal("[1,2,3,4,5,6,7,8,9,10]", initial["props"]!["posts"]!["data"]!.ToJsonString());
        var first = await (await client.InertiaGetAsync("/paged/cursor")).PageAsync();
        Assert.Equal(1, (int)first["scrollProps"]!["posts"]!["currentPage"]!);
        var next = (string)first["props"]!["posts"]!["nextCursor"]!;
        Assert.Equal($"{{\"data\":[1,2,3,4,5,6,7,8,9,10],\"perPage\":10,\"nextCursor\":\"{next}\",\"previousCursor\":null,\"hasMore\":true,\"cursorName\":\"cursor\"}}",
            first["props"]!["posts"]!.ToJsonString());
        Assert.Equal($"{{\"posts\":{{\"pageName\":\"cursor\",\"previousPage\":null,\"nextPage\":\"{next}\",\"currentPage\":1,\"reset\":false}}}}",
            first["scrollProps"]!.ToJsonString());
        var second = await (await client.InertiaGetAsync("/paged/cursor?cursor=" + Uri.EscapeDataString(next))).PageAsync();
        JsonAssert.Equal("[11,12,13,14,15,16,17,18,19,20]", second["props"]!["posts"]!["data"]);
        Assert.Equal(next, (string?)second["scrollProps"]!["posts"]!["currentPage"]);
        var previous = (string)second["props"]!["posts"]!["previousCursor"]!;
        var thirdCursor = (string)second["props"]!["posts"]!["nextCursor"]!;
        var third = await (await client.InertiaGetAsync("/paged/cursor?cursor=" + Uri.EscapeDataString(thirdCursor))).PageAsync();
        Assert.Equal("[21,22,23,24,25]", third["props"]!["posts"]!["data"]!.ToJsonString());
        Assert.Null(third["props"]!["posts"]!["nextCursor"]);
        var back = await (await client.InertiaGetAsync("/paged/cursor?cursor=" + Uri.EscapeDataString(previous), r =>
        {
            r.WithHeader(InertiaHeaders.PartialComponent, "Paged");
            r.WithHeader(InertiaHeaders.PartialData, "posts");
            r.WithHeader(InertiaHeaders.InfiniteScrollMergeIntent, "prepend");
        })).PageAsync();
        Assert.Equal(first["props"]!["posts"]!["data"]!.ToJsonString(), back["props"]!["posts"]!["data"]!.ToJsonString());
        Assert.Null(back["props"]!["posts"]!["previousCursor"]);
        Assert.Equal("[\"posts.data\"]", back["prependProps"]!.ToJsonString());
    }

    [Fact]
    public async Task Deferred_array_index_reload_preserves_both_rows_and_their_siblings()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().InertiaGetAsync("/array", r =>
        {
            r.WithHeader(InertiaHeaders.PartialComponent, "Array");
            r.WithHeader(InertiaHeaders.PartialData, "rows.1.extra");
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonAssert.Equal("""[{"name":"first","extra":10},{"name":"second","extra":20}]""", (await response.PageAsync())["props"]!["rows"]);
    }

    private const string OrderListener = """{"listeners":[{"channel":{"name":"orders.1","type":"private"},"events":["OrderUpdated"]}]}""";
    private const string StatsListener = """{"listeners":[{"channel":{"name":"stats","type":"public"},"events":["StatsChanged"]}]}""";

    [Fact]
    public async Task Live_props_report_listeners_on_the_initial_page_and_inertia_visits()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var expected = $$$"""
            {"bell":{"listeners":[{"channel":{"name":"users.1","type":"private"},"events":["Notified"]}]},
             "order":{{{OrderListener}}},"stats":{{{StatsListener}}},"history":{{{OrderListener}}},
             "feed":{"listeners":[{"channel":{"name":"stats","type":"public"},"events":["StatsChanged"]}],"throttle":500},
             "flaky":{{{OrderListener}}}}
            """;

        var html = await (await client.GetAsync("/live", TestContext.Current.CancellationToken)).PageAsync();
        JsonAssert.Equal(expected, html["liveProps"]);
        JsonAssert.Equal("""{"default":["stats"]}""", html["deferredProps"]);
        JsonAssert.Equal("""["flaky"]""", html["rescuedProps"]);
        JsonAssert.Equal("""{"errors":{},"bell":3,"order":"order","feed":"feed","socket":null}""", html["props"]);

        var visit = await (await client.InertiaGetAsync("/live", r => r.WithHeader(InertiaHeaders.SocketId, " 123.456 "))).PageAsync();
        JsonAssert.Equal(expected, visit["liveProps"]);
        Assert.Equal("123.456", (string?)visit["props"]!["socket"]);
    }

    [Fact]
    public async Task Live_props_on_partial_reloads_report_only_included_props()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        Task<JsonObject> Partial(string? only, string? except = null) => client.InertiaGetAsync("/live", r =>
        {
            r.WithHeader(InertiaHeaders.PartialComponent, "Live");
            if (only is not null) r.WithHeader(InertiaHeaders.PartialData, only);
            if (except is not null) r.WithHeader(InertiaHeaders.PartialExcept, except);
        }).ContinueWith(t => t.Result.PageAsync()).Unwrap();

        var only = await Partial("order,flaky");
        JsonAssert.Equal($$$"""{"order":{{{OrderListener}}},"flaky":{{{OrderListener}}}}""", only["liveProps"]);
        JsonAssert.Equal("""["flaky"]""", only["rescuedProps"]);

        var except = await Partial(null, "order,bell,flaky");
        Assert.Equal(["stats", "history", "feed"], except["liveProps"]!.AsObject().Select(p => p.Key));

        var deferredGroup = await Partial("stats");
        JsonAssert.Equal($$$"""{"stats":{{{StatsListener}}}}""", deferredGroup["liveProps"]);
        Assert.Equal("stats", (string?)deferredGroup["props"]!["stats"]);
    }

    [Fact]
    public async Task Responses_choose_their_own_ssr_mode()
    {
        var ssr = new FakeSsrServer();
        await using var app = await StartAsync(new ConformanceSetup
        {
            Inertia = o => o.Ssr.Mode = SsrMode.Preferred,
            Services = s => s.AddHttpClient(InertiaSsr.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => ssr),
        });
        var client = app.CreateClient();

        Assert.Contains(FakeSsrServer.Body, await client.GetStringAsync("/page", TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.DoesNotContain(FakeSsrServer.Body, await client.GetStringAsync("/ssr-off", TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(1, ssr.Calls);

        ssr.Down = true;
        Assert.DoesNotContain(FakeSsrServer.Body, await client.GetStringAsync("/page", TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<InertiaSsrException>(() => client.GetAsync("/ssr-required", TestContext.Current.CancellationToken));
        Assert.Equal("connection", error.Type); // thrown before the response started, so the app's error handling sees it
        Assert.Equal(3, ssr.Calls);
    }

    [Fact]
    public async Task Plain_get_returns_html_with_the_embedded_page()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().GetAsync("/page", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(InertiaHeaders.PageVary, response.Header("Vary"));
        Assert.Null(response.Header(InertiaHeaders.Inertia));
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("<script data-page=\"app\" type=\"application/json\">", html, StringComparison.Ordinal);
        var page = await response.PageAsync();
        Assert.Equal("Page", (string?)page["component"]);
        Assert.Equal("hello", (string?)page["props"]!["message"]);
    }

    [Fact]
    public async Task Inertia_get_returns_a_json_page()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().InertiaGetAsync("/page");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("true", response.Header(InertiaHeaders.Inertia));
        Assert.Equal(InertiaHeaders.PageVary, response.Header("Vary"));
        JsonAssert.Equal("""{"component":"Page","props":{"errors":{},"message":"hello"},"url":"/page","version":"","sharedProps":["errors"]}""", await response.PageAsync());
    }

    [Theory]
    [InlineData("GET", "/external", false)]
    [InlineData("GET", "/external", true)]
    [InlineData("GET", "/fragment-redirect", true)]
    [InlineData("GET", "/empty", true)]
    [InlineData("GET", "/empty", false)]
    [InlineData("GET", "/does-not-exist", false)]
    [InlineData("POST", "/redirect", true)]
    public async Task Every_response_varies_on_x_inertia(string method, string url, bool inertia)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = inertia
            ? await client.InertiaAsync(new HttpMethod(method), url)
            : await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), TestContext.Current.CancellationToken);
        Assert.Equal("X-Inertia", response.Header("Vary"));
    }

    /// <summary>
    /// A shared cache that stores every GET and answers a later request from it when the request headers named by the stored
    /// response's <c>Vary</c> match (RFC 9111 §4.1). It stores regardless of <c>Cache-Control</c>, so it checks <c>Vary</c> alone.
    /// </summary>
    private sealed class SharedCache(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly List<(string Url, string[] Vary, Dictionary<string, string> Selected, int Status, byte[] Body)> _entries = [];

        public int Hits { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            foreach (var entry in _entries)
            {
                if (entry.Url == url && !entry.Vary.Contains("*") && entry.Vary.All(h => entry.Selected[h] == Value(request, h)))
                {
                    Hits++;
                    return new HttpResponseMessage((HttpStatusCode)entry.Status) { Content = new ByteArrayContent(entry.Body) };
                }
            }

            var response = await base.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var vary = response.Headers.Vary.SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.ToLowerInvariant()).ToArray();
            _entries.Add((url, vary, vary.ToDictionary(h => h, h => Value(request, h)), (int)response.StatusCode, body));
            return new HttpResponseMessage(response.StatusCode) { Content = new ByteArrayContent(body) };
        }

        private static string Value(HttpRequestMessage request, string header) =>
            request.Headers.TryGetValues(header, out var values) ? string.Join(",", values) : "";
    }

    public static TheoryData<string, string> SharedCacheVisits => new()
    {
        { "full", "partial" },
        { "partial", "full" },
        { "full", "partial-html" },
        { "partial-html", "full-html" },
        { "full", "once" },
        { "full", "error-bag" },
        { "current", "stale" },
        { "stale", "current" },
    };

    [Theory]
    [MemberData(nameof(SharedCacheVisits))]
    public async Task A_shared_cache_serves_each_visit_its_own_page(string first, string second)
    {
        await using var app = await StartAsync(new ConformanceSetup { Inertia = o => o.Version = "v2" });
        var cache = new SharedCache(app.Server.CreateHandler());
        using var shared = new HttpClient(cache) { BaseAddress = app.Server.BaseAddress };

        HttpRequestMessage Visit(string kind)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/partial");
            if (!kind.EndsWith("-html", StringComparison.Ordinal)) request.WithHeader(InertiaHeaders.Inertia, "true");
            request.WithHeader(InertiaHeaders.Version, kind == "stale" ? "v1" : "v2");
            if (kind.StartsWith("partial", StringComparison.Ordinal))
            {
                request.WithHeader(InertiaHeaders.PartialComponent, "Partial");
                request.WithHeader(InertiaHeaders.PartialData, "d");
            }

            if (kind == "once") request.WithHeader(InertiaHeaders.ExceptOnceProps, "a");
            if (kind == "error-bag") request.WithHeader(InertiaHeaders.ErrorBag, "login");
            return request;
        }

        // What the origin answers the second visit with, without the cache in between.
        var direct = await app.CreateClient().SendAsync(Visit(second), TestContext.Current.CancellationToken);
        var expected = await direct.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        await shared.SendAsync(Visit(first), TestContext.Current.CancellationToken);
        var served = await shared.SendAsync(Visit(second), TestContext.Current.CancellationToken);

        Assert.Equal(direct.StatusCode, served.StatusCode);
        Assert.Equal(expected, await served.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        // The same visit again is a hit: the variation does not defeat caching of identical requests.
        var hits = cache.Hits;
        await shared.SendAsync(Visit(second), TestContext.Current.CancellationToken);
        Assert.Equal(hits + 1, cache.Hits);
    }

    [Fact]
    public async Task Version_mismatch_is_a_409_and_the_handler_does_not_run()
    {
        await using var app = await StartAsync(new ConformanceSetup { Inertia = o => o.Version = "v2", PathBase = "/base" });
        var response = await app.CreateClient().InertiaGetAsync("/base/page?q=caf%C3%A9&x=1", r => r.WithHeader(InertiaHeaders.Version, "v1"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"{Base}/base/page?q=caf%C3%A9&x=1", response.Header(InertiaHeaders.Location));
        Assert.Equal("v2", response.Header(InertiaHeaders.Version));
        Assert.Equal(InertiaHeaders.PageVary, response.Header("Vary"));
        Assert.Equal(0, app.Counters.Page);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        var matching = await app.CreateClient().InertiaGetAsync("/base/page", r => r.WithHeader(InertiaHeaders.Version, "v2"));
        Assert.Equal(HttpStatusCode.OK, matching.StatusCode);
        Assert.Equal("v2", (string?)(await matching.PageAsync())["version"]);
        Assert.Equal(1, app.Counters.Page);
    }

    [Fact]
    public async Task Version_mismatch_only_applies_to_inertia_gets()
    {
        await using var app = await StartAsync(new ConformanceSetup { Inertia = o => o.Version = "v2" });
        var client = app.CreateClient();

        var post = await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" }, r => r.WithHeader(InertiaHeaders.Version, "v1"));
        Assert.Equal(HttpStatusCode.Found, post.StatusCode);

        var plain = new HttpRequestMessage(HttpMethod.Get, "/page");
        plain.WithHeader(InertiaHeaders.Version, "v1");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(plain, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task State_is_kept_across_a_version_409()
    {
        await using var app = await StartAsync(new ConformanceSetup { Inertia = o => o.Version = "v2" });
        var client = app.CreateClient();
        await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" }, r => r.WithHeader(InertiaHeaders.Version, "v2"));

        var conflict = await client.InertiaGetAsync("/page", r => r.WithHeader(InertiaHeaders.Version, "v1"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Null(conflict.Header("Set-Cookie"));

        var page = await (await client.GetAsync("/page", TestContext.Current.CancellationToken)).PageAsync();
        Assert.Equal("Saved Ann", (string?)page["flash"]!["success"]);
    }

    [Theory]
    [InlineData("PUT", HttpStatusCode.SeeOther)]
    [InlineData("PATCH", HttpStatusCode.SeeOther)]
    [InlineData("DELETE", HttpStatusCode.SeeOther)]
    [InlineData("QUERY", HttpStatusCode.SeeOther)]
    [InlineData("POST", HttpStatusCode.Found)]
    public async Task Redirects_after_put_patch_delete_become_303(string method, HttpStatusCode expected)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var response = await client.InertiaAsync(new HttpMethod(method), "/redirect");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("/page", response.Headers.Location?.OriginalString);

        var form = await client.InertiaAsync(new HttpMethod(method), "/form", new { name = "Ann" });
        Assert.Equal(expected, form.StatusCode);

        var plain = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/redirect"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, plain.StatusCode);
    }

    [Fact]
    public async Task Redirect_to_a_fragment_is_a_409_with_x_inertia_redirect()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var response = await client.InertiaGetAsync("/fragment-redirect");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("/page#section", response.Header(InertiaHeaders.Redirect));
        Assert.Null(response.Headers.Location);

        var prefetch = await client.InertiaGetAsync("/fragment-redirect", r => r.WithHeader(InertiaHeaders.Purpose, "prefetch"));
        Assert.Equal(HttpStatusCode.Found, prefetch.StatusCode);
        Assert.Equal("/page#section", prefetch.Headers.Location?.OriginalString);

        var plain = await client.GetAsync("/fragment-redirect", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, plain.StatusCode);
    }

    [Fact]
    public async Task Location_is_a_409_for_inertia_requests_and_a_302_otherwise()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var inertia = await client.InertiaGetAsync("/external");
        Assert.Equal(HttpStatusCode.Conflict, inertia.StatusCode);
        Assert.Equal("https://external.example/path", inertia.Header(InertiaHeaders.Location));

        var plain = await client.GetAsync("/external", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, plain.StatusCode);
        Assert.Equal("https://external.example/path", plain.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("GET", HttpStatusCode.Found)]
    [InlineData("POST", HttpStatusCode.Found)]
    [InlineData("PUT", HttpStatusCode.SeeOther)]
    [InlineData("PATCH", HttpStatusCode.SeeOther)]
    [InlineData("DELETE", HttpStatusCode.SeeOther)]
    [InlineData("QUERY", HttpStatusCode.SeeOther)]
    public async Task An_empty_response_to_an_inertia_request_redirects_back(string method, HttpStatusCode expected)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var response = await client.InertiaAsync(new HttpMethod(method), "/empty", configure: r => r.Headers.Referrer = new Uri($"{Base}/page?x=1"));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal($"{Base}/page?x=1", response.Headers.Location?.OriginalString);

        var noReferer = await client.InertiaAsync(new HttpMethod(method), "/empty");
        Assert.Equal("/", noReferer.Headers.Location?.OriginalString);

        var foreign = await client.InertiaAsync(new HttpMethod(method), "/empty", configure: r => r.Headers.Referrer = new Uri("https://evil.example/"));
        Assert.Equal("/", foreign.Headers.Location?.OriginalString);

        var plain = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/empty"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("createUser")]
    public async Task Validation_errors_redirect_back_and_show_on_the_next_page_once(string? errorBag)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        void Headers(HttpRequestMessage r)
        {
            r.Headers.Referrer = new Uri($"{Base}/page");
            if (errorBag is not null)
            {
                r.WithHeader(InertiaHeaders.ErrorBag, errorBag);
            }
        }

        var response = await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "" }, Headers);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal($"{Base}/page", response.Headers.Location?.OriginalString);

        // The client follows the redirect with the same headers.
        var errors = (await (await client.InertiaGetAsync("/page", Headers)).PageAsync())["props"]!["errors"]!.AsObject();
        var fields = errorBag is null ? errors : errors[errorBag]!.AsObject();
        Assert.Single(fields);
        Assert.Equal("name", fields.Single().Key);
        Assert.False(string.IsNullOrEmpty((string?)fields.Single().Value));

        var again = await (await client.InertiaGetAsync("/page", Headers)).PageAsync();
        JsonAssert.Equal("{}", again["props"]!["errors"]);
    }

    [Fact]
    public async Task Validation_errors_survive_a_put_with_a_303()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = await client.InertiaAsync(HttpMethod.Put, "/form", new { name = "" }, r => r.Headers.Referrer = new Uri($"{Base}/page"));
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Single((await (await client.InertiaGetAsync("/page")).PageAsync())["props"]!["errors"]!.AsObject());
    }

    [Fact]
    public async Task Flash_survives_the_redirect_and_is_consumed_once()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var response = await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" });
        Assert.Equal("/page", response.Headers.Location?.OriginalString);
        if (THost.StateInCookie)
        {
            Assert.Contains(".Inertia.State=", response.Header("Set-Cookie"), StringComparison.Ordinal);
        }

        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        JsonAssert.Equal("""{"success":"Saved Ann"}""", page["flash"]);
        JsonAssert.Equal("{}", page["props"]!["errors"]);

        JsonAssert.Missing(await (await client.InertiaGetAsync("/page")).PageAsync(), "flash");
    }

    [Fact]
    public async Task Flash_from_a_classic_form_post_reaches_the_html_page()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var post = new HttpRequestMessage(HttpMethod.Post, "/form") { Content = System.Net.Http.Json.JsonContent.Create(new { name = "Bob" }) };
        Assert.Equal(HttpStatusCode.Found, (await client.SendAsync(post, TestContext.Current.CancellationToken)).StatusCode);

        var page = await (await client.GetAsync("/page", TestContext.Current.CancellationToken)).PageAsync();
        Assert.Equal("Saved Bob", (string?)page["flash"]!["success"]);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("location")]
    [InlineData("fragment")]
    public async Task Newly_set_flash_survives_client_redirects_and_empty_responses(string kind)
    {
        await using var app = await StartAsync(new ConformanceSetup
        {
            Middleware = application => application.Use(async (context, next) =>
            {
                if (context.Request.Path == "/flash-control")
                {
                    context.Inertia().Flash("toast", "saved");
                    if (kind == "location") await Location("/page").ExecuteAsync(context);
                    else if (kind == "fragment") context.Response.Redirect("/page#saved");
                    return;
                }
                await next(context);
            }),
        });
        using var client = app.CreateClient();
        using var redirect = await client.InertiaGetAsync("/flash-control");
        Assert.Equal(kind == "empty" ? HttpStatusCode.Found : HttpStatusCode.Conflict, redirect.StatusCode);
        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        Assert.Equal("saved", (string?)page["flash"]!["toast"]);
        JsonAssert.Missing(await (await client.InertiaGetAsync("/page")).PageAsync(), "flash");
    }

    // Laravel re-flashes on every redirect (Middleware::reflash) and keeps clearHistory/preserveFragment in the session until a
    // page renders, so e.g. Back() to a URL that itself redirects still shows the flash.
    [Fact]
    public async Task State_survives_chained_redirects_until_a_page_renders()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" });
        await client.GetAsync("/clear-history", TestContext.Current.CancellationToken);

        var other = await client.GetAsync("/external", TestContext.Current.CancellationToken); // a redirect that sets nothing new
        if (THost.StateInCookie)
        {
            Assert.DoesNotContain("expires=", other.Header("Set-Cookie") ?? "", StringComparison.OrdinalIgnoreCase);
        }

        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        JsonAssert.Equal("""{"success":"Saved Ann"}""", page["flash"]);
        Assert.True((bool?)page["clearHistory"]);

        var next = await (await client.InertiaGetAsync("/page")).PageAsync();
        JsonAssert.Missing(next, "flash");
        JsonAssert.Missing(next, "clearHistory");
    }

    [Fact]
    public async Task State_is_consumed_by_the_next_response_that_is_not_a_redirect()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" });

        var other = await client.GetAsync("/empty", TestContext.Current.CancellationToken);
        if (THost.StateInCookie)
        {
            Assert.Contains("expires=", other.Header("Set-Cookie"), StringComparison.OrdinalIgnoreCase);
        }

        JsonAssert.Missing(await (await client.InertiaGetAsync("/page")).PageAsync(), "flash");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Forgetting_restored_flash_preserves_remaining_values_only_until_the_next_page(bool redirectAgain)
    {
        await using var app = await StartAsync(new ConformanceSetup
        {
            Middleware = application => application.Use(async (context, next) =>
            {
                if (context.Request.Path == "/forget-flash")
                {
                    context.Inertia().ForgetFlashed("success").Flash("remaining", "kept");
                    if (redirectAgain) context.Response.Redirect("/page");
                    else await Render("Page").ExecuteAsync(context);
                    return;
                }
                await next(context);
            }),
        });
        using var client = app.CreateClient();
        using var submit = await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" });
        using var response = await client.InertiaGetAsync("/forget-flash");
        var page = redirectAgain ? await (await client.InertiaGetAsync("/page")).PageAsync() : await response.PageAsync();
        JsonAssert.Equal("""{"remaining":"kept"}""", page["flash"]);
        JsonAssert.Missing(await (await client.InertiaGetAsync("/page")).PageAsync(), "flash");
    }

    [Fact]
    public async Task A_successful_response_with_a_location_header_consumes_flash_instead_of_reflashing()
    {
        await using var app = await StartAsync(new ConformanceSetup
        {
            Middleware = application => application.Use(async (context, next) =>
            {
                if (context.Request.Path == "/success-location")
                {
                    context.Response.Headers.Location = "/page";
                    await context.Response.WriteAsync("ok");
                    return;
                }
                await next(context);
            }),
        });
        using var client = app.CreateClient();
        using var submit = await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" });
        using var response = await client.InertiaGetAsync("/success-location");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonAssert.Missing(await (await client.InertiaGetAsync("/page")).PageAsync(), "flash");
    }

    [Theory]
    [InlineData("/clear-history", "clearHistory")]
    [InlineData("/preserve-fragment", "preserveFragment")]
    public async Task History_flags_survive_the_redirect_once(string url, string flag)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.Found, (await client.InertiaGetAsync(url)).StatusCode);
        Assert.True((bool)(await (await client.InertiaGetAsync("/page")).PageAsync())[flag]!);
        JsonAssert.Missing(await (await client.InertiaGetAsync("/page")).PageAsync(), flag);
    }

    [Fact]
    public async Task Encrypt_history_globally_and_per_request()
    {
        await using (var app = await StartAsync())
        {
            var client = app.CreateClient();
            JsonAssert.Missing(await (await client.InertiaGetAsync("/page")).PageAsync(), "encryptHistory");
            Assert.True((bool)(await (await client.InertiaGetAsync("/encrypt")).PageAsync())["encryptHistory"]!);
        }

        await using (var app = await StartAsync(new ConformanceSetup { Inertia = o => o.EncryptHistory = true }))
        {
            var client = app.CreateClient();
            Assert.True((bool)(await (await client.InertiaGetAsync("/page")).PageAsync())["encryptHistory"]!);
            JsonAssert.Missing(await (await client.InertiaGetAsync("/isolated/1")).PageAsync(), "encryptHistory"); // per-request opt-out
        }
    }

    [Theory]
    [InlineData(null, "/page", "/page")]
    [InlineData(null, "/page/", "/page/")]
    [InlineData(null, "/page?b=2&a=1", "/page?b=2&a=1")]
    [InlineData(null, "/page?q=caf%C3%A9", "/page?q=caf%C3%A9")]
    [InlineData(null, "/caf%C3%A9", "/caf%C3%A9")]
    [InlineData("/base", "/base/page?x=1", "/base/page?x=1")]
    [InlineData("/base", "/base/page/", "/base/page/")]
    [InlineData("/base", "/base/caf%C3%A9?q=%C3%A9", "/base/caf%C3%A9?q=%C3%A9")]
    public async Task The_url_keeps_path_base_query_unicode_and_trailing_slash(string? pathBase, string url, string expected)
    {
        await using var app = await StartAsync(new ConformanceSetup { PathBase = pathBase });
        var page = await (await app.CreateClient().InertiaGetAsync(url)).PageAsync();
        Assert.Equal(expected, (string?)page["url"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Error_pages_keep_their_status_code(bool inertia)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = inertia ? await client.InertiaGetAsync("/error-page") : await client.GetAsync("/error-page", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (int)(await response.PageAsync())["props"]!["status"]!);
    }

    [Fact]
    public async Task Partial_reloads_filter_props()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        JsonAssert.Equal("""{"errors":{},"a":1,"b":2,"c":3}""", (await (await client.InertiaGetAsync("/partial")).PageAsync())["props"]);

        var partial = await client.InertiaGetAsync("/partial", r =>
        {
            r.WithHeader(InertiaHeaders.PartialComponent, "Partial");
            r.WithHeader(InertiaHeaders.PartialData, "b,d");
        });
        JsonAssert.Equal("""{"errors":{},"b":2,"c":3,"d":4}""", (await partial.PageAsync())["props"]);
    }

    [Fact]
    public async Task Deferred_props_load_by_group()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var page = await (await client.InertiaGetAsync("/deferred")).PageAsync();
        JsonAssert.Equal("""{"errors":{},"a":1}""", page["props"]);
        JsonAssert.Equal("""{"default":["b"],"other":["c"]}""", page["deferredProps"]);

        var group = await client.InertiaGetAsync("/deferred", r =>
        {
            r.WithHeader(InertiaHeaders.PartialComponent, "Deferred");
            r.WithHeader(InertiaHeaders.PartialData, "b");
        });
        JsonAssert.Equal("""{"errors":{},"b":"b"}""", (await group.PageAsync())["props"]);
    }

    [Fact]
    public async Task Parallel_requests_are_isolated()
    {
        await using var app = await StartAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(async n =>
        {
            var client = app.CreateClient();
            var redirect = await client.InertiaAsync(HttpMethod.Post, $"/flash/{n}");
            Assert.Equal($"/isolated/{n}", redirect.Headers.Location?.OriginalString);
            return (n, page: await (await client.InertiaGetAsync($"/isolated/{n}")).PageAsync());
        }));

        foreach (var (n, page) in results)
        {
            Assert.Equal(n, (int)page["props"]!["n"]!);
            Assert.Equal(n, (int)page["props"]!["shared"]!);
            Assert.Equal(n, (int)page["flash"]!["n"]!);
            Assert.Equal(n % 2 == 0, page.ContainsKey("encryptHistory"));
        }
    }

    [Fact]
    public async Task A_tampered_state_cookie_is_ignored_and_deleted()
    {
        if (!THost.StateInCookie)
        {
            return; // the TempData store has its own unreadable-state test
        }

        await using var app = await StartAsync();
        var client = app.Server.CreateClient(); // no cookie container: the Cookie header is set by hand
        var request = new HttpRequestMessage(HttpMethod.Get, "/page");
        request.Headers.Add(InertiaHeaders.Inertia, "true");
        request.Headers.Add("Cookie", ".Inertia.State=CfDJ8tampered");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonAssert.Missing(await response.PageAsync(), "flash");
        Assert.Contains(".Inertia.State=;", response.Header("Set-Cookie"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_store_carries_errors_and_flash()
    {
        await using var app = await StartAsync(new ConformanceSetup { UseSession = true });
        var client = app.CreateClient();

        var failed = await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "" });
        Assert.Equal(HttpStatusCode.Found, failed.StatusCode);
        Assert.DoesNotContain(".Inertia.State", failed.Header("Set-Cookie") ?? "", StringComparison.Ordinal);
        Assert.Single((await (await client.InertiaGetAsync("/page")).PageAsync())["props"]!["errors"]!.AsObject());

        await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" });
        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        JsonAssert.Equal("""{"success":"Saved Ann"}""", page["flash"]);
        JsonAssert.Equal("{}", page["props"]!["errors"]);
        JsonAssert.Missing(await (await client.InertiaGetAsync("/page")).PageAsync(), "flash");
    }
}

internal static class JsonAssert
{
    public static void Equal(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"Expected: {JsonNode.Parse(expected)?.ToJsonString()}\nActual:   {actual?.ToJsonString()}");

    public static void Missing(JsonNode? node, string key) => Assert.False(node!.AsObject().ContainsKey(key), $"'{key}' should be missing in {node.ToJsonString()}");
}
