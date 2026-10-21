using System.Net;
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

    [Fact]
    public async Task Request_bodies_and_headers_are_recorded_with_secrets_redacted()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = await client.InertiaAsync(HttpMethod.Post, "/save", new { name = "Ann", password = "hunter2" }, r =>
        {
            r.WithHeader("Authorization", "Bearer abc");
            r.WithHeader("X-Custom", "visible");
        });
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
            app.MapPost("/save", async (HttpContext http) =>
            {
                var body = await http.Request.ReadFromJsonAsync<JsonObject>();
                _last = (string?)body?["name"];
                return Results.Redirect("/dashboard");
            });
            app.MapGet("/last", () => _last ?? "");
        }
    }
}
