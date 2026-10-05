using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inertia.Net.IntegrationTests;

/// <summary>
/// A framework host (Minimal API, MVC, FastEndpoints) that implements the route contract documented on
/// <see cref="ProtocolConformanceTests{THost}"/>.
/// </summary>
public interface IConformanceHost
{
    /// <summary>Adds the framework's services (e.g. <c>AddValidation()</c>, <c>AddControllers()</c>).</summary>
    static abstract void ConfigureServices(IServiceCollection services);

    /// <summary>Maps the route contract. Called after <c>UseInertia()</c>.</summary>
    static abstract void MapRoutes(WebApplication app);

    /// <summary>True when the host needs <c>app.UseSession()</c> before <c>UseInertia()</c> (it registers the session services itself).</summary>
    static virtual bool UsesSession => false;

    /// <summary>False for hosts whose state store is not the <c>.Inertia.State</c> cookie (the suite then skips the cookie-specific assertions).</summary>
    static virtual bool StateInCookie => true;
}

/// <summary>Counts handler invocations, so tests can prove a handler did not run.</summary>
public sealed class ConformanceCounters
{
    private int _page;

    public int Page => Volatile.Read(ref _page);

    public void HitPage() => Interlocked.Increment(ref _page);
}

/// <summary>Variations of the test app.</summary>
public sealed record ConformanceSetup
{
    public Action<InertiaOptions>? Inertia { get; init; }

    public bool UseSession { get; init; }

    public string? PathBase { get; init; }
}

/// <summary>A started TestServer app for one <see cref="IConformanceHost"/>.</summary>
public sealed class ConformanceApp : IAsyncDisposable
{
    private static readonly string ContentRoot = CreateContentRoot();

    private ConformanceApp(WebApplication app)
    {
        App = app;
        Server = app.GetTestServer();
        Counters = app.Services.GetRequiredService<ConformanceCounters>();
    }

    public WebApplication App { get; }

    public TestServer Server { get; }

    public ConformanceCounters Counters { get; }

    public static async Task<ConformanceApp> StartAsync<THost>(ConformanceSetup? setup = null)
        where THost : IConformanceHost
    {
        setup ??= new ConformanceSetup();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = ContentRoot, EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<ConformanceCounters>();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddInertia(o => setup.Inertia?.Invoke(o));
        if (setup.UseSession)
        {
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession();
            builder.Services.Configure<InertiaOptions>(o => o.State.UseSession());
        }

        THost.ConfigureServices(builder.Services);
        var app = builder.Build();
        if (setup.PathBase is { } pathBase)
        {
            app.UsePathBase(pathBase);
        }

        if (setup.UseSession || THost.UsesSession)
        {
            app.UseSession();
        }

        app.UseInertia();
        THost.MapRoutes(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new ConformanceApp(app);
    }

    /// <summary>A client that keeps cookies (one browser) and does not follow redirects.</summary>
    public HttpClient CreateClient() =>
        new(new CookieContainerHandler { InnerHandler = Server.CreateHandler() }) { BaseAddress = Server.BaseAddress };

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }

    private static string CreateContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "inertia-conformance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "app.html"), "<!DOCTYPE html><html><head>@inertiaHead</head><body>@inertia</body></html>");
        return root;
    }
}

/// <summary>Request helpers mimicking the Inertia client.</summary>
public static class InertiaClientExtensions
{
    public static Task<HttpResponseMessage> InertiaAsync(this HttpClient client, HttpMethod method, string url, object? json = null, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(InertiaHeaders.Inertia, "true");
        request.Headers.Add("Accept", "text/html, application/xhtml+xml");
        if (json is not null)
        {
            request.Content = JsonContent.Create(json);
        }

        configure?.Invoke(request);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static Task<HttpResponseMessage> InertiaGetAsync(this HttpClient client, string url, Action<HttpRequestMessage>? configure = null) =>
        client.InertiaAsync(HttpMethod.Get, url, configure: configure);

    public static async Task<JsonObject> PageAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (!body.StartsWith('{'))
        {
            const string open = "type=\"application/json\">";
            var start = body.IndexOf(open, StringComparison.Ordinal) + open.Length;
            body = body[start..body.IndexOf("</script>", start, StringComparison.Ordinal)];
        }

        return JsonNode.Parse(body)!.AsObject();
    }

    public static string? Header(this HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values) ? string.Join(", ", values) : null;

    public static void WithHeader(this HttpRequestMessage request, string name, string value) => request.Headers.TryAddWithoutValidation(name, value);
}
