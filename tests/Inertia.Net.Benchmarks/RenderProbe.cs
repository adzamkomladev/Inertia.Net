using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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

        await ShapesAsync();
    }

    // Prop shapes of the 1.9.3 plan: best of 25 interleaved rounds, so rows are compared under the same host load.
    private static async Task ShapesAsync()
    {
        var services = BenchApp.CreateServices();
        var options = services.GetRequiredService<InertiaPageWriter>().SerializerOptions;
        var context = BenchApp.CreateContext(services, "/users", (InertiaHeaders.Inertia, "true"), (InertiaHeaders.Version, BenchApp.Version(services)));
        var rows = BenchApp.ObjectRows();
        var rowProps = new InertiaProps { ["rows"] = rows };
        var root = BenchApp.Scalars();
        var nested = new InertiaProps { ["page"] = root };
        var rowsInfo = options.GetTypeInfo(typeof(ObjectRow[]));
        var buffer = new ArrayBufferWriter<byte>(256 * 1024);
        using var writer = new Utf8JsonWriter(buffer);
        Task Page(object props)
        {
            context.Response.Headers.Clear();
            return Render(BenchApp.Component, props).ExecuteAsync(context);
        }

        var shapes = new (string Name, int Iterations, Func<Task> Run)[]
        {
            ("rows1000 object member", 40, () => Page(rowProps)),
            ("rows1000 object member, STJ reference", 40, () =>
            {
                buffer.ResetWrittenCount();
                writer.Reset(buffer);
                JsonSerializer.Serialize(writer, rows, rowsInfo);
                writer.Flush();
                return Task.CompletedTask;
            }),
            ("root plain object", 4000, () => Page(root)),
            ("same object nested", 4000, () => Page(nested)),
        };

        var best = new double[shapes.Length];
        var bytes = new long[shapes.Length];
        Array.Fill(best, double.MaxValue);
        Array.Fill(bytes, long.MaxValue);
        for (var round = 0; round < 26; round++)
        {
            for (var s = 0; s < shapes.Length; s++)
            {
                var (_, iterations, run) = shapes[s];
                var allocated = GC.GetTotalAllocatedBytes(true);
                var started = Stopwatch.GetTimestamp();
                for (var i = 0; i < iterations; i++) await run();
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMicroseconds / iterations;
                if (round == 0) continue; // warm-up
                best[s] = Math.Min(best[s], elapsed);
                bytes[s] = Math.Min(bytes[s], (GC.GetTotalAllocatedBytes(true) - allocated) / iterations);
            }
        }

        for (var s = 0; s < shapes.Length; s++)
        {
            Console.WriteLine($"{shapes[s].Name}: {best[s]:F2} us; {bytes[s]} B");
        }
    }
}
