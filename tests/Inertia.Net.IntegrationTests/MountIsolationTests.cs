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

        var relabelled = ".Inertia.State" + cookie[cookie.IndexOf('=', StringComparison.Ordinal)..];
        var foreign = await Get(client, "/", "host-1", relabelled);
        JsonAssert.Missing(await foreign.PageAsync(), "flash");
        Assert.Contains("expires=", foreign.Header("Set-Cookie"), StringComparison.OrdinalIgnoreCase);

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

    /// <summary>Renders <c>render:{host of the SSR URL}:{call}</c>, after <see cref="Gate"/> opens.</summary>
    private sealed class MountRenderer : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "sp-renderer")
            {
                Assert.True(request.Options.TryGetValue(SsrHttpTransport.MountKey, out var mount));
                Assert.Equal("sp", mount);
            }
            Interlocked.Increment(ref _calls);
            await Gate.Task.WaitAsync(cancellationToken);
            var json = $$"""{"head":[],"body":"<div id=\"app\">render:{{request.RequestUri!.Host}}</div>"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Named_mounts_use_their_own_SSR_connection_timeout()
    {
        var services = new ServiceCollection();
        services.AddInertia(o => o.Ssr.ConnectTimeout = TimeSpan.FromSeconds(2));
        services.AddInertia("sp", o => o.Ssr.ConnectTimeout = TimeSpan.FromSeconds(7));
        using var provider = services.BuildServiceProvider();
        using var transport = new HttpMessageInvoker(SsrHttpTransport.Create(provider, timeout => new TimeoutProbe(timeout)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost");
        using var first = await transport.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("2", first.ReasonPhrase);
        using var mounted = new HttpRequestMessage(HttpMethod.Get, "http://localhost");
        mounted.Options.Set(SsrHttpTransport.MountKey, "sp");
        using var second = await transport.SendAsync(mounted, TestContext.Current.CancellationToken);
        Assert.Equal("7", second.ReasonPhrase);
    }

    private sealed class TimeoutProbe(TimeSpan timeout) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { ReasonPhrase = timeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    [Fact]
    public async Task Cached_renders_stay_in_their_mount_for_identical_page_json()
    {
        var renderer = new MountRenderer();
        var root = Path.Join(Path.GetTempPath(), "inertia-mount-ssr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Join(root, "app.html"), "<html><body>@inertia</body></html>");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => renderer);
        // Same version, root view and props: only the renderer differs, so both mounts produce byte-identical page JSON.
        void Configure(InertiaOptions o, string ssr)
        {
            o.Version = "1";
            o.Ssr.Mode = SsrMode.Preferred;
            o.Ssr.Urls.Add(ssr);
            o.Ssr.Cache.Duration = TimeSpan.FromMinutes(5);
        }

        builder.Services.AddInertia(o => Configure(o, "http://default-renderer"));
        builder.Services.AddInertia("sp", o => Configure(o, "http://sp-renderer"));
        await using var app = builder.Build();
        app.MapWhen(http => http.Request.Host.Host == "sp.example", branch =>
        {
            branch.UseInertia("sp");
            branch.Run(http => Render("Home").WithSsrCache(TimeSpan.FromMinutes(5)).ExecuteAsync(http));
        });
        app.UseInertia();
        app.Run(http => Render("Home").WithSsrCache(TimeSpan.FromMinutes(5)).ExecuteAsync(http));
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();
        async Task<string> Visit(string host)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://" + host + "/");
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        // Simultaneous first visits: neither may join the other's fill.
        var first = new[] { Visit("default.example"), Visit("sp.example"), Visit("default.example"), Visit("sp.example") };
        renderer.Gate.SetResult();
        var cold = await Task.WhenAll(first);
        Assert.All(new[] { cold[0], cold[2] }, html => Assert.Contains("render:default-renderer", html, StringComparison.Ordinal));
        Assert.All(new[] { cold[1], cold[3] }, html => Assert.Contains("render:sp-renderer", html, StringComparison.Ordinal));

        // Warm: each mount reads its own entry, with no new render.
        var calls = renderer.Calls;
        Assert.Contains("render:default-renderer", await Visit("default.example"), StringComparison.Ordinal);
        Assert.Contains("render:sp-renderer", await Visit("sp.example"), StringComparison.Ordinal);
        Assert.Equal(calls, renderer.Calls);
        Assert.InRange(calls, 2, 4); // a visit to the same mount may coalesce with its first fill, never with the other mount's
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
                    e.MapFastEndpoints(c => c.ConfigureInertia(FastEndpointsHost.AllowAnonymous));
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
    public async Task Pages_and_redirects_use_the_mount(string host)
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

        var redirect = await client.InertiaAsync(HttpMethod.Put, "/dashboard/redirect", configure: r => r.WithHeader(InertiaHeaders.Version, "sp-1"));
        Assert.Equal(HttpStatusCode.SeeOther, redirect.StatusCode);
    }
}
