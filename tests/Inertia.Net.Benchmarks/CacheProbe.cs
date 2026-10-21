using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Benchmarks;

/// <summary>
/// <c>--cache-probe</c>: a page whose loader builds 500 rows, with and without <see cref="InertiaProp.Cache"/>, and a server-rendered
/// page through an in-memory SSR server (about 64 KiB of HTML), with and without the SSR cache. The loader and the SSR server cost
/// nothing beyond building their output here, so the gains in a real app (database queries, a Node render) are larger.
/// </summary>
internal static class CacheProbe
{
    private static readonly User[] Rows = [.. Enumerable.Range(1, 500).Select(i => new User(i, $"User {i}", $"user{i}@example.com", i % 3 == 0))];

    internal static async Task RunAsync()
    {
        var services = BenchApp.CreateServices();
        var version = BenchApp.Version(services);
        var json = BenchApp.CreateContext(services, "/users", (InertiaHeaders.Inertia, "true"), (InertiaHeaders.Version, version));

        var ssrServices = SsrServices(cache: false);
        var cachedSsrServices = SsrServices(cache: true);
        var html = BenchApp.CreateContext(ssrServices);
        var cachedHtml = BenchApp.CreateContext(cachedSsrServices);

        for (var repetition = 1; repetition <= 3; repetition++)
        {
            await MeasureAsync(repetition, "prop loader", 20000, () => Render(BenchApp.Component, Page(cached: false)).ExecuteAsync(json));
            await MeasureAsync(repetition, "prop cached", 20000, () => Render(BenchApp.Component, Page(cached: true)).ExecuteAsync(json));
            await MeasureAsync(repetition, "ssr render", 5000, () => Render(BenchApp.Component, SsrPage()).ExecuteAsync(html));
            await MeasureAsync(repetition, "ssr cached", 5000, () => Render(BenchApp.Component, SsrPage()).ExecuteAsync(cachedHtml));
        }
    }

    // The loader copies the rows as an app would map query results to DTOs.
    private static InertiaProps Page(bool cached)
    {
        var rows = Prop(() => Rows.Select(r => r with { }).ToArray());
        return new InertiaProps { ["title"] = "Users", ["users"] = cached ? rows.Cache("bench-users", TimeSpan.FromMinutes(5)) : rows };
    }

    private static InertiaProps SsrPage() => new() { ["title"] = "Home", ["count"] = 3 };

    private static IServiceProvider SsrServices(bool cache)
    {
        var body = new string('x', 64 * 1024);
        var response = Encoding.UTF8.GetBytes($$"""{"head":["<title>Bench</title>"],"body":"<div id=\"app\">{{body}}</div>"}""");
        return BenchApp.CreateServices(o =>
        {
            o.Ssr.Mode = SsrMode.Preferred;
            o.Ssr.Concurrency.MaxActive = 0;
            if (cache)
            {
                o.Ssr.Cache.Duration = TimeSpan.FromMinutes(5);
            }
        }, s => s.AddHttpClient(InertiaSsr.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new FixedHandler(response)));
    }

    private static async Task MeasureAsync(int repetition, string name, int iterations, Func<Task> render)
    {
        for (var i = 0; i < iterations / 10; i++) await render();
        var before = GC.GetTotalAllocatedBytes(true);
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) await render();
        var elapsed = Stopwatch.GetElapsedTime(started);
        Console.WriteLine($"run={repetition} {name}: {elapsed.TotalMicroseconds / iterations:F2} us/request; {(GC.GetTotalAllocatedBytes(true) - before) / iterations} B/request");
    }

    private sealed class FixedHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) { Headers = { ContentType = new("application/json") } } });
    }
}
