using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Benchmarks;

/// <summary>Targeted allocation fixtures for scroll paths, member converters, contended fills and planned cache hits.</summary>
public sealed class SmallAllocationBenchmarks
{
    private readonly RenderBenchmarks _control = new();
    private readonly InertiaCacheKey _fillKey = new(InertiaCacheEntryKind.Prop, "held-fill");
    private ServiceProvider _services = null!;
    private InertiaCaching _caching = null!;
    private TaskCompletionSource<byte[]?> _fill = null!;
    private DefaultHttpContext _append = null!, _prepend = null!, _convertedContext = null!, _cachedContext = null!, _asyncContext = null!;
    private InertiaProps _scroll = null!, _cached = null!, _asyncProps = null!;
    private ConvertedAllocationProps _converted = null!;

    /// <summary>Creates and checks the workloads before measurement.</summary>
    public async Task Setup()
    {
        _control.Setup();
        _services = (ServiceProvider)BenchApp.CreateServices(configureServices: services => services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, SmallAllocationJsonContext.Default)));
        _caching = _services.GetRequiredService<InertiaCaching>();
        if (!_caching.TryBeginFill(_fillKey, out _fill)) throw new InvalidOperationException("The contention fixture needs its own fill.");
        _append = Context();
        _prepend = Context();
        _prepend.Request.Headers[InertiaHeaders.InfiniteScrollMergeIntent] = "prepend";
        _convertedContext = Context();
        _cachedContext = Context();
        _asyncContext = Context();
        _scroll = new InertiaProps();
        _cached = new InertiaProps();
        _converted = new ConvertedAllocationProps();
        _asyncProps = new InertiaProps { ["value"] = Prop(async _ => { await Task.Yield(); return 42; }) };
        for (var index = 0; index < 8; index++)
        {
            _scroll[$"feed{index}"] = Scroll(new AllocationFeed([1, 2]), metadata: ScrollMetadata.FromPage(1, hasMore: true)).Append("older", "id");
        }

        for (var index = 0; index < 32; index++)
        {
            var value = index;
            _cached[$"cached{index}"] = Prop(_ => ValueTask.FromResult(value)).Parallel().Cache($"small-{index}", TimeSpan.FromHours(1));
        }

        await PlannedCacheHits32();
        await CheckOutput(_append, "Small/Scroll", _scroll, root =>
        {
            var paths = root.GetProperty("mergeProps");
            if (paths.GetArrayLength() != 16 || paths[0].GetString() != "feed0.older" || paths[1].GetString() != "feed0.data")
                throw new InvalidOperationException("Scroll append paths changed.");
        });
        await CheckOutput(_prepend, "Small/Scroll", _scroll, root =>
        {
            if (root.GetProperty("prependProps")[0].GetString() != "feed0.data" || root.GetProperty("mergeProps")[0].GetString() != "feed0.older")
                throw new InvalidOperationException("Scroll prepend paths changed.");
        });
        await CheckOutput(_convertedContext, "Small/Converters", _converted, root =>
        {
            if (root.GetProperty("props").GetProperty("first").GetString() != "value")
                throw new InvalidOperationException("The member converter did not run.");
        });
        ContendedFillAttempt();
    }

    private DefaultHttpContext Context() => BenchApp.CreateContext(_services, "/small", (InertiaHeaders.Inertia, "true"));

    /// <summary>The ordinary mixed JSON render control.</summary>
    public Task Json20() => _control.Json20Props();

    /// <summary>A sequential loader that suspends, checking the allocation cost of the writer's asynchronous state.</summary>
    public Task YieldingLoader() => Execute(_asyncContext, "Small/Async", _asyncProps);

    /// <summary>Eight scroll props with existing append paths and an appended scroll wrapper.</summary>
    public Task ScrollAppend8() => Execute(_append, "Small/Scroll", _scroll);

    /// <summary>Eight scroll props with existing append paths and a prepended scroll wrapper.</summary>
    public Task ScrollPrepend8() => Execute(_prepend, "Small/Scroll", _scroll);

    /// <summary>Eight custom-converter properties on a traversed typed object.</summary>
    public Task ConverterProperties8() => Execute(_convertedContext, "Small/Converters", _converted);

    /// <summary>Thirty-two synchronous cache hits whose props are marked parallel.</summary>
    public Task PlannedCacheHits32() => Execute(_cachedContext, "Small/Cached", _cached);

    /// <summary>A waiter attempts to claim a key whose fill remains owned by another request.</summary>
    public void ContendedFillAttempt()
    {
        if (_caching.TryBeginFill(_fillKey, out var completion) || !ReferenceEquals(completion, _fill))
            throw new InvalidOperationException("A waiter acquired an already-owned fill.");
    }

    private static Task Execute(DefaultHttpContext context, string component, object props)
    {
        context.Response.Headers.Clear();
        return Render(component, props).ExecuteAsync(context);
    }

    private static async Task CheckOutput(DefaultHttpContext context, string component, object props, Action<JsonElement> check)
    {
        using var body = new MemoryStream();
        context.Response.Body = body;
        await Execute(context, component, props);
        using var document = JsonDocument.Parse(body.ToArray());
        check(document.RootElement);
        context.Response.Body = Stream.Null;
    }

    /// <summary>Releases the held fill and fixture services.</summary>
    public void Cleanup()
    {
        _caching.EndFill(_fillKey, _fill);
        _services.Dispose();
    }
}

/// <summary>The payload of the scroll fixture.</summary>
public sealed record AllocationFeed(int[] Data);

/// <summary>A typed prop object whose ordinary string members each have a converter.</summary>
public sealed class ConvertedAllocationProps
{
    [JsonConverter(typeof(AllocationStringConverter))] public string First => "value";
    [JsonConverter(typeof(AllocationStringConverter))] public string Second => "value";
    [JsonConverter(typeof(AllocationStringConverter))] public string Third => "value";
    [JsonConverter(typeof(AllocationStringConverter))] public string Fourth => "value";
    [JsonConverter(typeof(AllocationStringConverter))] public string Fifth => "value";
    [JsonConverter(typeof(AllocationStringConverter))] public string Sixth => "value";
    [JsonConverter(typeof(AllocationStringConverter))] public string Seventh => "value";
    [JsonConverter(typeof(AllocationStringConverter))] public string Eighth => "value";
    public InertiaProp Extra { get; } = Always(1);
}

/// <summary>A member converter used by the allocation fixture.</summary>
public sealed class AllocationStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

[JsonSerializable(typeof(AllocationFeed))]
[JsonSerializable(typeof(ConvertedAllocationProps))]
internal sealed partial class SmallAllocationJsonContext : JsonSerializerContext;

/// <summary>Loads identical fixture binaries against separate adapter builds for alternating paired rounds.</summary>
internal static class SmallAllocationProbe
{
    private static readonly string[] Methods = ["Json20", "YieldingLoader", "ScrollAppend8", "ScrollPrepend8", "ConverterProperties8", "ContendedFillAttempt", "PlannedCacheHits32"];

    internal static async Task RunAsync(string directory, string output)
    {
        var variants = new List<Variant>();
        var rows = new List<Sample>();
        try
        {
            foreach (var name in new[] { "baseline", "current" })
            {
                var variant = new Variant(name, Path.Combine(directory, name, "Inertia.Net.Benchmarks.dll"));
                variants.Add(variant);
                await variant.Setup();
            }

            foreach (var method in Methods)
            {
                foreach (var variant in variants)
                {
                    var started = Stopwatch.GetTimestamp();
                    do { variant.Actions[method](); } while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 300);
                }

                var count = variants.Min(variant => Calibrate(variant.Actions[method]));
                for (var round = 0; round < 32; round++)
                {
                    foreach (var index in round % 2 == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
                    {
                        var variant = variants[index];
                        var action = variant.Actions[method];
                        var allocated = GC.GetTotalAllocatedBytes(true);
                        var started = Stopwatch.GetTimestamp();
                        for (var iteration = 0; iteration < count; iteration++) action();
                        var elapsed = Stopwatch.GetElapsedTime(started);
                        rows.Add(new Sample(method, variant.Name, round, count, elapsed.TotalNanoseconds / count,
                            (GC.GetTotalAllocatedBytes(true) - allocated) / (double)count));
                    }
                }

                Console.WriteLine(method + ": " + string.Join("; ", variants.Select(variant =>
                {
                    var samples = rows.Where(row => row.Method == method && row.Variant == variant.Name).ToArray();
                    return $"{variant.Name} {Median(samples.Select(row => row.AllocatedBytes)):F0} B/op {Median(samples.Select(row => row.Nanoseconds)) / 1000:F3} us/op";
                })));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
            {
                runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), rounds = 32,
                processorCount = Environment.ProcessorCount,
                hashes = variants.ToDictionary(variant => variant.Name, variant => variant.Hash), rows,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            foreach (var variant in variants) variant.Dispose();
        }
    }

    private static int Calibrate(Action action)
    {
        var count = 1;
        while (true)
        {
            var started = Stopwatch.GetTimestamp();
            for (var index = 0; index < count; index++) action();
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 20 || count >= 1048576) return count;
            count *= 2;
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
    }

    private sealed record Sample(string Method, string Variant, int Round, int Count, double Nanoseconds, double AllocatedBytes);

    private sealed class Variant : IDisposable
    {
        private readonly LibraryContext _context;
        private readonly object _fixture;
        private readonly MethodInfo _cleanup;
        private readonly MethodInfo _setup;

        public Variant(string name, string path)
        {
            Name = name;
            Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path)!, "Inertia.Net.dll"))));
            _context = new LibraryContext(name, path);
            var assembly = _context.LoadFromAssemblyPath(Path.GetFullPath(path));
            var type = assembly.GetType(typeof(SmallAllocationBenchmarks).FullName!, throwOnError: true)!;
            _fixture = Activator.CreateInstance(type)!;
            _setup = type.GetMethod("Setup")!;
            _cleanup = type.GetMethod("Cleanup")!;
            Actions = Methods.ToDictionary(method => method, method =>
            {
                var info = type.GetMethod(method)!;
                if (info.ReturnType == typeof(Task))
                {
                    var run = info.CreateDelegate<Func<Task>>(_fixture);
                    return (Action)(() => run().GetAwaiter().GetResult());
                }

                return info.CreateDelegate<Action>(_fixture);
            });
        }

        public string Name { get; }
        public string Hash { get; }
        public Dictionary<string, Action> Actions { get; }
        public Task Setup() => (Task)_setup.Invoke(_fixture, null)!;

        public void Dispose()
        {
            _cleanup.Invoke(_fixture, null);
            _context.Unload();
        }
    }

    private sealed class LibraryContext(string name, string path) : AssemblyLoadContext(name, isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(path);

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name!.StartsWith("System", StringComparison.Ordinal) || name.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || name.Name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)) return null;
            return _resolver.ResolveAssemblyToPath(name) is { } assembly ? LoadFromAssemblyPath(assembly) : null;
        }
    }
}
