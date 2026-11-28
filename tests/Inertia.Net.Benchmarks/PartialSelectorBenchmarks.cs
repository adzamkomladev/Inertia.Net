using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Benchmarks;

/// <summary>
/// Partial reloads with wider <c>X-Inertia-Partial-Data</c> lists: 96 scalar props, one selected, the rest of the list names absent
/// props. GrowingOutput selects existing optional props to measure the additional loader and output work. <c>Nested</c> selects one leaf under a nested prop.
/// Flat and Nested keep their respective response bytes constant across selector counts.
/// </summary>
[MemoryDiagnoser]
public class PartialSelectorBenchmarks
{
    [Params(1, 3, 8, 32)] public int Selectors { get; set; }

    private DefaultHttpContext _flat = null!;
    private DefaultHttpContext _nested = null!;
    private DefaultHttpContext _growingOutput = null!;
    private InertiaProps _props = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var services = BenchApp.CreateServices();
        var version = BenchApp.Version(services);
        var flat = string.Join(",", ["p0", .. Enumerable.Range(1, Selectors - 1).Select(i => "missing" + i)]);
        var nested = string.Join(",", ["group.b", .. Enumerable.Range(1, Selectors - 1).Select(i => "missing" + i)]);
        _flat = Context(flat);
        _nested = Context(nested);
        _growingOutput = Context(string.Join(",", ["p0", .. Enumerable.Range(1, Selectors - 1).Select(i => "optional" + i)]));
        _props = [];
        for (var i = 0; i < 96; i++)
        {
            _props["p" + i] = i;
        }

        var loaderCalls = 0;
        for (var i = 1; i < 32; i++)
        {
            _props["optional" + i] = Optional(() =>
            {
                loaderCalls++;
                return 0;
            });
        }

        _props["group"] = new InertiaProps { ["a"] = 1, ["b"] = 2, ["c"] = new InertiaProps { ["d"] = 3 } };

        await CheckOutputAsync(_flat, Context("p0"));
        await CheckOutputAsync(_nested, Context("group.b"));
        if (loaderCalls != 0) throw new InvalidOperationException("The constant-output fixtures ran optional loaders.");
        await GrowingOutput();
        if (loaderCalls != Selectors - 1) throw new InvalidOperationException("Unexpected loader count in the growing-output fixture.");

        async Task CheckOutputAsync(DefaultHttpContext actual, DefaultHttpContext expected)
        {
            using var actualBody = new MemoryStream();
            using var expectedBody = new MemoryStream();
            actual.Response.Body = actualBody;
            expected.Response.Body = expectedBody;
            await Render(BenchApp.Component, _props).ExecuteAsync(actual);
            await Render(BenchApp.Component, _props).ExecuteAsync(expected);
            actual.Response.Body = Stream.Null;
            if (!actualBody.ToArray().SequenceEqual(expectedBody.ToArray()))
                throw new InvalidOperationException("The constant-output fixture changed the response.");
        }

        DefaultHttpContext Context(string only) => BenchApp.CreateContext(services, "/users",
            (InertiaHeaders.Inertia, "true"), (InertiaHeaders.Version, version),
            (InertiaHeaders.PartialComponent, BenchApp.Component), (InertiaHeaders.PartialData, only));
    }

    [Benchmark] public Task Flat() => Render(BenchApp.Component, _props).ExecuteAsync(_flat);

    [Benchmark] public Task Nested() => Render(BenchApp.Component, _props).ExecuteAsync(_nested);

    [Benchmark] public Task GrowingOutput() => Render(BenchApp.Component, _props).ExecuteAsync(_growingOutput);

    [Benchmark] public InertiaRequest ParseOnly() => new(_flat.Request.Headers);
}
