using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Inertia.Net.Mvc;

namespace Inertia.Net.IntegrationTests;

public sealed class MvcConformanceTests : ProtocolConformanceTests<MvcHost>;

/// <summary>Controller-specific behavior: ModelState errors and controllers returning the core results.</summary>
public sealed class MvcModelStateTests
{
    private static Task<ConformanceApp> StartAsync() => ConformanceApp.StartAsync<MvcHost>();

    [Fact]
    public async Task Nested_model_paths_use_json_names_and_all_failures_are_reported()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = await client.InertiaAsync(HttpMethod.Post, "/mvc/nested", new { address = new { street = "" } });
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        Assert.Equal(["address.street", "firstName"], page["props"]!["errors"]!.AsObject().Select(p => p.Key).Order());
    }

    [Fact]
    public async Task Non_inertia_requests_keep_the_controllers_own_answer()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().PostAsJsonAsync("/form", new { name = "" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_controllers_redirect_back_for_inertia_requests_and_keep_the_400_otherwise()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var inertia = await client.InertiaAsync(HttpMethod.Post, "/mvc/api-form", new { name = "" }, r => r.WithHeader(InertiaHeaders.ErrorBag, "login"));
        Assert.Equal(HttpStatusCode.Found, inertia.StatusCode);
        Assert.Equal("/", inertia.Headers.Location?.OriginalString);
        var page = await (await client.InertiaGetAsync("/page", r => r.WithHeader(InertiaHeaders.ErrorBag, "login"))).PageAsync();
        Assert.Equal(["name"], page["props"]!["errors"]!["login"]!.AsObject().Select(p => p.Key));

        var plain = await client.PostAsJsonAsync("/mvc/api-form", new { name = "" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
        Assert.Equal("application/problem+json", plain.Content.Headers.ContentType?.MediaType);

        var valid = await client.InertiaAsync(HttpMethod.Post, "/mvc/api-form", new { name = "Ann" });
        Assert.Equal("/page", valid.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Controller_returning_back_with_errors_and_flash()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = await client.InertiaAsync(HttpMethod.Post, "/mvc/back");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/fallback", response.Headers.Location?.OriginalString);

        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        JsonAssert.Equal("""{"name":"Nope"}""", page["props"]!["errors"]);
        Assert.Equal("Check the form", (string?)page["flash"]!["toast"]);
    }
}

/// <summary>The Razor root view and its tag helpers.</summary>
public sealed class MvcRazorTests
{
    private const string Manifest = """
        {
          "src/app.ts": { "file": "assets/app-1.js", "src": "src/app.ts", "isEntry": true, "css": ["assets/app-1.css"] },
          "src/other.ts": { "file": "assets/other-2.js", "src": "src/other.ts", "isEntry": true }
        }
        """;

    private static async Task<ConformanceApp> StartAsync(Action<InertiaOptions>? configure = null)
    {
        var manifest = Path.Combine(Path.GetTempPath(), "inertia-razor-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(manifest, Manifest, TestContext.Current.CancellationToken);
        return await ConformanceApp.StartAsync<Host>(new ConformanceSetup
        {
            Inertia = o =>
            {
                o.Vite.ManifestPath = manifest;
                configure?.Invoke(o);
            },
        });
    }

    private static async Task<string> HtmlAsync(HttpClient client, string url = "/mvc/razor")
    {
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Renders_the_view_with_vite_tags_view_data_and_the_client_side_body()
    {
        await using var app = await StartAsync();
        var html = await HtmlAsync(app.CreateClient());

        Assert.Contains("<title>&lt;Razor&gt; &amp; co</title>", html, StringComparison.Ordinal);
        Assert.Contains("""<link rel="stylesheet" href="/build/assets/app-1.css">""", html, StringComparison.Ordinal);
        Assert.Contains("""<script type="module" src="/build/assets/app-1.js"></script>""", html, StringComparison.Ordinal);
        Assert.Contains("""<script type="module" src="/build/assets/other-2.js"></script>""", html, StringComparison.Ordinal);
        Assert.DoesNotContain("@react-refresh", html, StringComparison.Ordinal);
        Assert.Contains("""<script data-page="app" type="application/json">""", html, StringComparison.Ordinal);
        Assert.Contains("""<div id="app"></div>""", html, StringComparison.Ordinal);
        Assert.Contains("""<head>""", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<inertia", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<vite", html, StringComparison.Ordinal);

        var response = await app.CreateClient().GetAsync("/mvc/razor", TestContext.Current.CancellationToken);
        Assert.Equal("hello", (string?)(await response.PageAsync())["props"]!["message"]);
    }

    [Fact]
    public async Task Inertia_requests_still_get_json()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var version = (string?)(await (await client.GetAsync("/mvc/razor", TestContext.Current.CancellationToken)).PageAsync())["version"];
        var response = await client.InertiaGetAsync("/mvc/razor", r => r.WithHeader(InertiaHeaders.Version, version!));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Razor", (string?)(await response.PageAsync())["component"]);
    }

    [Fact]
    public async Task Dev_server_tags_and_react_refresh()
    {
        await using var app = await StartAsync(o => o.Vite.DevServerUrl = "http://localhost:5173");
        var html = await HtmlAsync(app.CreateClient());

        Assert.Contains("http://localhost:5173/@react-refresh", html, StringComparison.Ordinal);
        Assert.Contains("""<script type="module" src="http://localhost:5173/@vite/client"></script>""", html, StringComparison.Ordinal);
        Assert.Contains("""<script type="module" src="http://localhost:5173/src/other.ts"></script>""", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("@react-refresh", StringComparison.Ordinal) < html.IndexOf("@vite/client", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ssr_head_and_body_come_from_a_single_ssr_call()
    {
        await using var app = await StartAsync();
        var ssr = app.App.Services.GetRequiredService<FakeSsr>();
        ssr.Result = new SsrRender("<title>SSR title</title>", "<div id=\"app\">ssr body</div>");

        var html = await HtmlAsync(app.CreateClient());

        Assert.Equal(1, ssr.Calls);
        Assert.Contains("<title>SSR title</title>", html, StringComparison.Ordinal);
        Assert.Contains("""<div id="app">ssr body</div>""", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-page", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ssr_fallback_renders_the_client_body_with_an_empty_head()
    {
        await using var app = await StartAsync();
        var ssr = app.App.Services.GetRequiredService<FakeSsr>();
        var html = await HtmlAsync(app.CreateClient());

        Assert.Equal(1, ssr.Calls);
        Assert.Contains("""<script data-page="app" type="application/json">""", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_root_view_names_another_razor_view()
    {
        await using var app = await StartAsync();
        var html = await HtmlAsync(app.CreateClient(), "/mvc/razor-other");
        Assert.StartsWith("<html><body>other root:<script data-page=\"app\"", html, StringComparison.Ordinal);
    }

    /// <summary>A scriptable stand-in for the SSR server.</summary>
    internal sealed class FakeSsr : IInertiaSsrRenderer
    {
        private int _calls;

        public SsrRender? Result { get; set; }

        public int Calls => _calls;

        public ValueTask<SsrRender?> RenderAsync(InertiaRootViewContext context)
        {
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult(Result);
        }
    }

    public sealed class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews().AddInertiaMvc(o => o.UseRazorRootView("App"));
            services.AddSingleton<FakeSsr>();
            services.Replace(ServiceDescriptor.Singleton<IInertiaSsrRenderer>(sp => sp.GetRequiredService<FakeSsr>()));
        }

        public static void MapRoutes(WebApplication app) => app.MapControllers();
    }
}
