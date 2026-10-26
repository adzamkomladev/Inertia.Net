using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

/// <summary>The Inertia DevTools protocol: discovery, correlation headers, entries and the read API.</summary>
public sealed class DevToolsTests
{
    private static Task<ConformanceApp> StartAsync(Action<InertiaDevToolsOptions>? configure = null) =>
        ConformanceApp.StartAsync<Host>(new ConformanceSetup
        {
            Services = s => s.AddInertiaDevTools(o =>
            {
                o.Enabled = true;
                o.Authorize = _ => true; // the conformance apps run in Production
                configure?.Invoke(o);
            }),
        });

    private static async Task<JsonObject> EntryAsync(HttpClient client, HttpResponseMessage response)
    {
        var id = response.Header("X-Inertia-Devtools-Id");
        Assert.NotNull(id);
        var entry = await client.GetAsync($"/_inertia/devtools/entries/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        return JsonNode.Parse(await entry.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
    }

    [Fact]
    public async Task The_first_page_carries_the_discovery_tag_and_an_initial_entry()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = await client.GetAsync("/dashboard", TestContext.Current.CancellationToken);
        var id = response.Header("X-Inertia-Devtools-Id");
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains($"<script data-inertia-devtools-id type=\"application/json\">\"{id}\"</script>", html, StringComparison.Ordinal);
        Assert.Equal(id, response.Header("X-Inertia-Devtools-Parent-Out")); // a request without a parent starts a batch

        var entry = await EntryAsync(client, response);
        var meta = entry["__meta"]!;
        Assert.Equal(id, (string?)meta["id"]);
        Assert.Equal("GET", (string?)meta["method"]);
        Assert.Equal("/dashboard", (string?)meta["url"]);
        Assert.Equal(200, (int?)meta["status"]);
        Assert.Equal("initial", (string?)meta["requestType"]);
        Assert.Equal("Dashboard", (string?)meta["component"]);
        Assert.Null(meta["tabUuid"]);
        Assert.Null(meta["batchId"]);
        Assert.True((double?)meta["utime"] > 0);
        Assert.NotNull((double?)meta["serverTimingMs"]);
        Assert.Equal("/dashboard", (string?)entry["route"]!["uri"]);
        Assert.Equal("present", (string?)entry["http"]!["responseBody"]!["status"]);
        Assert.Equal("Dashboard", (string?)entry["http"]!["responseBody"]!["value"]!["component"]);
        Assert.Null(entry["renderSource"]);
        Assert.Null(entry["componentPath"]);
    }

    [Fact]
    public async Task Props_metadata_and_values_come_from_the_page_and_secrets_are_redacted()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var entry = await EntryAsync(client, await client.InertiaGetAsync("/dashboard"));

        var props = entry["props"]!;
        Assert.Equal("defer", (string?)props["stats"]!["inertiaType"]);
        Assert.Equal("sidebar", (string?)props["stats"]!["deferGroup"]);
        Assert.Equal("merge", (string?)props["feed"]!["inertiaType"]);
        Assert.Equal("append", (string?)props["feed"]!["mergeDirection"]);
        Assert.Equal("once", (string?)props["plans"]!["inertiaType"]);
        Assert.True((bool?)props["appName"]!["shared"]);

        var values = entry["propValues"]!;
        Assert.Equal("Ann", (string?)values["user"]!["name"]);
        Assert.Equal("[REDACTED]", (string?)values["user"]!["apiToken"]);
        Assert.Equal("[REDACTED]", (string?)values["secretKey"]);
        Assert.Equal("[REDACTED]", (string?)entry["http"]!["responseBody"]!["value"]!["props"]!["user"]!["apiToken"]);
        Assert.Equal("navigate", (string?)entry["__meta"]!["requestType"]);
    }

    [Fact]
    public async Task Correlation_headers_scope_entries_and_derive_the_request_type()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        void Tab(HttpRequestMessage r)
        {
            r.WithHeader("X-Inertia-Devtools-Tab", "tab-1");
            r.WithHeader("X-Inertia-Devtools-Visit", "visit-7");
            r.WithHeader("X-Inertia-Devtools-Parent", "root-id");
        }

        var navigate = await client.InertiaGetAsync("/dashboard", Tab);
        Assert.Equal("root-id", navigate.Header("X-Inertia-Devtools-Parent-Out"));
        var meta = (await EntryAsync(client, navigate))["__meta"]!;
        Assert.Equal("tab-1", (string?)meta["tabUuid"]);
        Assert.Equal("root-id", (string?)meta["batchId"]);
        Assert.Equal("visit-7", (string?)meta["visitId"]);

        async Task<string?> TypeOf(Action<HttpRequestMessage> configure, string url = "/dashboard") =>
            (string?)(await EntryAsync(client, await client.InertiaGetAsync(url, configure)))["__meta"]!["requestType"];

        Assert.Equal("partial", await TypeOf(r => r.WithHeader(InertiaHeaders.PartialComponent, "Dashboard")));
        Assert.Equal("deferred", await TypeOf(r => { r.WithHeader(InertiaHeaders.PartialComponent, "Dashboard"); r.WithHeader("X-Inertia-Devtools-Deferred", "1"); }));
        Assert.Equal("poll", await TypeOf(r => r.WithHeader("X-Inertia-Devtools-Poll", "1")));
        Assert.Equal("precognition", await TypeOf(r => r.WithHeader("Precognition", "true")));

        var prefetch = await client.InertiaGetAsync("/dashboard", r => { Tab(r); r.WithHeader(InertiaHeaders.Purpose, "prefetch"); });
        Assert.Equal(prefetch.Header("X-Inertia-Devtools-Id"), prefetch.Header("X-Inertia-Devtools-Parent-Out"));
        Assert.Equal("prefetch", (string?)(await EntryAsync(client, prefetch))["__meta"]!["requestType"]);

        var plain = await client.GetAsync("/plain", TestContext.Current.CancellationToken);
        var http = await EntryAsync(client, plain);
        Assert.Equal("http", (string?)http["__meta"]!["requestType"]);
        Assert.Null(http["__meta"]!["component"]);
        Assert.Equal("omitted", (string?)http["http"]!["responseBody"]!["status"]);
        Assert.Equal("non-inertia-response", (string?)http["http"]!["responseBody"]!["reason"]);
    }

    [Theory]
    [InlineData("application/json", "{\"name\":\"Ann\",\"password\":\"hunter2\"}")]
    [InlineData("application/x-www-form-urlencoded", "name=Ann&password=hunter2")]
    public async Task Request_bodies_and_headers_are_recorded_with_secrets_redacted(string contentType, string body)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, "/save")
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
        message.Content.Headers.ContentLength = Encoding.UTF8.GetByteCount(body);
        message.WithHeader(InertiaHeaders.Inertia, "true");
        message.WithHeader("Authorization", "Bearer abc");
        message.WithHeader("X-Custom", "visible");
        var response = await client.SendAsync(message, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("Ann", await client.GetStringAsync("/last", TestContext.Current.CancellationToken)); // the handler still read the body

        var entry = await EntryAsync(client, response);
        var request = entry["http"]!;
        Assert.Equal("Ann", (string?)request["requestBody"]!["value"]!["name"]);
        Assert.Equal("[REDACTED]", (string?)request["requestBody"]!["value"]!["password"]);
        Assert.Equal("[REDACTED]", (string?)request["requestHeaders"]!["authorization"]);
        Assert.Equal("visible", (string?)request["requestHeaders"]!["x-custom"]);
        Assert.Equal("empty", (string?)request["responseBody"]!["status"]);
        Assert.Equal("/dashboard", (string?)entry["__meta"]!["redirectLocation"]);
    }

    [Fact]
    public async Task The_list_is_newest_first_filtered_and_bounded_per_tab()
    {
        await using var app = await StartAsync(o => o.MaxEntriesPerTab = 3);
        var client = app.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await client.InertiaGetAsync("/dashboard", r => r.WithHeader("X-Inertia-Devtools-Tab", "a"));
        }

        await client.GetAsync("/plain", TestContext.Current.CancellationToken);

        async Task<JsonArray> ListAsync(string query = "") =>
            JsonNode.Parse(await client.GetStringAsync("/_inertia/devtools/entries" + query, TestContext.Current.CancellationToken))!.AsArray();

        var all = await ListAsync();
        Assert.Equal(4, all.Count); // three kept for tab "a", one without a tab; the read API is never recorded
        Assert.Equal("http", (string?)all[0]!["__meta"]!["requestType"]);
        Assert.Equal(3, (await ListAsync("?component=Dashboard")).Count);
        Assert.Single(await ListAsync("?type=http"));
        Assert.Equal(3, (await ListAsync("?exclude=http")).Count);
        Assert.Equal(2, (await ListAsync("?offset=1&limit=2")).Count);
    }

    [Fact]
    public async Task Malformed_json_is_omitted_without_changing_the_handlers_input()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        const string body = "{\"password\":\"malformed-body-secret\",";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/capture")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(InertiaHeaders.Inertia, "true");
        request.Content.Headers.ContentLength = Encoding.UTF8.GetByteCount(body);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(body, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var entry = await EntryAsync(client, response);
        Assert.Equal("omitted", (string?)entry["http"]!["requestBody"]!["status"]);
        Assert.Equal("unserializable", (string?)entry["http"]!["requestBody"]!["reason"]);
        Assert.DoesNotContain("malformed-body-secret", entry.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("malformed-body-secret", await client.GetStringAsync("/_inertia/devtools/entries", TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sensitive_queries_are_redacted_from_metadata_page_copies_and_url_headers()
    {
        await using var app = await StartAsync(o => o.RedactedKeys.Add("credential"));
        var client = app.CreateClient();
        const string query = "?ToKeN=query-secret&%74oken=duplicate-secret&credential=custom-secret&ok=visible";
        var response = await client.InertiaGetAsync("/dashboard" + query, r =>
            r.WithHeader("Referer", "https://example.test/source?api_key=referer-secret&ok=visible#kept"));
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        var actualUrl = (string?)actual["url"];
        Assert.Contains("query-secret", actualUrl, StringComparison.Ordinal);
        Assert.Contains("duplicate-secret", actualUrl, StringComparison.Ordinal);
        Assert.Contains("custom-secret", actualUrl, StringComparison.Ordinal);
        Assert.Contains("ok=visible", actualUrl, StringComparison.Ordinal);

        var entry = await EntryAsync(client, response);
        foreach (var secret in new[] { "query-secret", "duplicate-secret", "custom-secret", "referer-secret" })
        {
            Assert.DoesNotContain(secret, entry.ToJsonString(), StringComparison.Ordinal);
        }
        Assert.Contains("ok=visible", (string?)entry["__meta"]!["url"], StringComparison.Ordinal);
        Assert.Equal((string?)entry["__meta"]!["url"], (string?)entry["http"]!["responseBody"]!["value"]!["url"]);
        Assert.EndsWith("#kept", (string?)entry["http"]!["requestHeaders"]!["referer"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("location", "location")]
    [InlineData("external", "x-inertia-location")]
    [InlineData("fragment", "x-inertia-redirect")]
    public async Task Redirect_queries_are_redacted_only_in_recorded_copies(string kind, string header)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = await client.InertiaGetAsync("/redirect-secret/" + kind);
        var actual = response.Header(header)!;
        Assert.Contains("redirect-secret", actual, StringComparison.Ordinal);
        var entry = await EntryAsync(client, response);
        // The route itself contains redirect-secret; inspect the URL-valued fields rather than the request path.
        var location = (string?)entry["__meta"]!["redirectLocation"];
        Assert.DoesNotContain("=redirect-secret", location, StringComparison.Ordinal);
        Assert.Contains("ok=visible", location, StringComparison.Ordinal);
        Assert.Equal(location, (string?)entry["http"]!["responseHeaders"]![header]);
        Assert.DoesNotContain("=redirect-secret", (string?)entry["http"]!["responseHeaders"]!["content-location"], StringComparison.Ordinal);
        Assert.Contains("refresh-secret", response.Header("Refresh"), StringComparison.Ordinal);
        Assert.Contains("link-secret", response.Header("Link"), StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret", entry.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("link-secret", entry.ToJsonString(), StringComparison.Ordinal);
        Assert.EndsWith("#kept\"", (string?)entry["http"]!["responseHeaders"]!["refresh"], StringComparison.Ordinal);
        Assert.Contains("</safe?ok=visible>", (string?)entry["http"]!["responseHeaders"]!["link"], StringComparison.Ordinal);
        Assert.Contains("rel=preload", (string?)entry["http"]!["responseHeaders"]!["link"], StringComparison.Ordinal);
        if (kind != "location") Assert.EndsWith("#kept", location, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Ineligible_bodies_are_not_buffered_and_the_handler_can_read_them(bool inertia, bool unknownLength)
    {
        await using var app = await StartAsync();
        var recorder = app.App.Services.GetRequiredService<DevToolsRecorder>();
        var http = new DefaultHttpContext();
        await using var body = new NonSeekableStream(new byte[128 * 1024]);
        http.Request.Body = body;
        http.Request.ContentType = "application/json";
        if (unknownLength) http.Request.Headers.TransferEncoding = "chunked";
        else http.Request.ContentLength = body.Length;
        if (inertia) http.Request.Headers[InertiaHeaders.Inertia] = "true";
        var capture = recorder.Begin(http)!;
        Assert.Same(body, http.Request.Body);
        using var received = new MemoryStream();
        await http.Request.Body.CopyToAsync(received, TestContext.Current.CancellationToken);
        Assert.Equal(body.Length, received.Length);
        await recorder.CompleteAsync(http, capture, failed: false);
        var entry = JsonNode.Parse(await app.CreateClient().GetStringAsync("/_inertia/devtools/entries/" + capture.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal("omitted", (string?)entry["http"]!["requestBody"]!["status"]);
        Assert.Equal(!inertia ? "non-inertia-request" : unknownLength ? "streamed" : "too-large", (string?)entry["http"]!["requestBody"]!["reason"]);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    [Theory]
    [InlineData("?%74oken=encoded-secret&ok=visible")]
    [InlineData("?%63redential=encoded-secret&ok=visible")]
    public async Task Encoded_query_names_and_custom_absolute_page_urls_are_redacted(string query)
    {
        await using var app = await StartAsync(o => o.RedactedKeys.Add("credential"));
        var recorder = app.App.Services.GetRequiredService<DevToolsRecorder>();
        var http = new DefaultHttpContext();
        http.Request.PathBase = "/base";
        http.Request.Path = "/dashboard";
        http.Request.QueryString = new QueryString(query);
        var capture = recorder.Begin(http)!;
        var original = new JsonObject
        {
            ["component"] = "Dashboard",
            ["props"] = new JsonObject(),
            ["url"] = "https://example.test/base/dashboard" + query + "#kept",
        }.ToJsonString();
        capture.PageJson = Encoding.UTF8.GetBytes(original);
        await recorder.CompleteAsync(http, capture, failed: false);
        var entry = JsonNode.Parse(await app.CreateClient().GetStringAsync("/_inertia/devtools/entries/" + capture.Id, TestContext.Current.CancellationToken))!;
        Assert.DoesNotContain("encoded-secret", entry.ToJsonString(), StringComparison.Ordinal);
        Assert.StartsWith("/base/dashboard?", (string?)entry["__meta"]!["url"], StringComparison.Ordinal);
        var recordedUrl = (string?)entry["http"]!["responseBody"]!["value"]!["url"];
        Assert.StartsWith("https://example.test/base/dashboard?", recordedUrl, StringComparison.Ordinal);
        Assert.Contains("ok=visible", recordedUrl, StringComparison.Ordinal);
        Assert.EndsWith("#kept", recordedUrl, StringComparison.Ordinal);
        Assert.Equal(original, Encoding.UTF8.GetString(capture.PageJson));
    }

    [Theory]
    [InlineData("Link", "</asset?token=header-secret", true)]
    [InlineData("Link", "/asset?token=header-secret", true)]
    [InlineData("Link", "</safe>; rel=next, /asset?token=header-secret", true)]
    [InlineData("Link", "</safe?ok=visible>; rel=next", false)]
    [InlineData("Refresh", "0; url='/asset?token=header-secret'", true)]
    [InlineData("Refresh", "0; URL = /asset?token=header-secret", true)]
    [InlineData("Refresh", "0; url=\"/asset?token=header-secret", true)]
    [InlineData("Refresh", "0; /asset?token=header-secret", true)]
    [InlineData("Refresh", "5", false)]
    public async Task Url_header_variants_do_not_retain_secrets(string header, string value, bool redacted)
    {
        await using var app = await StartAsync();
        var recorder = app.App.Services.GetRequiredService<DevToolsRecorder>();
        var http = new DefaultHttpContext();
        http.Response.Headers[header] = value;
        var capture = recorder.Begin(http)!;
        await recorder.CompleteAsync(http, capture, failed: false);
        var entry = JsonNode.Parse(await app.CreateClient().GetStringAsync("/_inertia/devtools/entries/" + capture.Id, TestContext.Current.CancellationToken))!;
        Assert.DoesNotContain("header-secret", entry.ToJsonString(), StringComparison.Ordinal);
        var recorded = (string?)entry["http"]!["responseHeaders"]![header.ToLowerInvariant()];
        if (!redacted) Assert.Equal(value, recorded);
        Assert.Equal(value, http.Response.Headers[header].ToString());
    }

    [Fact]
    public async Task The_read_api_is_closed_without_authorization_and_nothing_is_recorded_when_off()
    {
        await using var closed = await StartAsync(o => o.Authorize = _ => false);
        var client = closed.CreateClient();
        var response = await client.InertiaGetAsync("/dashboard");
        Assert.NotNull(response.Header("X-Inertia-Devtools-Id"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/_inertia/devtools/entries", TestContext.Current.CancellationToken)).StatusCode);

        // Production default: off, so no headers and a closed API.
        await using var off = await ConformanceApp.StartAsync<Host>(new ConformanceSetup { Services = s => s.AddInertiaDevTools() });
        var offClient = off.CreateClient();
        Assert.Null((await offClient.InertiaGetAsync("/dashboard")).Header("X-Inertia-Devtools-Id"));
        Assert.Equal(HttpStatusCode.NotFound, (await offClient.GetAsync("/_inertia/devtools/entries", TestContext.Current.CancellationToken)).StatusCode);
    }

    public sealed record DevUser(string Name, string ApiToken);

    public sealed class Host : IConformanceHost
    {
        private static string? _last;

        public static void ConfigureServices(IServiceCollection services) => services.Configure<InertiaOptions>(o => o.Share("appName", "Demo"));

        public static void MapRoutes(WebApplication app)
        {
            app.MapInertiaDevTools();
            app.MapGet("/dashboard", () => Render("Dashboard", new InertiaProps
            {
                ["user"] = new InertiaProps { ["name"] = "Ann", ["apiToken"] = "t0k3n" },
                ["secretKey"] = "s3cr3t",
                ["stats"] = Defer(() => 1, "sidebar"),
                ["feed"] = Merge(new[] { 1, 2 }),
                ["plans"] = Once(() => "free"),
            }));
            app.MapGet("/plain", () => Results.Text("plain"));
            app.MapPost("/capture", async (HttpContext http) =>
            {
                using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
                return Results.Text(await reader.ReadToEndAsync(http.RequestAborted));
            });
            app.MapGet("/redirect-secret/{kind}", (HttpContext http, string kind) =>
            {
                var target = (kind == "external" ? "https://example.test" : "") + "/dashboard?token=redirect-secret&ok=visible" + (kind == "location" ? "" : "#kept");
                http.Response.Headers.ContentLocation = target;
                http.Response.Headers["Refresh"] = "0; url=\"/dashboard?token=refresh-secret&ok=visible#kept\"";
                http.Response.Headers["Link"] = "</safe?ok=visible>; rel=next, </asset?token=link-secret>; rel=preload, <https://example.test/other?api_key=link-secret#kept>; rel=next";
                return Results.Redirect(target);
            });
            app.MapPost("/save", async (HttpContext http) =>
            {
                _last = http.Request.HasFormContentType ? (string?)(await http.Request.ReadFormAsync())["name"]
                    : (string?)(await http.Request.ReadFromJsonAsync<JsonObject>())?["name"];
                return Results.Redirect("/dashboard");
            });
            app.MapGet("/last", () => _last ?? "");
        }
    }
}
