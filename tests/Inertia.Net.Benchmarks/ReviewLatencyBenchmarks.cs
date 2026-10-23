using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Benchmarks;

/// <summary>Comparable latency fixtures for the performance review, including planner overhead after Parallel has been used.</summary>
[MemoryDiagnoser]
public class ReviewLatencyBenchmarks
{
    private readonly RenderBenchmarks _render = new();
    private readonly CacheWriteBenchmarks _smallCache = new() { PayloadBytes = 1024 };
    private readonly CacheWriteBenchmarks _largeCache = new() { PayloadBytes = 1048576 };
    private readonly DictionaryPropsBenchmarks _dictionary = new() { Count = 1000 };
    private readonly SemaphoreSlim _backend = new(4);
    private ServiceProvider _services = null!;
    private DefaultHttpContext _context = null!;
    private DefaultHttpContext[] _burstContexts = null!;
    private InertiaProps _nested20 = null!;
    private InertiaProps _nested2000 = null!;
    private InertiaProps _cached1 = null!;
    private InertiaProps _cached32 = null!;
    private InertiaProps _burst = null!;
    private int _backendCalls;

    public int LastBurstCalls { get; private set; }

    [GlobalSetup]
    public async Task Setup()
    {
        InertiaProp.ParallelUsed = true;
        _render.Setup();
        _smallCache.Setup();
        _largeCache.Setup();
        _dictionary.Setup();
        _services = (ServiceProvider)BenchApp.CreateServices();
        _context = BenchApp.CreateContext(_services, "/users", (InertiaHeaders.Inertia, "true"));
        _nested20 = Nested(20);
        _nested2000 = Nested(2000);
        _cached1 = Cached(1);
        _cached32 = Cached(32);
        await Execute(_cached1);
        await Execute(_cached32);
        _burstContexts = Enumerable.Range(0, 32)
            .Select(_ => BenchApp.CreateContext(_services, "/users", (InertiaHeaders.Inertia, "true"))).ToArray();
        _burst = new InertiaProps { ["value"] = Prop(LoadAsync).Cache("burst", TimeSpan.FromHours(1)) };
        await ColdBurst32();
        if (LastBurstCalls is < 1 or > 32) throw new InvalidOperationException("Incorrect cold-burst fixture.");
        Console.WriteLine($"Cold burst fixture: {LastBurstCalls} backend calls for 32 requests; 4 slots, 10 ms simulated I/O.");
    }

    [Benchmark] public Task Json20() => _render.Json20Props();
    [Benchmark] public Task Partial2Of20() => _render.PartialReload();
    [Benchmark] public Task Html20() => _render.Html20Props();
    [Benchmark] public void ReferenceSerialize() => _render.PlainSerialize();
    [Benchmark] public byte[] CacheWrite1KiB() => _smallCache.CreateEntry();
    [Benchmark] public byte[] CacheWrite1MiB() => _largeCache.CreateEntry();
    [Benchmark] public Task IntegerKeys1000() => _dictionary.IntegerKeys();
    [Benchmark] public Task Nested20() => Execute(_nested20);
    [Benchmark] public Task Nested2000() => Execute(_nested2000);
    [Benchmark] public Task ParallelCacheHit1() => Execute(_cached1);
    [Benchmark] public Task ParallelCacheHit32() => Execute(_cached32);

    [Benchmark]
    public async Task ColdBurst32()
    {
        await _services.GetRequiredService<IInertiaCache>().RemovePropAsync("burst");
        var before = Volatile.Read(ref _backendCalls);
        await Task.WhenAll(_burstContexts.Select(context => Render(BenchApp.Component, _burst).ExecuteAsync(context)));
        LastBurstCalls = Volatile.Read(ref _backendCalls) - before;
    }

    private Task Execute(InertiaProps props)
    {
        _context.Response.Headers.Clear();
        return Render(BenchApp.Component, props).ExecuteAsync(_context);
    }

    private async Task<int> LoadAsync(CancellationToken cancellationToken)
    {
        await _backend.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Increment(ref _backendCalls);
            await Task.Delay(10, cancellationToken);
            return 42;
        }
        finally
        {
            _backend.Release();
        }
    }

    private static InertiaProps Nested(int count)
    {
        var props = new InertiaProps();
        for (var group = 0; group < count / 10; group++)
        {
            var fields = new InertiaProps();
            for (var field = 0; field < 10; field++) fields[$"field{field}"] = field;
            props[$"group{group}"] = fields;
        }

        return props;
    }

    private static InertiaProps Cached(int count)
    {
        var props = new InertiaProps();
        for (var i = 0; i < count; i++)
        {
            var value = i;
            props[$"cached{i}"] = Prop(_ => ValueTask.FromResult(value)).Parallel().Cache($"cached{i}", TimeSpan.FromHours(1));
        }

        return props;
    }

    [Benchmark]
    public void ConstructProps() => GC.KeepAlive(BenchApp.Props20());

    [GlobalCleanup]
    public void Cleanup()
    {
        _services.Dispose();
        _smallCache.Cleanup();
        _largeCache.Cleanup();
        _dictionary.Cleanup();
        _backend.Dispose();
    }
}
