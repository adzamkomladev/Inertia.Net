using System.Text;
using BenchmarkDotNet.Running;
using Inertia.Net.Benchmarks;

if (args is ["--load-server"]) { await LoadProbe.ServerAsync(); return; }
if (args is ["--process-memory", var processMode, var processOutput]) { await ProcessMemoryProbe.RunAsync(processMode, processOutput); return; }
if (args is ["--render-probe"]) { await RenderProbe.RunAsync(); return; }
if (args is ["--mvc-probe"]) { MvcProbe.Run(); return; }
if (args is ["--payload-probe"]) { await PayloadProbe.RunAsync(); return; }
if (args is ["--cache-probe"]) { await CacheProbe.RunAsync(); return; }
if (args is ["--allocation-probe"]) { await AllocationProbe.RunAsync(); return; }
if (args is ["--paired-latency", var variants, var pairedOutput]) { await PairedLatencyProbe.RunAsync(variants, pairedOutput); return; }
if (args is ["--memory-comparison", var memoryDirectory, var memoryOutput]) { await PairedLatencyProbe.RunAsync(memoryDirectory, memoryOutput, memory: true); return; }
if (args is ["--fix-comparison", var fixDirectory, var fixOutput]) { await PairedLatencyProbe.RunAsync(fixDirectory, fixOutput, memory: true, fixes: true); return; }
if (args is ["--planner-cost", var plannerDirectory, var plannerOutput]) { await PairedLatencyProbe.RunPlanningCostAsync(plannerDirectory, plannerOutput); return; }
if (args is ["--load", var output]) { await LoadProbe.RunAsync(output); return; }
if (args is ["--ssr-probe"])
{
    var benchmark = new SsrBufferingBenchmarks { PayloadBytes = 1048576 };
    benchmark.Setup();
    for (var repetition = 1; repetition <= 3; repetition++)
    {
        foreach (var (name, run) in new (string, Func<Task<int?>>)[] { ("buffered", () => benchmark.BufferedReference()), ("gateway", () => benchmark.Streaming().AsTask()) })
        {
            var cold = System.Diagnostics.Stopwatch.GetTimestamp();
            await run();
            var coldMs = System.Diagnostics.Stopwatch.GetElapsedTime(cold).TotalMilliseconds;
            for (var i = 0; i < 20; i++) await run();
            var before = GC.GetTotalAllocatedBytes(true);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var i = 0; i < 100; i++)
                if (await run() != 1048576) throw new InvalidOperationException("SSR probe returned the wrong body.");
            Console.WriteLine($"run={repetition} {name}: {(GC.GetTotalAllocatedBytes(true) - before) / 100} B/request, {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds / 100:F3} ms/request; first-call={coldMs:F3} ms");
        }
    }
    benchmark.Cleanup();
    return;
}

// `dotnet run -c Release -- --check` prints what each render benchmark produces, to verify the setup before measuring.
if (args is ["--check"])
{
    var bench = new RenderBenchmarks();
    bench.Setup();
    foreach (var (name, run) in new (string, Func<Microsoft.AspNetCore.Http.HttpContext, Task>)[]
    {
        ("json", c => Render(BenchApp.Component, BenchApp.Props20()).ExecuteAsync(c)),
        ("poco", c => Render(BenchApp.Component, BenchApp.Poco()).ExecuteAsync(c)),
    })
    {
        var context = BenchApp.CreateContext(BenchApp.CreateServices(), "/users", (InertiaHeaders.Inertia, "true"));
        context.Response.Body = new MemoryStream();
        await run(context);
        Console.WriteLine($"{name}: {context.Response.StatusCode} {Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())}\n");
    }

    var html = BenchApp.CreateContext(BenchApp.CreateServices());
    html.Response.Body = new MemoryStream();
    await Render(BenchApp.Component, BenchApp.Props20()).ExecuteAsync(html);
    Console.WriteLine($"html: {Encoding.UTF8.GetString(((MemoryStream)html.Response.Body).ToArray())}");
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
