using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Benchmarks;

/// <summary>
/// Partial reloads with wider <c>X-Inertia-Partial-Data</c> lists: 96 scalar props, one selected, the rest of the list names absent
/// optional props (as a client asking for several deferred groups would). <c>Nested</c> selects one leaf under a nested prop.
/// The response is the same few bytes in every row, so the cost difference is header parsing and path matching.
/// </summary>
[MemoryDiagnoser]
public class PartialSelectorBenchmarks
{
    [Params(1, 3, 8, 32)] public int Selectors { get; set; }

    private DefaultHttpContext _flat = null!;
    private DefaultHttpContext _nested = null!;
    private InertiaProps _props = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = BenchApp.CreateServices();
        var version = BenchApp.Version(services);
        var flat = string.Join(",", ["p0", .. Enumerable.Range(1, Selectors - 1).Select(i => "missing" + i)]);
        var nested = string.Join(",", ["group.b", .. Enumerable.Range(1, Selectors - 1).Select(i => "missing" + i)]);
        _flat = Context(flat);
        _nested = Context(nested);
        _props = [];
        for (var i = 0; i < 96; i++)
        {
            _props["p" + i] = i;
        }

        for (var i = 1; i < 32; i++)
        {
            _props["missing" + i] = Optional(() => 0);
        }

        _props["group"] = new InertiaProps { ["a"] = 1, ["b"] = 2, ["c"] = new InertiaProps { ["d"] = 3 } };

        DefaultHttpContext Context(string only) => BenchApp.CreateContext(services, "/users",
            (InertiaHeaders.Inertia, "true"), (InertiaHeaders.Version, version),
            (InertiaHeaders.PartialComponent, BenchApp.Component), (InertiaHeaders.PartialData, only));
    }

    [Benchmark] public Task Flat() => Render(BenchApp.Component, _props).ExecuteAsync(_flat);

    [Benchmark] public Task Nested() => Render(BenchApp.Component, _props).ExecuteAsync(_nested);

    [Benchmark] public InertiaRequest ParseOnly() => new(_flat.Request.Headers);
}
