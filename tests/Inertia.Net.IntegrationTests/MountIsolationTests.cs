using FastEndpoints;
using Inertia.Net.FastEndpoints;
using System.Buffers;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Inertia.Net.IntegrationTests;

/// <summary>A host app on the default mount at <c>/</c> and a named mount <c>sp</c> under <c>Map("/dashboard")</c>.</summary>
public sealed class MountIsolationTests
{
    private static async Task<WebApplication> StartAsync(Action<IServiceCollection>? services = null, Action<WebApplication>? beforeMounts = null)
    {
        var root = Path.Join(Path.GetTempPath(), "inertia-mounts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Join(root, "app.html"), "<html><body data-root=\"host\">@inertia</body></html>");
        File.WriteAllText(Path.Join(root, "sp.html"), "<html><body data-root=\"sp\">@inertia</body></html>");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddInertia(o =>
        {
            o.Version = "host-1";
            o.Share("user", "alice");
        });
        builder.Services.AddInertia("sp", o =>
        {
            o.Version = "sp-1";
            o.RootView = "sp.html";
            o.Share("product", "SP");
        });
        services?.Invoke(builder.Services);
        var app = builder.Build();
        beforeMounts?.Invoke(app);
        app.Map("/dashboard", branch =>
        {
            branch.UseInertia("sp");
            branch.UseRouting();
            branch.UseEndpoints(e =>
            {
                e.MapGet("/", () => Render("Dashboard/Index"));
                e.MapGet("/back", () => Back());
                e.MapGet("/modal", () => Modal("Dashboard/Modal").WithBaseUrl("/"));
                e.MapGet("/flash", (HttpContext http) =>
                {
                    http.Inertia().Flash("message", "from sp");
                    return Results.Redirect("/dashboard/");
                });
            });
        });
        app.UseInertia();
        app.UseRouting();
        app.MapGet("/", () => Render("Home"));
        app.MapGet("/modal", () => Modal("Host/Modal").WithBaseUrl("/"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    [Fact]
    public async Task Shared_props_stay_in_their_mount()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var sp = await (await Get(client, "/dashboard/", "sp-1")).PageAsync();
        Assert.Equal("Dashboard/Index", sp["component"]?.GetValue<string>());
        Assert.Equal("SP", sp["props"]!["product"]?.GetValue<string>());
        Assert.False(sp["props"]!.AsObject().ContainsKey("user"));
        Assert.Equal("sp-1", sp["version"]?.GetValue<string>());
        Assert.Equal("/dashboard/", sp["url"]?.GetValue<string>());

        var host = await (await Get(client, "/", "host-1")).PageAsync();
        Assert.Equal("alice", host["props"]!["user"]?.GetValue<string>());
        Assert.False(host["props"]!.AsObject().ContainsKey("product"));
        Assert.Equal("host-1", host["version"]?.GetValue<string>());
    }

    [Fact]
    public async Task A_modal_renders_its_base_page_from_its_own_mount()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var sp = await (await Get(client, "/dashboard/modal", "sp-1")).PageAsync();
        Assert.Equal("Dashboard/Index", (string?)sp["component"]);
        Assert.Equal("/dashboard/modal", (string?)sp["url"]);
        Assert.Equal("SP", (string?)sp["props"]!["product"]);
        Assert.Equal("Dashboard/Modal", (string?)sp["props"]!["_inertiaui_modal"]!["component"]);
        Assert.Equal("http://localhost/dashboard/", (string?)sp["props"]!["_inertiaui_modal"]!["baseUrl"]);

        var host = await (await Get(client, "/modal", "host-1")).PageAsync();
        Assert.Equal("Home", (string?)host["component"]);
        Assert.Equal("alice", (string?)host["props"]!["_inertiaui_modal"]!["props"]!["user"]);
    }

    [Fact]
    public async Task Each_mount_checks_only_its_own_asset_version()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await Get(client, "/dashboard/", "sp-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(client, "/", "host-1")).StatusCode);

        var stale = await Get(client, "/dashboard/", "host-1");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("sp-1", stale.Header(InertiaHeaders.Version));
        Assert.Equal(HttpStatusCode.Conflict, (await Get(client, "/", "sp-1")).StatusCode);
    }

    [Fact]
    public async Task Flash_set_in_one_mount_is_not_consumed_by_the_other()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var redirect = await Get(client, "/dashboard/flash", "sp-1");
        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        var setCookie = Assert.Single(redirect.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(".Inertia.State.sp=", setCookie, StringComparison.Ordinal);
        var cookie = setCookie[..setCookie.IndexOf(';', StringComparison.Ordinal)];

        var host = await Get(client, "/", "host-1", cookie);
        Assert.False(host.Headers.Contains("Set-Cookie")); // the host neither reads nor clears the mount's cookie
        Assert.False((await host.PageAsync()).ContainsKey("flash"));

        var sp = await (await Get(client, "/dashboard/", "sp-1", cookie)).PageAsync();
        Assert.Equal("from sp", sp["flash"]?["message"]?.GetValue<string>());
    }

    [Fact]
    public async Task Each_mount_renders_its_own_root_view()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        Assert.Contains("data-root=\"sp\"", await client.GetStringAsync("/dashboard/", TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("data-root=\"host\"", await client.GetStringAsync("/", TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_keyed_root_view_replaces_the_mount_template()
    {
        await using var app = await StartAsync(s => s.AddKeyedSingleton<IInertiaRootView, EmbeddedRootView>("sp"));
        var client = app.GetTestClient();

        var html = await client.GetStringAsync("/dashboard/", TestContext.Current.CancellationToken);
        Assert.StartsWith("<!-- embedded -->", html, StringComparison.Ordinal);
        Assert.Contains("Dashboard", html, StringComparison.Ordinal); // the page JSON escapes "/" in HTML
        Assert.Contains("data-root=\"host\"", await client.GetStringAsync("/", TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Back_falls_back_to_the_mount_root()
    {
        await using var app = await StartAsync();
        var back = await app.GetTestClient().GetAsync("/dashboard/back", TestContext.Current.CancellationToken);
        Assert.Equal("/dashboard/", back.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Error_pages_render_the_mount_component()
    {
        await using var app = await StartAsync(
            s =>
            {
                s.AddInertiaErrorPages(o => o.Component = "Error");
                s.AddInertiaErrorPages("sp", o => o.Component = "Sp/Error");
            },
            a => a.UseInertiaErrorPages());
        var client = app.GetTestClient();

        var sp = await (await Get(client, "/dashboard/missing", "sp-1")).PageAsync();
        Assert.Equal("Sp/Error", sp["component"]?.GetValue<string>());
        Assert.Equal(404, sp["props"]!["status"]?.GetValue<int>());

        var host = await (await Get(client, "/missing", "host-1")).PageAsync();
        Assert.Equal("Error", host["component"]?.GetValue<string>());
    }

    [Fact]
    public async Task Head_tags_from_middleware_use_each_mounts_title_template_also_on_error_pages()
    {
        await using var app = await StartAsync(
            s =>
            {
                s.Configure<InertiaOptions>(o => o.Head.TitleTemplate = "{0} - Host");
                s.Configure<InertiaOptions>("sp", o => o.Head.TitleTemplate = "{0} | SP");
                s.AddInertiaErrorPages("sp", _ => { });
            },
            a =>
            {
                a.UseInertiaErrorPages();
                a.Use((http, next) =>
                {
                    http.Inertia().Head.Title("Shop");
                    return next(http);
                });
            });
        var client = app.GetTestClient();

        var host = await (await Get(client, "/", "host-1")).PageAsync();
        Assert.Equal("<title data-inertia=\"title\">Shop - Host</title>", (string?)host["props"]!["head"]![0]);
        var error = await (await Get(client, "/dashboard/missing", "sp-1")).PageAsync();
        Assert.Equal(404, (int?)error["props"]!["status"]);
        Assert.Equal("<title data-inertia=\"title\">Shop | SP</title>", (string?)error["props"]!["head"]![0]);
    }

    [Fact]
    public async Task A_mount_without_error_pages_leaves_errors_alone()
    {
        await using var app = await StartAsync(s => s.AddInertiaErrorPages(), a => a.UseInertiaErrorPages());
        var response = await Get(app.GetTestClient(), "/dashboard/missing", "sp-1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Header(InertiaHeaders.Inertia));
    }

    [Fact]
    public void Use_inertia_requires_the_mount_registration()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddInertia();
        using var app = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() => app.UseInertia("missing"));
        Assert.Contains("AddInertia(\"missing\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mounts_must_use_distinct_state_cookies()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddInertia();
        builder.Services.AddInertia("sp", o => o.State.CookieName = ".Inertia.State");
        using var app = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() => app.UseInertia("sp"));
        Assert.Contains(".Inertia.State", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Named_mounts_default_to_their_own_cookie_names()
    {
        var services = new ServiceCollection().AddInertia("sp", null).BuildServiceProvider();
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<InertiaOptions>>().Get("sp");
        Assert.Equal(".Inertia.State.sp", options.State.CookieName);
        Assert.Equal(".Inertia.Previous.sp", options.State.PreviousUrlCookieName);
        Assert.Equal(".Inertia.State", services.GetRequiredService<Microsoft.Extensions.Options.IOptions<InertiaOptions>>().Value.State.CookieName);
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string url, string version, string? cookie = null) =>
        client.InertiaGetAsync(url, request =>
        {
            request.WithHeader(InertiaHeaders.Version, version);
            if (cookie is not null) request.WithHeader("Cookie", cookie);
        });

    private sealed class EmbeddedRootView : IInertiaRootView
    {
        public async ValueTask RenderAsync(InertiaRootViewContext context)
        {
            var writer = new ArrayBufferWriter<byte>();
            writer.Write("<!-- embedded -->"u8);
            context.WriteBody(writer);
            await context.HttpContext.Response.Body.WriteAsync(writer.WrittenMemory, context.HttpContext.RequestAborted);
        }
    }
}

/// <summary>The named mount <c>sp</c> under <c>Map("/dashboard")</c> serving MVC controllers or FastEndpoints endpoints.</summary>
// The FastEndpoints case keeps its configuration in statics: run with the other FastEndpoints apps, never concurrently.
[Collection("FastEndpoints")]
public sealed class MountHostParityTests
{
    private static async Task<WebApplication> StartAsync(string host)
    {
        var root = Path.Join(Path.GetTempPath(), "inertia-mount-hosts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Join(root, "app.html"), "<html><body data-root=\"host\">@inertia</body></html>");
        File.WriteAllText(Path.Join(root, "sp.html"), "<html><body data-root=\"sp\">@inertia</body></html>");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ConformanceCounters>();
        builder.Services.AddInertia(o => o.Version = "host-1");
        builder.Services.AddInertia("sp", o =>
        {
            o.Version = "sp-1";
            o.RootView = "sp.html";
            o.Share("product", "SP");
        });
        if (host == "mvc")
        {
            MvcHost.ConfigureServices(builder.Services);
        }
        else
        {
            FastEndpointsHost.ConfigureServices(builder.Services);
        }

        var app = builder.Build();
        app.Map("/dashboard", branch =>
        {
            branch.UseInertia("sp");
            branch.UseRouting();
            branch.UseEndpoints(e =>
            {
                if (host == "mvc")
                {
                    e.MapControllers();
                }
                else
                {
                    e.MapFastEndpoints(c => c.ConfigureInertia(ep => ep.AllowAnonymous()));
                }
            });
        });
        app.UseInertia();
        app.MapGet("/", () => Render("Home"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    [Theory]
    [InlineData("mvc")]
    [InlineData("fastendpoints")]
    public async Task Pages_modals_and_redirects_use_the_mount(string host)
    {
        await using var app = await StartAsync(host);
        var client = app.GetTestClient();

        var page = await (await client.InertiaGetAsync("/dashboard/page", r => r.WithHeader(InertiaHeaders.Version, "sp-1"))).PageAsync();
        Assert.Equal("Page", (string?)page["component"]);
        Assert.Equal("SP", (string?)page["props"]!["product"]);
        Assert.Equal("sp-1", (string?)page["version"]);
        Assert.Equal("/dashboard/page", (string?)page["url"]);

        var stale = await client.InertiaGetAsync("/dashboard/page", r => r.WithHeader(InertiaHeaders.Version, "host-1"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var html = await client.GetStringAsync(new Uri("/dashboard/page", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Contains("data-root=\"sp\"", html, StringComparison.Ordinal);

        var modal = await (await client.GetAsync(new Uri("/dashboard/modal/1", UriKind.Relative), TestContext.Current.CancellationToken)).PageAsync();
        Assert.Equal("Page", (string?)modal["component"]);
        Assert.Equal("/dashboard/modal/1", (string?)modal["url"]);
        Assert.Equal("http://localhost/dashboard/page", (string?)modal["props"]!["_inertiaui_modal"]!["baseUrl"]);

        var redirect = await client.InertiaAsync(HttpMethod.Put, "/dashboard/redirect", configure: r => r.WithHeader(InertiaHeaders.Version, "sp-1"));
        Assert.Equal(HttpStatusCode.SeeOther, redirect.StatusCode);
    }
}
