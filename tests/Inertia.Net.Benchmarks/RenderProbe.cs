using System.Diagnostics;

namespace Inertia.Net.Benchmarks;

internal static class RenderProbe
{
    internal static async Task RunAsync()
    {
        var benchmark = new RenderBenchmarks();
        benchmark.Setup();
        for (var repetition = 1; repetition <= 3; repetition++)
        {
            foreach (var (name, render) in new (string, Func<Task>)[]
                { ("json", benchmark.Json20Props), ("poco", benchmark.JsonTypedPoco), ("partial", benchmark.PartialReload), ("html", benchmark.Html20Props) })
            {
                for (var i = 0; i < 2000; i++) await render();
                var before = GC.GetTotalAllocatedBytes(true);
                var started = Stopwatch.GetTimestamp();
                for (var i = 0; i < 100000; i++) await render();
                var elapsed = Stopwatch.GetElapsedTime(started);
                Console.WriteLine($"run={repetition} {name}: {elapsed.TotalMicroseconds / 100000:F3} us/request; {(GC.GetTotalAllocatedBytes(true) - before) / 100000} B/request");
            }
        }
    }
}
