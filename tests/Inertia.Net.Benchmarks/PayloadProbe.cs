using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Inertia.Net.Benchmarks;

/// <summary>Warm render time and allocation of one large page (a 1 MiB string prop), JSON and HTML, after the buffer size settles.</summary>
internal static class PayloadProbe
{
    internal static async Task RunAsync()
    {
        var services = BenchApp.CreateServices();
        var props = new InertiaProps { ["text"] = new string('x', 1024 * 1024) };
        foreach (var inertia in new[] { true, false })
        {
            for (var repetition = 1; repetition <= 3; repetition++)
            {
                async Task RenderAsync()
                {
                    var context = inertia
                        ? BenchApp.CreateContext(services, "/users", (InertiaHeaders.Inertia, "true"))
                        : BenchApp.CreateContext(services);
                    context.Response.Body = Stream.Null;
                    await Render(BenchApp.Component, props).ExecuteAsync(context);
                }

                for (var i = 0; i < 20; i++) await RenderAsync();
                var before = GC.GetTotalAllocatedBytes(true);
                var started = Stopwatch.GetTimestamp();
                for (var i = 0; i < 200; i++) await RenderAsync();
                var elapsed = Stopwatch.GetElapsedTime(started);
                Console.WriteLine($"run={repetition} {(inertia ? "json" : "html")} 1 MiB: {elapsed.TotalMicroseconds / 200:F1} us/render, {(GC.GetTotalAllocatedBytes(true) - before) / 200} B/render");
            }
        }
    }
}
