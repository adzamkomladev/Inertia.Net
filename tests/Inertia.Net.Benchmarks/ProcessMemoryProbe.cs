using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace Inertia.Net.Benchmarks;

/// <summary>Process-isolated RSS, GC and retained prop-list measurements; writes raw observations as JSON.</summary>
internal static class ProcessMemoryProbe
{
    internal static async Task RunAsync(string mode, string output)
    {
        if (mode is not ("render" or "handler" or "retention")) throw new ArgumentException("Unknown workload.", nameof(mode));
        using var services = (ServiceProvider)BenchApp.CreateServices();
        var context = BenchApp.CreateContext(services, "/users", (InertiaHeaders.Inertia, "true"));
        var props = BenchApp.Props20();
        async Task RenderAsync()
        {
            context.Response.Headers.Clear();
            await Render(BenchApp.Component, mode == "handler" ? BenchApp.Props20() : props).ExecuteAsync(context);
        }

        for (var i = 0; i < 10000; i++) await RenderAsync();
        using var process = Process.GetCurrentProcess();
        FullCollection();
        var before = Snapshot(process);
        var peakRss = before.RssBytes;
        const int operations = 300000;
        var start = Stopwatch.GetTimestamp();
        if (mode != "retention")
        {
            for (var i = 0; i < operations; i++)
            {
                await RenderAsync();
                if (i % 4096 == 0) { process.Refresh(); peakRss = Math.Max(peakRss, process.WorkingSet64); }
            }
        }
        var elapsed = Stopwatch.GetElapsedTime(start);
        var after = Snapshot(process);
        peakRss = Math.Max(peakRss, after.RssBytes);
        var poolAfterWork = Pool();
        FullCollection();
        var afterFullGc = Snapshot(process);
        var retention = new List<object>();
        if (mode == "retention")
        {
            foreach (var count in new[] { 20, 1000, 2000 })
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var entered = 0;
                var values = new InertiaProps();
                values["wait"] = Prop(async ct => { Interlocked.Increment(ref entered); await gate.Task.WaitAsync(ct); return 1; });
                for (var i = 0; i < count; i++) values[$"field{i}"] = i;
                var contexts = Enumerable.Range(0, 32).Select(_ => BenchApp.CreateContext(services, "/users", (InertiaHeaders.Inertia, "true"))).ToArray();
                var pending = contexts.Select(c => Render(BenchApp.Component, values).ExecuteAsync(c)).ToArray();
                if (entered != 32) throw new InvalidOperationException("Concurrent retention fixture did not hold all renders.");
                gate.SetResult();
                await Task.WhenAll(pending);
                FullCollection();
                retention.Add(new { Fields = count, ConcurrentRenders = 32, Pool = Pool(), Memory = Snapshot(process) });
            }
        }
        var assembly = typeof(Inertia).Assembly;
        var report = new
        {
            Mode = mode, Operations = mode == "retention" ? 0 : operations,
            Runtime = Environment.Version.ToString(), Os = Environment.OSVersion.ToString(),
            Processors = Environment.ProcessorCount, ServerGc = System.Runtime.GCSettings.IsServerGC,
            LibraryHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
            Before = before, After = after, AfterFullGc = afterFullGc, PeakSampledRssBytes = peakRss,
            AllocatedBytes = after.AllocatedBytes - before.AllocatedBytes,
            Gen0 = after.Gen0 - before.Gen0, Gen1 = after.Gen1 - before.Gen1, Gen2 = after.Gen2 - before.Gen2,
            GcPauseMs = after.PauseMs - before.PauseMs, ElapsedMs = elapsed.TotalMilliseconds,
            PoolAfterWork = poolAfterWork, Retention = retention,
        };
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GC.KeepAlive(props);
    }

    private static void FullCollection()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private sealed record Memory(long RssBytes, long HeapBytes, long CommittedBytes, long AllocatedBytes, int Gen0, int Gen1, int Gen2, double PauseMs);

    private static Memory Snapshot(Process process)
    {
        process.Refresh();
        return new(process.WorkingSet64, GC.GetTotalMemory(false), GC.GetGCMemoryInfo().TotalCommittedBytes,
            GC.GetTotalAllocatedBytes(true), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration().TotalMilliseconds);
    }

    private static object Pool()
    {
        var pool = typeof(Inertia).Assembly.GetType("Inertia.Net.InertiaPageWriter")!
            .GetField("EntryLists", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        if (pool is null) return new { Lists = 0, Capacities = Array.Empty<int>(), RetainedListAndArrayBytes = 0L, InfrastructureBytes = 0L, TotalRetainedBytes = 0L, ReferencesCleared = true };
        var type = pool.GetType();
        var lists = new List<IList>();
        if (type.GetField("_fastItem", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pool) is IList first) lists.Add(first);
        var queue = type.GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pool)!;
        var segment = queue.GetType().GetField("_head", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue);
        var segments = 0;
        // Reading slots avoids ConcurrentQueue enumeration freezing a segment and changing subsequent retention.
        while (segment is not null)
        {
            segments++;
            var segmentType = segment.GetType();
            var slots = (Array)segmentType.GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(segment)!;
            foreach (var slot in slots)
            {
                var item = slot!.GetType().GetField("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(slot);
                if (item is IList list) lists.Add(list);
            }
            segment = segmentType.GetField("_nextSegment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(segment);
        }
        if (segments != 1) throw new InvalidOperationException("Pool infrastructure measurement requires one queue segment.");
        var capacities = lists.Select(list => (int)list.GetType().GetProperty("Capacity")!.GetValue(list)!).ToArray();
        var cleared = true;
        long retained = 0;
        foreach (var list in lists)
        {
            var array = (PropEntry[])list.GetType().GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(list)!;
            cleared &= list.Count == 0 && array.All(entry => entry.Equals(default(PropEntry)));
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var equivalent = new List<PropEntry>(array.Length);
            retained += GC.GetAllocatedBytesForCurrentThread() - allocated;
            GC.KeepAlive(equivalent);
        }
        if (!cleared || capacities.Any(capacity => capacity > 1024) || lists.Count > Environment.ProcessorCount * 2)
            throw new InvalidOperationException("Pool retention invariant failed.");
        MeasureInfrastructure(); // Warm the sizing helper before counting its allocations.
        var infrastructure = MeasureInfrastructure();
        if (infrastructure != MeasureInfrastructure()) throw new InvalidOperationException("Unstable pool infrastructure sizing.");
        return new { Lists = lists.Count, Capacities = capacities, RetainedListAndArrayBytes = retained,
            InfrastructureBytes = infrastructure, TotalRetainedBytes = retained + infrastructure, ReferencesCleared = cleared };
    }

    private sealed class SizingPolicy : PooledObjectPolicy<List<PropEntry>>
    {
        public override List<PropEntry> Create() => [];
        public override bool Return(List<PropEntry> value) => true;
    }

    private static long MeasureInfrastructure()
    {
        // Same platform pool and fieldless policy layout as EntryListPolicy; includes its queue, segment and delegates.
        var start = GC.GetAllocatedBytesForCurrentThread();
        var equivalent = new DefaultObjectPool<List<PropEntry>>(new SizingPolicy());
        var bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        GC.KeepAlive(equivalent);
        return bytes;
    }
}
