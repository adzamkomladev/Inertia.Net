using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;

namespace Inertia.Net.Benchmarks;

/// <summary>
/// <see cref="InertiaProp.Parallel"/> against sequential loading (plan gate W2): independent 20 ms loaders, where parallel loading
/// should approach one loader's time, and tiny loaders, which measure the planner's own overhead.
/// </summary>
[MemoryDiagnoser]
public class ParallelPropsBenchmarks
{
    [Params(1, 3, 6)] public int SlowLoaders { get; set; }

    private DefaultHttpContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = BenchApp.CreateServices();
        _context = BenchApp.CreateContext(services, "/users", (InertiaHeaders.Inertia, "true"), (InertiaHeaders.Version, BenchApp.Version(services)));
    }

    [Benchmark(Baseline = true, Description = "20 ms loaders, sequential")]
    public Task SlowSequential() => Execute(_context, Inertia.Render(BenchApp.Component, SlowProps(SlowLoaders, parallel: false)));

    [Benchmark(Description = "20 ms loaders, parallel")]
    public Task SlowParallel() => Execute(_context, Inertia.Render(BenchApp.Component, SlowProps(SlowLoaders, parallel: true)));

    [Benchmark(Description = "20 tiny loaders, sequential")]
    public Task TinySequential() => Execute(_context, Inertia.Render(BenchApp.Component, TinyProps(parallel: false)));

    [Benchmark(Description = "20 tiny loaders, parallel")]
    public Task TinyParallel() => Execute(_context, Inertia.Render(BenchApp.Component, TinyProps(parallel: true)));

    private static InertiaProps SlowProps(int count, bool parallel)
    {
        var props = new InertiaProps { ["sync"] = Inertia.Prop(() => 1) };
        for (var i = 0; i < count; i++)
        {
            var prop = Inertia.Prop(async ct =>
            {
                await Task.Delay(20, ct);
                return 1;
            });
            props[$"slow{i}"] = parallel ? prop.Parallel() : prop;
        }

        return props;
    }

    private static InertiaProps TinyProps(bool parallel)
    {
        var props = new InertiaProps();
        for (var i = 0; i < 20; i++)
        {
            var value = i;
            var prop = Inertia.Prop(_ => ValueTask.FromResult(value));
            props[$"tiny{i}"] = parallel ? prop.Parallel() : prop;
        }

        return props;
    }

    private static Task Execute(DefaultHttpContext context, InertiaResult result)
    {
        context.Response.Headers.Clear();
        return result.ExecuteAsync(context);
    }
}
