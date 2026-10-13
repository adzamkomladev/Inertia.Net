using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Inertia.Net.Benchmarks;

/// <summary>A separate Kestrel process keeps client allocations out of server allocation measurements.</summary>
internal static class LoadProbe
{
    public static async Task ServerAsync()
    {
        using var allocations = new LohSamples();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = BenchApp.CreateContentRoot(), Args = [] });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, BenchJsonContext.Default));
        builder.Services.AddInertia(o => { o.Version = "load"; o.Share("appName", "Bench"); o.Share("auth", _ => new AuthInfo(null)); });
        await using var app = builder.Build();
        app.UseInertia();
        var small = BenchApp.Props20();
        var large = new InertiaProps { ["data"] = new string('x', 1048576) };
        app.MapGet("/small", () => Render(BenchApp.Component, small));
        app.MapGet("/large", () => Render(BenchApp.Component, large));
        app.MapGet("/__metrics", () =>
        {
            var info = GC.GetGCMemoryInfo();
            using var process = Process.GetCurrentProcess();
            return string.Join(',', GC.GetTotalAllocatedBytes(precise: true), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                process.WorkingSet64, process.PeakWorkingSet64, info.HeapSizeBytes, info.TotalCommittedBytes,
                info.GenerationInfo.Length > 3 ? info.GenerationInfo[3].SizeAfterBytes : 0, allocations.Bytes, allocations.Ticks);
        });
        await app.StartAsync();
        Console.WriteLine("READY " + app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        await app.WaitForShutdownAsync();
    }

    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(LoadProbe).Assembly.Location);
        start.ArgumentList.Add("--load-server");
        using var server = Process.Start(start)!;
        var errorTask = server.StandardError.ReadToEndAsync();
        try
        {
            var ready = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if (ready is null || !ready.StartsWith("READY ", StringComparison.Ordinal)) throw new InvalidOperationException("Load server did not start: " + ready);
            using var client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 128 }) { BaseAddress = new Uri(ready[6..]), Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add(InertiaHeaders.Inertia, "true");
            client.DefaultRequestHeaders.Add(InertiaHeaders.Version, "load");
            var rows = new List<object>();
            foreach (var endpoint in new[] { "/small", "/large" })
            {
                var cold = Stopwatch.GetTimestamp();
                await RequestAsync(client, endpoint);
                var coldMs = Stopwatch.GetElapsedTime(cold).TotalMilliseconds;
                for (var i = 0; i < 100; i++) await RequestAsync(client, endpoint);
                foreach (var concurrency in new[] { 1, 32, 128 })
                {
                    for (var run = 1; run <= 3; run++)
                    {
                        var before = await MetricsAsync(client);
                        var samples = Enumerable.Range(0, concurrency).Select(_ => new List<double>()).ToArray();
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        var started = Stopwatch.GetTimestamp();
                        await Task.WhenAll(samples.Select(async times =>
                        {
                            while (!deadline.IsCancellationRequested)
                            {
                                var tick = Stopwatch.GetTimestamp();
                                await RequestAsync(client, endpoint);
                                times.Add(Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
                            }
                        }));
                        var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
                        var after = await MetricsAsync(client);
                        var latencies = samples.SelectMany(s => s).Order().ToArray();
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        var retained = await MetricsAsync(client);
                        var row = new
                        {
                            endpoint,
                            concurrency,
                            run,
                            coldMs,
                            requests = latencies.Length,
                            requestsPerSecond = latencies.Length / elapsed,
                            p50Ms = Percentile(latencies, .50),
                            p95Ms = Percentile(latencies, .95),
                            p99Ms = Percentile(latencies, .99),
                            allocatedBytesPerRequest = (after[0] - before[0]) / (double)latencies.Length,
                            gen0 = after[1] - before[1],
                            gen1 = after[2] - before[2],
                            gen2 = after[3] - before[3],
                            workingSetBytes = after[4],
                            peakWorkingSetBytes = after[5],
                            heapBytes = after[6],
                            committedBytes = after[7],
                            lohAfterLastGcBytes = after[8],
                            retainedWorkingSetBytesAfterOneSecond = retained[4],
                            sampledLohBytes = after[9] - before[9],
                            sampledLohAllocationTicks = after[10] - before[10],
                        };
                        rows.Add(row);
                        Console.WriteLine(JsonSerializer.Serialize(row));
                    }
                }
            }
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
            {
                environment = new { os = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount },
                measuredAtUtc = DateTimeOffset.UtcNow,
                rows,
            }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        }
        finally
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
            var errors = await errorTask;
            if (errors.Length > 0) Console.Error.WriteLine(errors);
        }
    }

    private static async Task RequestAsync(HttpClient client, string endpoint)
    {
        using var response = await client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException($"Load endpoint returned {response.StatusCode}.");
        using var body = await response.Content.ReadAsStreamAsync();
        await body.CopyToAsync(Stream.Null);
    }
    private static async Task<long[]> MetricsAsync(HttpClient client) =>
        (await client.GetStringAsync("/__metrics")).Split(',').Select(s => long.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    private static double Percentile(double[] values, double p) => values[(int)Math.Ceiling(values.Length * p) - 1];

    private sealed class LohSamples : EventListener
    {
        private long _bytes;
        private long _ticks;
        internal long Bytes => Interlocked.Read(ref _bytes);
        internal long Ticks => Interlocked.Read(ref _ticks);
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)1);
        }
        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventId != 10 || e.PayloadNames is null || e.Payload is null) return;
            var kind = e.PayloadNames.IndexOf("AllocationKind");
            var amount = e.PayloadNames.IndexOf("AllocationAmount64");
            if (kind < 0 || amount < 0 || Convert.ToInt32(e.Payload[kind], CultureInfo.InvariantCulture) != 1) return;
            Interlocked.Add(ref _bytes, Convert.ToInt64(e.Payload[amount], CultureInfo.InvariantCulture));
            Interlocked.Increment(ref _ticks);
        }
    }
}
