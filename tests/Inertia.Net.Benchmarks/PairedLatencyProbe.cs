using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

namespace Inertia.Net.Benchmarks;

/// <summary>Counterbalanced timings of identical fixtures loaded with separate library versions in the same process.</summary>
internal static class PairedLatencyProbe
{
    private static readonly string[] Methods =
    [
        "Json20", "Partial2Of20", "Html20", "ReferenceSerialize", "CacheWrite1KiB", "CacheWrite1MiB", "IntegerKeys1000",
        "ConstructProps", "Nested20", "Nested2000", "ParallelCacheHit1", "ParallelCacheHit32", "TinyParallel20", "YieldParallel3", "ColdBurst32",
    ];

    internal static async Task RunAsync(string directory, string output, bool memory = false)
    {
        string[] names = memory ? ["baseline", "current"] : ["original", "current", "prune", "remember"];
        var variants = new List<Variant>();
        var rows = new List<Sample>();
        try
        {
            foreach (var name in names)
            {
                var path = Path.Combine(directory, name, "tests/Inertia.Net.Benchmarks/bin/Release/net10.0/Inertia.Net.Benchmarks.dll");
                var variant = new Variant(name, path);
                variants.Add(variant);
                await variant.SetupAsync();
            }

            Console.WriteLine("Warming every fixture/version before measuring.");
            foreach (var method in Methods)
            {
                foreach (var variant in variants)
                {
                    var run = variant.Actions[method];
                    var started = Stopwatch.GetTimestamp();
                    do { run(); } while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 300);
                }
            }

            foreach (var method in Methods)
            {
                var count = variants.Min(variant => Calibrate(variant.Actions[method]));
                for (var round = 0; round < 32; round++)
                {
                    var order = Enumerable.Range(0, names.Length).Select(index => (index + round % names.Length) % names.Length);
                    if (round % 8 >= 4) order = order.Reverse();
                    foreach (var index in order)
                    {
                        var variant = variants[index];
                        var run = variant.Actions[method];
                        var allocated = GC.GetTotalAllocatedBytes(true);
                        var started = Stopwatch.GetTimestamp();
                        for (var i = 0; i < count; i++) run();
                        var elapsed = Stopwatch.GetElapsedTime(started);
                        rows.Add(new Sample(method, variant.Name, round, count, elapsed.TotalNanoseconds / count,
                            (GC.GetTotalAllocatedBytes(true) - allocated) / (double)count));
                    }
                }

                Console.WriteLine(method + ": " + string.Join("; ", names.Select(name =>
                {
                    var times = rows.Where(row => row.Method == method && row.Variant == name).Select(row => row.Nanoseconds).Order().ToArray();
                    return $"{name} median {(times[15] + times[16]) / 2000:F3} us";
                })));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
            {
                runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString(),
                rounds = 32,
                hashes = variants.ToDictionary(variant => variant.Name, variant => variant.Hash),
                rows,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Saved paired samples to " + output);
        }
        finally
        {
            foreach (var variant in variants) variant.Dispose();
        }
    }

    private static int Calibrate(Action run)
    {
        var count = 1;
        while (true)
        {
            var started = Stopwatch.GetTimestamp();
            for (var i = 0; i < count; i++) run();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (elapsed >= 20 || count >= 1048576) return count;
            count *= 2;
        }
    }

    internal static async Task RunPlanningCostAsync(string directory, string output)
    {
        var path = Path.Combine(directory, "current/tests/Inertia.Net.Benchmarks/bin/Release/net10.0/Inertia.Net.Benchmarks.dll");
        using var variant = new Variant("current", path);
        await variant.SetupAsync();
        var rows = new List<Sample>();
        foreach (var method in new[] { "Json20", "Nested20", "Nested2000", "ReferenceSerialize" })
        {
            var run = variant.Actions[method];
            foreach (var enabled in new[] { true, false })
            {
                variant.SetPlanning(enabled);
                var start = Stopwatch.GetTimestamp();
                do { run(); } while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 300);
            }

            var count = Calibrate(run);
            for (var round = 0; round < 64; round++)
            {
                foreach (var enabled in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
                {
                    variant.SetPlanning(enabled);
                    var allocated = GC.GetTotalAllocatedBytes(true);
                    var started = Stopwatch.GetTimestamp();
                    for (var i = 0; i < count; i++) run();
                    var elapsed = Stopwatch.GetElapsedTime(started);
                    rows.Add(new Sample(method, enabled ? "on" : "off", round, count, elapsed.TotalNanoseconds / count,
                        (GC.GetTotalAllocatedBytes(true) - allocated) / (double)count));
                }
            }
        }

        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { rows }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Saved planner on/off samples to " + output);
    }

    private sealed record Sample(string Method, string Variant, int Round, int Count, double Nanoseconds, double AllocatedBytes);

    private sealed class Variant : IDisposable
    {
        private readonly LibraryContext _context;
        private readonly object _fixture;
        private readonly MethodInfo _setup;
        private readonly MethodInfo _cleanup;

        public Variant(string name, string path)
        {
            Name = name;
            Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path)!, "Inertia.Net.dll"))));
            _context = new LibraryContext(name, path);
            var assembly = _context.LoadFromAssemblyPath(Path.GetFullPath(path));
            var type = assembly.GetType("Inertia.Net.Benchmarks.ReviewLatencyBenchmarks", throwOnError: true)!;
            _fixture = Activator.CreateInstance(type)!;
            _setup = type.GetMethod("Setup")!;
            _cleanup = type.GetMethod("Cleanup")!;
            Actions = Methods.ToDictionary(method => method, method => ActionFor(type.GetMethod(method)!));
        }

        public string Name { get; }
        public string Hash { get; }
        public Dictionary<string, Action> Actions { get; }
        public Task SetupAsync() => (Task)_setup.Invoke(_fixture, null)!;

        public void SetPlanning(bool enabled) => _context.Assemblies.Single(assembly => assembly.GetName().Name == "Inertia.Net")
            .GetType("Inertia.Net.InertiaProp")!.GetField("ParallelUsed", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, enabled);

        private Action ActionFor(MethodInfo method)
        {
            if (method.ReturnType == typeof(Task))
            {
                var run = method.CreateDelegate<Func<Task>>(_fixture);
                return () => run().GetAwaiter().GetResult();
            }

            if (method.ReturnType == typeof(byte[]))
            {
                var run = method.CreateDelegate<Func<byte[]>>(_fixture);
                return () => GC.KeepAlive(run());
            }

            return method.CreateDelegate<Action>(_fixture);
        }

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
            // Share the platform; each variant gets its own Inertia assemblies, metadata and fixtures.
            if (name.Name!.StartsWith("System", StringComparison.Ordinal)
                || name.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || name.Name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)) return null;
            return _resolver.ResolveAssemblyToPath(name) is { } assembly ? LoadFromAssemblyPath(assembly) : null;
        }
    }
}
