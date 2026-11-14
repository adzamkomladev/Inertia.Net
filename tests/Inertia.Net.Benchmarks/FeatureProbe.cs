using System.Diagnostics;
using System.Text;
using Inertia.Net.Live.SignalR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Benchmarks;

/// <summary>
/// <c>--feature-probe</c>: the per-request cost of the 1.6-1.9 features. The 20-prop page with five head tags (HTML and JSON), five
/// lazy props with and without <c>.Live(evt)</c>, resolving a typed live-channel authorizer as the hub does per invocation (in a new
/// scope, and the options' factory alone), the hub's origin check for an origin third in <c>AllowedOrigins</c>, and a GET
/// through <c>UseInertiaAntiforgery()</c> that is not a page load. <c>--feature-probe check</c> prints the bodies instead, to compare
/// output before and after a change; <c>--feature-probe {text}</c> runs only the rows whose name contains the text.
/// </summary>
internal static class FeatureProbe
{
    private static readonly int[] Ids = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private static readonly LiveEvent Changed = new("Changed", LiveChannel.Private("orders.1"));

    internal static async Task RunAsync(bool check, string? only = null)
    {
        _only = only;
        var services = BenchApp.CreateServices();
        var version = BenchApp.Version(services);
        var json = BenchApp.CreateContext(services, "/users", (InertiaHeaders.Inertia, "true"), (InertiaHeaders.Version, version));
        var html = BenchApp.CreateContext(services);

        if (check)
        {
            foreach (var (name, context, render) in new (string, DefaultHttpContext, Func<HttpContext, Task>)[]
            {
                ("html head", html, c => WithHead().ExecuteAsync(c)),
                ("json head", json, c => WithHead().ExecuteAsync(c)),
                ("json live", json, c => Render(BenchApp.Component, LiveProps(live: true)).ExecuteAsync(c)),
            })
            {
                context.Response.Body = new MemoryStream();
                context.Features.Set<InertiaFeature>(null);
                await render(context);
                Console.WriteLine($"{name}: {Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())}\n");
                context.Response.Body = Stream.Null;
            }

            return;
        }

        var hub = new ServiceCollection();
        hub.AddLogging();
        hub.AddInertia();
        hub.AddInertiaLive().AddSignalR(o => o.Authorize<BenchAuthorizer>());
        var hubServices = hub.BuildServiceProvider();
        var scopes = hubServices.GetRequiredService<IServiceScopeFactory>();
        var authorizerFactory = hubServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<InertiaLiveSignalROptions>>().Value.AuthorizerFactory!;

        var origin = new LiveOrigin(Microsoft.Extensions.Logging.Abstractions.NullLogger<LiveOrigin>.Instance);
        string[] allowedOrigins = ["https://admin.example.com", "https://docs.example.com", "https://app.example.com"];
        var crossOrigin = BenchApp.CreateContext(hubServices, "/live/negotiate");
        crossOrigin.Request.Headers.Origin = "https://app.example.com";

        var antiforgeryServices = BenchApp.CreateServices(configureServices: s => s.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN"));
        var app = new ApplicationBuilder(antiforgeryServices);
        app.UseInertiaAntiforgery();
        app.Run(static context => Task.CompletedTask);
        var pipeline = app.Build();
        var api = BenchApp.CreateContext(antiforgeryServices, "/api/ping");
        api.SetEndpoint(new Endpoint(static context => Task.CompletedTask, EndpointMetadataCollection.Empty, "ping"));

        for (var repetition = 1; repetition <= 3; repetition++)
        {
            await MeasureAsync(repetition, "html", 50000, html, c => Render(BenchApp.Component, BenchApp.Props20()).ExecuteAsync(c));
            await MeasureAsync(repetition, "html head", 50000, html, c => WithHead().ExecuteAsync(c));
            await MeasureAsync(repetition, "json", 50000, json, c => Render(BenchApp.Component, BenchApp.Props20()).ExecuteAsync(c));
            await MeasureAsync(repetition, "json head", 50000, json, c => WithHead().ExecuteAsync(c));
            await MeasureAsync(repetition, "json 5 lazy", 50000, json, c => Render(BenchApp.Component, LiveProps(live: false)).ExecuteAsync(c));
            await MeasureAsync(repetition, "json 5 live", 50000, json, c => Render(BenchApp.Component, LiveProps(live: true)).ExecuteAsync(c));
            await MeasureAsync(repetition, "hub authorizer", 200000, null, _ =>
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<ILiveChannelAuthorizer>();
                return Task.CompletedTask;
            });
            await MeasureAsync(repetition, "hub authorizer factory", 1000000, null, _ =>
            {
                authorizerFactory(hubServices);
                return Task.CompletedTask;
            });
            await MeasureAsync(repetition, "live origin, third allowed", 1000000, null, _ =>
            {
                if (!origin.IsAllowed(crossOrigin.Request, allowedOrigins)) throw new InvalidOperationException("The origin is allowed.");
                return Task.CompletedTask;
            });
            await MeasureAsync(repetition, "antiforgery GET", 200000, api, c => pipeline(c));
        }
    }

    private static InertiaResult WithHead() => Render(BenchApp.Component, BenchApp.Props20()).WithHead(h => h
        .Title("Users")
        .Meta("description", "Every user of the bench app")
        .Property("og:title", "Users")
        .Property("og:type", "website")
        .Link("canonical", "https://example.com/users"));

    private static InertiaProps LiveProps(bool live) => new()
    {
        ["a"] = live ? Prop(() => Ids).Live(Changed) : Prop(() => Ids),
        ["b"] = live ? Prop(() => Ids).Live(Changed) : Prop(() => Ids),
        ["c"] = live ? Prop(() => Ids).Live(Changed) : Prop(() => Ids),
        ["d"] = live ? Prop(() => Ids).Live(Changed) : Prop(() => Ids),
        ["e"] = live ? Prop(() => Ids).Live(Changed) : Prop(() => Ids),
    };

    private static string? _only;

    private static async Task MeasureAsync(int repetition, string name, int iterations, DefaultHttpContext? context, Func<HttpContext, Task> run)
    {
        if (_only is not null && !name.Contains(_only, StringComparison.Ordinal)) return;
        for (var i = 0; i < iterations / 10; i++) await Run(context, run);
        var before = GC.GetTotalAllocatedBytes(true);
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) await Run(context, run);
        var elapsed = Stopwatch.GetElapsedTime(started);
        Console.WriteLine($"run={repetition} {name}: {elapsed.TotalMicroseconds / iterations:F2} us/request; {(GC.GetTotalAllocatedBytes(true) - before) / iterations} B/request");
    }

    // A reused context, like Kestrel's pooled ones: the per-request Inertia feature and the response headers are reset.
    private static Task Run(DefaultHttpContext? context, Func<HttpContext, Task> run)
    {
        if (context is null)
        {
            return run(null!);
        }

        context.Features.Set<InertiaFeature>(null);
        context.Response.Headers.Clear();
        return run(context);
    }

    private sealed class BenchAuthorizer(IServiceProvider services) : ILiveChannelAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(HubCallerContext context, LiveChannel channel, CancellationToken cancellationToken) =>
            ValueTask.FromResult(services is not null);
    }
}
