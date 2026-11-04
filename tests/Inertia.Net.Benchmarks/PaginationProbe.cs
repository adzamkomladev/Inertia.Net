using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Inertia.Net.Benchmarks;

/// <summary>Counterbalanced pagination measurements without BenchmarkDotNet's generated runtime.</summary>
internal static class PaginationProbe
{
    internal static async Task RunAsync(string? output = null)
    {
        var fixture = new PaginationBenchmarks();
        fixture.Setup();
        try
        {
            Action valid = () => fixture.RequestValid();
            Action garbage = () => fixture.RequestGarbage();
            Action cursor = fixture.CursorRoundTrip;
            Action serialize = fixture.PlainSerialize;
            var methods = new (string Name, Action Baseline, Action Current)[]
            {
                ("Full", () => fixture.HandRolledFull().GetAwaiter().GetResult(), () => fixture.PaginateFull().GetAwaiter().GetResult()),
                ("PartialPrepend", () => fixture.HandRolledPartial().GetAwaiter().GetResult(), () => fixture.PaginatePartial().GetAwaiter().GetResult()),
                ("RequestValid", valid, valid),
                ("RequestGarbage", garbage, garbage),
                ("CursorRoundTrip", cursor, cursor),
                ("PlainSerialize", serialize, serialize),
            };
            foreach (var (_, baseline, current) in methods)
            {
                Warm(baseline);
                Warm(current);
            }

            var rows = new List<Sample>();
            foreach (var (name, baseline, current) in methods)
            {
                var count = Math.Min(Calibrate(baseline), Calibrate(current));
                for (var round = 0; round < 32; round++)
                {
                    if (round % 2 == 0)
                    {
                        rows.Add(Measure(name, "baseline", round, count, baseline));
                        rows.Add(Measure(name, "current", round, count, current));
                    }
                    else
                    {
                        rows.Add(Measure(name, "current", round, count, current));
                        rows.Add(Measure(name, "baseline", round, count, baseline));
                    }
                }

                foreach (var variant in new[] { "baseline", "current" })
                {
                    var samples = rows.Where(row => row.Method == name && row.Variant == variant).ToArray();
                    Console.WriteLine($"{name} {variant}: {Median(samples.Select(row => row.AllocatedBytes)):F3} B/op; {Median(samples.Select(row => row.Nanoseconds)) / 1000000:F6} ms/op");
                }
            }

            if (output is not null)
            {
                var assembly = typeof(PaginationBenchmarks).Assembly.Location;
                var core = typeof(Inertia).Assembly.Location;
                var hashes = new Dictionary<string, string>
                {
                    ["benchmarks"] = Hash(assembly),
                    ["core"] = Hash(core),
                };
                var environment = new
                {
                    measuredAtUtc = DateTimeOffset.UtcNow,
                    runtime = RuntimeInformation.FrameworkDescription,
                    os = RuntimeInformation.OSDescription,
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    processorCount = Environment.ProcessorCount,
                    processAffinity = OperatingSystem.IsLinux() || OperatingSystem.IsWindows() ? Process.GetCurrentProcess().ProcessorAffinity.ToInt64().ToString("X") : null,
                    serverGc = GCSettings.IsServerGC,
                    latencyMode = GCSettings.LatencyMode.ToString(),
                };
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
                {
                    rounds = 32,
                    comparison = "Same-build hand-written and helper pages with byte-identical JSON; controls call the same method in both slots.",
                    environment,
                    hashes,
                    rows,
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Saved paired samples to " + output);
            }
        }
        finally
        {
            fixture.Cleanup();
        }
    }

    private static void Warm(Action run)
    {
        var started = Stopwatch.GetTimestamp();
        do { run(); } while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 300);
    }

    private static int Calibrate(Action run)
    {
        for (var count = 1; ; count *= 2)
        {
            var started = Stopwatch.GetTimestamp();
            for (var i = 0; i < count; i++) run();
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 20 || count >= 1048576) return count;
        }
    }

    private static Sample Measure(string method, string variant, int round, int count, Action run)
    {
        var allocated = GC.GetTotalAllocatedBytes(true);
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < count; i++) run();
        var elapsed = Stopwatch.GetElapsedTime(started);
        return new Sample(method, variant, round, count, elapsed.TotalNanoseconds / count,
            (GC.GetTotalAllocatedBytes(true) - allocated) / (double)count);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed record Sample(string Method, string Variant, int Round, int Count, double Nanoseconds, double AllocatedBytes);
}
