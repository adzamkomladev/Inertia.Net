using System.Diagnostics;

namespace Inertia.Net.Benchmarks;

/// <summary>Warm allocation and time of cache writes and typed dictionary props, using the benchmark fixtures.</summary>
internal static class AllocationProbe
{
    internal static async Task RunAsync()
    {
        foreach (var size in new[] { 1024, 1048576 })
        {
            var cache = new CacheWriteBenchmarks { PayloadBytes = size };
            cache.Setup();
            try
            {
                for (var round = 1; round <= 3; round++)
                {
                    await MeasureAsync($"cache {size} bytes round {round}", () =>
                    {
                        if (cache.CreateEntry().Length != size + 20) throw new InvalidOperationException("Incorrect cached string length.");
                        return Task.CompletedTask;
                    }, size < 1048576 ? 10000 : 100);
                }
            }
            finally
            {
                cache.Cleanup();
            }
        }

        var dictionary = new DictionaryPropsBenchmarks { Count = 1000 };
        dictionary.Setup();
        try
        {
            for (var round = 1; round <= 3; round++)
            {
                await MeasureAsync($"integer keys round {round}", dictionary.IntegerKeys, 1000);
                await MeasureAsync($"string keys round {round}", dictionary.StringKeys, 1000);
            }
        }
        finally
        {
            dictionary.Cleanup();
        }
    }

    private static async Task MeasureAsync(string name, Func<Task> run, int count)
    {
        for (var i = 0; i < count / 10; i++) await run();
        var before = GC.GetTotalAllocatedBytes(true);
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < count; i++) await run();
        var elapsed = Stopwatch.GetElapsedTime(started);
        Console.WriteLine($"{name}: {(GC.GetTotalAllocatedBytes(true) - before) / count} B/op; {elapsed.TotalMicroseconds / count:F2} us/op");
    }
}
