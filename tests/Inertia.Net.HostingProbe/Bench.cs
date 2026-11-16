using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Inertia.Net.HostingProbe;

/// <summary>
/// Starts each server build in its own process and measures it from this one: RSS at rest, under load and after, requests per second,
/// latency percentiles, CPU used and startup time. Linux only (reads /proc).
/// <c>--suite hosting</c> (the default) compares Inertia.Net with a plain Minimal API; <c>--suite ssr</c> compares client-rendered and
/// server-rendered first loads of the ClientApp pages, counting the managed SSR process's CPU and RSS with the server's.
/// </summary>
internal static class Bench
{
    // Env: space-separated NAME=value pairs for the server process. Render: drive the SSR process's /render endpoint directly with the
    // page JSON of Path, bypassing the .NET server.
    private sealed record Scenario(string Name, string Mode, string Path, bool InertiaVisit, bool SignIn = false, string Env = "", bool Render = false);

    private sealed record Build(string Name, bool Aot, bool ServerGc);

    private sealed record Result(double Rps, double P50Ms, double P95Ms, double P99Ms, double P999Ms, double RestMb, double PeakMb, double AfterMb, double SsrRestMb, double SsrPeakMb,
        double Cores, double StartupMs, long Bytes, long Errors, double AllocatedPerRequest, long NotRendered, RateResult[] Rates)
    {
        // Server CPU time per request: holds up better than req/s when other work on the machine takes CPU from the client or server.
        public double CpuMicros => Cores * 1_000_000 / Rps;
    }

    // Latency at a fixed request rate, from when each request was due rather than when it was sent (no coordinated omission).
    private sealed record RateResult(int Target, double Rps, double P50Ms, double P95Ms, double P99Ms, long Errors);

    private sealed record Load(double Rps, double P50Ms, double P95Ms, double P99Ms, double P999Ms, long Errors, double Seconds);

    private static readonly Scenario[] Scenarios =
    [
        new("Plain JSON API", "plain", "/api/users", false),
        new("Plain HTML shell", "plain", "/users", false),
        new("Inertia visit (JSON)", "inertia", "/users", true),
        new("Inertia first load (HTML)", "inertia", "/users", false),
        // A small app: cookie sign-in, and the signed-in user's 25 todos from SQLite (EF Core on JIT, Microsoft.Data.Sqlite on AOT).
        new("Todo app visit (SQLite, cookie auth)", "todo", "/todos", true, SignIn: true),
    ];

    private const string NodeSsr = "PROBE_SSR=node";

    // The SPA/SSR comparison (PROBE_MODE=ssr): the todo list at 10, 25 and 100 rows, and the anonymous home page. The Inertia visit
    // runs first and last: SSR is not involved in visits, so the two should agree, and a gap between them is noise on the machine.
    private static readonly Scenario[] SsrScenarios =
    [
        new("Inertia visit (JSON), SSR on, 25 rows", "ssr", "/todos?perPage=25", true, Env: NodeSsr),
        new("SPA first load (HTML, SSR off), 25 rows", "ssr", "/todos?perPage=25", false, Env: "PROBE_SSR=off"),
        new("SSR first load, Node, 10 rows", "ssr", "/todos?perPage=10", false, Env: NodeSsr),
        new("SSR first load, Node, 25 rows", "ssr", "/todos?perPage=25", false, Env: NodeSsr),
        new("SSR first load, Node, 100 rows", "ssr", "/todos?perPage=100", false, Env: NodeSsr),
        new("SSR first load, Bun, 25 rows", "ssr", "/todos?perPage=25", false, Env: "PROBE_SSR=bun"),
        new("Anonymous home, Node SSR + render cache", "ssr", "/", false, Env: NodeSsr + " PROBE_SSR_CACHE=1"),
        new("Node /render direct, 25 rows", "ssr", "/todos?perPage=25", true, Env: NodeSsr, Render: true),
        new("Inertia visit (JSON), SSR on, 25 rows (A/A repeat)", "ssr", "/todos?perPage=25", true, Env: NodeSsr),
    ];

    // Every page of PROBE_MODE=ssr, requested once (HTML and JSON) before any measurement, so each page's code is loaded in both processes.
    private static readonly string[] SsrPages = ["/", "/todos?perPage=10", "/todos?perPage=25", "/todos?perPage=100"];

    // Elements in the server-rendered <body> per page, counted once per run from the SSR HTML.
    private static readonly Dictionary<string, int> ElementCounts = new(StringComparer.Ordinal);

    private static readonly Build[] Builds =
    [
        new("JIT, server GC", false, true),
        new("JIT, workstation GC", false, false),
        new("Native AOT, server GC", true, true),
        new("Native AOT, workstation GC", true, false),
    ];

    public static async Task RunAsync(string jitDll, string aotBinary, string output, string[] options)
    {
        var seconds = Option(options, "--seconds", 10);
        var warmup = Option(options, "--warmup", 5);
        var ssrSuite = (options.SkipWhile(o => o != "--suite").Skip(1).FirstOrDefault() ?? "hosting") switch
        {
            "hosting" => false,
            "ssr" => true,
            var other => throw new ArgumentException($"--suite must be hosting or ssr, not '{other}'."),
        };
        // 8 by default for the ssr suite: one SSR process takes 8 renders in flight (SsrOptions.Concurrency.MaxActive); more connections
        // only queue in the gateway, where a wait past QueueTimeout fails the request in Required mode, measuring the queue policy instead.
        var connections = Option(options, "--connections", ssrSuite ? 8 : 64);
        var repeat = Option(options, "--repeat", 3);
        // --builds jit|aot|all; the ssr suite runs the server GC builds only.
        var buildFilter = options.SkipWhile(o => o != "--builds").Skip(1).FirstOrDefault() ?? "all";
        if (buildFilter is not ("jit" or "aot" or "all")) throw new ArgumentException($"--builds must be jit, aot or all, not '{buildFilter}'.");
        var builds = Builds.Where(b => (buildFilter == "all" || b.Aot == (buildFilter == "aot")) && (!ssrSuite || b.ServerGc)).ToArray();
        var cpus = options.SkipWhile(o => o != "--server-cpus").Skip(1).FirstOrDefault() ?? "0-3";
        // --only <text>: run only the scenarios whose name contains the text, e.g. --only Todo.
        var only = options.SkipWhile(o => o != "--only").Skip(1).FirstOrDefault();
        // --rates 1000,4000: after the saturation run, hold each rate for --seconds on the same server process (server GC builds only).
        var rates = (options.SkipWhile(o => o != "--rates").Skip(1).FirstOrDefault() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => int.Parse(r, CultureInfo.InvariantCulture)).ToArray();
        var scenarios = (ssrSuite ? SsrScenarios : Scenarios).Where(s => only is null || s.Name.Contains(only, StringComparison.OrdinalIgnoreCase)).ToArray();
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"{connections} keep-alive connections, {warmup} s warmup, {seconds} s measured, {repeat} rounds; each round starts a fresh server process for every row, in table order. Server on CPUs {cpus}. Medians across rounds.");
        report.AppendLine();
        if (ssrSuite)
        {
            report.AppendLine("| Build | Scenario | Response | Req/s | p50 ms | p95 ms | p99 ms | p99.9 ms | Server + SSR CPU per request | .NET RSS rest / peak | SSR RSS rest / peak | Allocated per request |");
            report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        }
        else
        {
            report.AppendLine("| Build | Scenario | Response | Req/s | p50 ms | p95 ms | p99 ms | Server CPU per request | RSS at rest | Peak RSS under load | RSS after load | Startup |");
            report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        }

        // Rounds outermost, so a slow patch on a shared machine hits every row of a round rather than all rounds of one row.
        var results = new Dictionary<(Build, Scenario), List<Result>>();
        for (var i = 0; i < repeat; i++)
        {
            foreach (var build in builds)
            {
                foreach (var scenario in scenarios)
                {
                    var client = Process.GetCurrentProcess().TotalProcessorTime;
                    var result = await RunOnceAsync(build, scenario, build.Aot ? aotBinary : jitDll, cpus, warmup, seconds, connections, build.ServerGc ? rates : []);
                    var clientCores = (Process.GetCurrentProcess().TotalProcessorTime - client).TotalSeconds / (warmup + seconds);
                    Console.WriteLine(FormattableString.Invariant(
                        $"{build.Name} | {scenario.Name} | round {i + 1}: {result.Rps:F0} req/s, p50 {result.P50Ms:F2} ms, p95 {result.P95Ms:F2} ms, p99 {result.P99Ms:F2} ms, p99.9 {result.P999Ms:F2} ms, rest {result.RestMb:F1} MB, peak {result.PeakMb:F1} MB, after {result.AfterMb:F1} MB, server {result.Cores:F2} cores ({result.CpuMicros:F0} µs/request), client ~{clientCores:F2} cores, startup {result.StartupMs:F0} ms, errors {result.Errors}"));
                    if (ssrSuite)
                    {
                        Console.WriteLine(FormattableString.Invariant(
                            $"{build.Name} | {scenario.Name} | round {i + 1}: SSR process RSS rest {result.SsrRestMb:F1} MB, peak {result.SsrPeakMb:F1} MB, {result.AllocatedPerRequest:N0} B allocated per request, {result.NotRendered} renders not server-rendered"));
                    }

                    foreach (var rate in result.Rates)
                    {
                        Console.WriteLine(FormattableString.Invariant(
                            $"{build.Name} | {scenario.Name} | round {i + 1} at {rate.Target} req/s: {rate.Rps:F0} req/s, p50 {rate.P50Ms:F2} ms, p95 {rate.P95Ms:F2} ms, p99 {rate.P99Ms:F2} ms, errors {rate.Errors}"));
                    }

                    (results.TryGetValue((build, scenario), out var list) ? list : results[(build, scenario)] = []).Add(result);
                }
            }
        }

        foreach (var build in builds)
        {
            foreach (var scenario in scenarios)
            {
                var runs = results[(build, scenario)];
                if (ssrSuite)
                {
                    // Any failed request, or any render that was not server-rendered, makes the row's numbers describe a different workload.
                    var failed = runs.Sum(r => r.Errors + r.NotRendered);
                    report.AppendLine(failed > 0
                        ? FormattableString.Invariant($"| {build.Name} | {scenario.Name} | {runs[0].Bytes:N0} B | overloaded ({failed:N0} failed or not server-rendered) | | | | | | | | |")
                        : FormattableString.Invariant(
                            $"| {build.Name} | {scenario.Name} | {runs[0].Bytes:N0} B | {Median(runs, r => r.Rps):N0} | {Median(runs, r => r.P50Ms):F2} | {Median(runs, r => r.P95Ms):F2} | {Median(runs, r => r.P99Ms):F2} | {Median(runs, r => r.P999Ms):F2} | {Median(runs, r => r.CpuMicros):F0} µs | {Median(runs, r => r.RestMb):F0} / {Median(runs, r => r.PeakMb):F0} MB | {Median(runs, r => r.SsrRestMb):F0} / {Median(runs, r => r.SsrPeakMb):F0} MB | {Median(runs, r => r.AllocatedPerRequest):N0} B |"));
                    continue;
                }

                report.AppendLine(FormattableString.Invariant(
                    $"| {build.Name} | {scenario.Name} | {runs[0].Bytes:N0} B | {Median(runs, r => r.Rps):N0} | {Median(runs, r => r.P50Ms):F2} | {Median(runs, r => r.P95Ms):F2} | {Median(runs, r => r.P99Ms):F2} | {Median(runs, r => r.CpuMicros):F0} µs | {Median(runs, r => r.RestMb):F0} MB | {Median(runs, r => r.PeakMb):F0} MB | {Median(runs, r => r.AfterMb):F0} MB | {Median(runs, r => r.StartupMs):F0} ms |"));
            }
        }

        if (ssrSuite)
        {
            report.AppendLine();
            report.AppendLine("Server-rendered elements in <body>: " + string.Join(", ", SsrPages.Where(ElementCounts.ContainsKey).Select(p => FormattableString.Invariant($"`{p}` {ElementCounts[p]:N0}"))) + ". " +
                "SSR rows run SsrMode.Required with one managed SSR process on the server's CPUs; CPU and RSS columns add up the .NET server and its SSR child process. " +
                "Allocated per request is the .NET server's allocations over the measured window.");
        }

        if (rates.Length > 0)
        {
            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"Latency at a fixed rate: {connections} connections send on a schedule for {seconds} s per rate, right after the saturation run on the same server process. A request's latency counts from when it was due, so time spent waiting behind a slow response is included. \"Overloaded\" means the server could not keep up with the rate.");
            report.AppendLine();
            report.AppendLine("| Build | Scenario | Rate | p50 ms | p95 ms | p99 ms |");
            report.AppendLine("|---|---|---:|---:|---:|---:|");
            foreach (var build in builds.Where(b => b.ServerGc))
            {
                foreach (var scenario in scenarios)
                {
                    var runs = results[(build, scenario)];
                    for (var r = 0; r < rates.Length; r++)
                    {
                        var at = runs.Select(run => run.Rates[r]).ToList();
                        // Behind schedule: the queue grows for the whole run, so its percentiles only measure the run's length.
                        report.AppendLine(Median(at, a => a.Rps) < rates[r] * 0.97
                            ? FormattableString.Invariant($"| {build.Name} | {scenario.Name} | {rates[r]:N0} req/s | overloaded | overloaded | overloaded |")
                            : FormattableString.Invariant($"| {build.Name} | {scenario.Name} | {rates[r]:N0} req/s | {Median(at, a => a.P50Ms):F2} | {Median(at, a => a.P95Ms):F2} | {Median(at, a => a.P99Ms):F2} |"));
                    }
                }
            }
        }

        await File.WriteAllTextAsync(output, report.ToString());
        Console.WriteLine();
        Console.WriteLine(report);
    }

    private static async Task<Result> RunOnceAsync(Build build, Scenario scenario, string program, string cpus, int warmup, int seconds, int connections, int[] rates)
    {
        var port = FreePort();
        var ssrPort = FreePort();
        var ssrPages = scenario.Mode == "ssr";
        var start = new ProcessStartInfo("taskset") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in build.Aot ? ["-c", cpus, program] : new[] { "-c", cpus, "dotnet", program })
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["PROBE_MODE"] = scenario.Mode;
        start.Environment["DOTNET_gcServer"] = build.ServerGc ? "1" : "0";
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["DOTNET_TieredPGO"] = null; // the runtime default
        start.Environment["PROBE_SSR_PORT"] = ssrPort.ToString(CultureInfo.InvariantCulture);
        foreach (var pair in scenario.Env.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            start.Environment[pair[..pair.IndexOf('=')]] = pair[(pair.IndexOf('=') + 1)..];
        }

        var started = Stopwatch.GetTimestamp();
        using var server = Process.Start(start)!;
        server.OutputDataReceived += (_, _) => { };
        server.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine("server: " + e.Data); };
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();
        try
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, port);
            var health = Encoding.ASCII.GetBytes("GET /healthz HTTP/1.1\r\nHost: probe\r\n\r\n");
            while (true)
            {
                try
                {
                    await using var probe = await Connection.OpenAsync(endpoint);
                    if (await probe.SendAsync(health) == 200) break;
                }
                catch (SocketException) when (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
                {
                    await Task.Delay(5);
                }
            }

            var startupMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (ssrPages) await PrewarmAsync(endpoint, ssr: scenario.Env.Contains("PROBE_SSR=node", StringComparison.Ordinal) || scenario.Env.Contains("PROBE_SSR=bun", StringComparison.Ordinal));

            var cookie = "";
            if (scenario.SignIn)
            {
                await using var login = await Connection.OpenAsync(endpoint);
                login.CaptureCookie = true;
                if (await login.SendAsync(Encoding.ASCII.GetBytes("GET /probe-login HTTP/1.1\r\nHost: probe\r\n\r\n")) != 204 || login.LastSetCookie is null)
                    throw new InvalidOperationException("Sign-in failed.");
                cookie = $"Cookie: {login.LastSetCookie}\r\n";
            }

            var request = Encoding.ASCII.GetBytes(
                $"GET {scenario.Path} HTTP/1.1\r\nHost: probe\r\nAccept: text/html, application/json\r\n" + cookie +
                (scenario.InertiaVisit ? "X-Inertia: true\r\nX-Inertia-Version: 1\r\n" : "") + "\r\n");
            long bytes;
            await using (var first = await Connection.OpenAsync(endpoint))
            {
                first.CaptureBody = scenario.Render;
                if (await first.SendAsync(request) != 200) throw new InvalidOperationException($"{scenario.Name} did not answer 200.");
                bytes = first.LastBodyBytes;
                if (scenario.Render)
                {
                    // The same page JSON the gateway posts, sent straight to the SSR process: its cost without the .NET side.
                    var page = first.LastBody!;
                    request = [.. Encoding.ASCII.GetBytes($"POST /render HTTP/1.1\r\nHost: probe\r\nContent-Type: application/json\r\nContent-Length: {page.Length}\r\n\r\n"), .. page];
                    endpoint = new IPEndPoint(IPAddress.Loopback, ssrPort);
                    await using var render = await Connection.OpenAsync(endpoint);
                    if (await render.SendAsync(request) != 200) throw new InvalidOperationException($"{scenario.Name}: /render did not answer 200.");
                    bytes = render.LastBodyBytes;
                }
            }

            var metricsEndpoint = new IPEndPoint(IPAddress.Loopback, port);
            await Task.Delay(2000);
            var restMb = RssMb(server.Id);
            var ssrRestMb = ChildRssMb(server.Id);

            var stop = false;
            var peakMb = restMb;
            var ssrPeakMb = ssrRestMb;
            var sampler = Task.Run(async () =>
            {
                while (!Volatile.Read(ref stop))
                {
                    peakMb = Math.Max(peakMb, RssMb(server.Id));
                    ssrPeakMb = Math.Max(ssrPeakMb, ChildRssMb(server.Id));
                    await Task.Delay(100);
                }
            });
            long cpuBefore = 0, cpuAfter = 0;
            Task<ProbeMetrics>? metricsBefore = null, metricsAfter = null;
            var load = await LoadAsync(endpoint, request, connections, 0, warmup, seconds,
                () =>
                {
                    cpuBefore = TreeCpuTicks(server.Id);
                    if (ssrPages) metricsBefore = MetricsAsync(metricsEndpoint);
                },
                () =>
                {
                    cpuAfter = TreeCpuTicks(server.Id);
                    if (ssrPages) metricsAfter = MetricsAsync(metricsEndpoint);
                });
            Volatile.Write(ref stop, true);
            await sampler;
            await Task.Delay(3000);
            var afterMb = RssMb(server.Id);

            var rateResults = new List<RateResult>();
            foreach (var rate in rates)
            {
                var atRate = await LoadAsync(endpoint, request, connections, rate, 2, seconds, null, null);
                rateResults.Add(new RateResult(rate, atRate.Rps, atRate.P50Ms, atRate.P95Ms, atRate.P99Ms, atRate.Errors));
            }

            double allocated = 0;
            long notRendered = 0;
            if (ssrPages)
            {
                var before = await metricsBefore!;
                var after = await metricsAfter!;
                allocated = (after.AllocatedBytes - before.AllocatedBytes) / (load.Rps * load.Seconds);
                Console.WriteLine(FormattableString.Invariant(
                    $"{build.Name} | {scenario.Name}: GC in the measured window: gen0 {after.Gen0 - before.Gen0}, gen1 {after.Gen1 - before.Gen1}, gen2 {after.Gen2 - before.Gen2}, pause {after.GcPauseMs - before.GcPauseMs:F0} ms"));
                // Read after the rate runs too, so a fallback anywhere in this server's life marks the row.
                notRendered = (await MetricsAsync(metricsEndpoint)).SsrOutcomes.Where(o => !o.Key.StartsWith("rendered/", StringComparison.Ordinal)).Sum(o => o.Value);
            }

            return new Result(load.Rps, load.P50Ms, load.P95Ms, load.P99Ms, load.P999Ms, restMb, peakMb, afterMb, ssrRestMb, ssrPeakMb,
                (cpuAfter - cpuBefore) / 100.0 / load.Seconds, startupMs, bytes, load.Errors, allocated, notRendered, [.. rateResults]);
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    // Requests every PROBE_MODE=ssr page once as HTML and as JSON, retrying until the SSR process answers, and counts the elements of
    // each server-rendered page once per run.
    private static async Task PrewarmAsync(IPEndPoint endpoint, bool ssr)
    {
        var started = Stopwatch.GetTimestamp();
        await using var connection = await Connection.OpenAsync(endpoint);
        connection.CaptureBody = true;
        foreach (var path in SsrPages)
        {
            foreach (var visit in new[] { "", "X-Inertia: true\r\nX-Inertia-Version: 1\r\n" })
            {
                var request = Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: probe\r\nAccept: text/html, application/json\r\n{visit}\r\n");
                while (await connection.SendAsync(request) != 200)
                {
                    if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(30)) throw new InvalidOperationException($"{path} did not answer 200 within 30 s.");
                    await Task.Delay(100);
                }

                if (ssr && visit.Length == 0 && !ElementCounts.ContainsKey(path))
                {
                    var html = Encoding.UTF8.GetString(connection.LastBody!);
                    if (!html.Contains("data-server-rendered", StringComparison.Ordinal)) throw new InvalidOperationException($"{path} was not server-rendered.");
                    var count = CountElements(html);
                    ElementCounts[path] = count;
                    Console.WriteLine(FormattableString.Invariant($"{path}: {count:N0} server-rendered elements in <body>"));
                }
            }
        }
    }

    // Opening tags inside <body>, without <script> tags: the DOM the browser builds from the HTML.
    private static int CountElements(string html)
    {
        var body = html.AsSpan()[(html.IndexOf('>', html.IndexOf("<body", StringComparison.Ordinal)) + 1)..html.IndexOf("</body>", StringComparison.Ordinal)];
        var count = 0;
        for (var i = 0; i + 1 < body.Length; i++)
        {
            if (body[i] == '<' && char.IsAsciiLetter(body[i + 1]) && !body[(i + 1)..].StartsWith("script", StringComparison.Ordinal)) count++;
        }

        return count;
    }

    private static async Task<ProbeMetrics> MetricsAsync(IPEndPoint endpoint)
    {
        await using var connection = await Connection.OpenAsync(endpoint);
        connection.CaptureBody = true;
        if (await connection.SendAsync("GET /__metrics HTTP/1.1\r\nHost: probe\r\n\r\n"u8.ToArray()) != 200) throw new InvalidOperationException("/__metrics did not answer 200.");
        return JsonSerializer.Deserialize(connection.LastBody, ProbeJsonContext.Default.ProbeMetrics)!;
    }

    /// <summary>
    /// Puts <paramref name="connections"/> keep-alive connections under load for <paramref name="warmup"/> + <paramref name="seconds"/> seconds and
    /// measures the last <paramref name="seconds"/>. With <paramref name="rate"/> 0 each connection sends its next request as soon as a response
    /// arrives (saturation). Otherwise the connections together send <paramref name="rate"/> requests per second on a schedule, and a request
    /// sent late because the previous response had not arrived counts its latency from when it was due.
    /// </summary>
    private static async Task<Load> LoadAsync(IPEndPoint endpoint, byte[] request, int connections, int rate, int warmup, int seconds, Action? measureStart, Action? measureEnd)
    {
        var measuring = false;
        var stop = false;
        var histograms = new long[connections][];
        var counts = new long[connections];
        var errors = new long[connections];
        var interval = rate > 0 ? Stopwatch.Frequency * connections / rate : 0;
        var origin = Stopwatch.GetTimestamp();
        var workers = Enumerable.Range(0, connections).Select(c => Task.Run(async () =>
        {
            var histogram = histograms[c] = new long[Buckets];
            await using var connection = await Connection.OpenAsync(endpoint);
            var due = origin + interval * c / connections; // spread the connections' schedules across one interval
            while (!Volatile.Read(ref stop))
            {
                long begin;
                if (interval > 0)
                {
                    var now = Stopwatch.GetTimestamp();
                    if (due > now)
                    {
                        await Task.Delay(Stopwatch.GetElapsedTime(now, due));
                        // On time: the response before this one came back before it was due, so any lateness here is timer slack, not the server.
                        begin = Stopwatch.GetTimestamp();
                    }
                    else
                    {
                        begin = due;
                    }

                    due += interval;
                }
                else
                {
                    begin = Stopwatch.GetTimestamp();
                }

                var record = Volatile.Read(ref measuring);
                var status = await connection.SendAsync(request);
                if (!record || Volatile.Read(ref stop)) continue;
                if (status != 200) { errors[c]++; continue; }
                histogram[Math.Min(Bucket(Stopwatch.GetElapsedTime(begin).TotalMicroseconds), Buckets - 1)]++;
                counts[c]++;
            }
        })).ToArray();

        await Task.Delay(TimeSpan.FromSeconds(warmup));
        measureStart?.Invoke();
        var measured = Stopwatch.GetTimestamp();
        Volatile.Write(ref measuring, true);
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var elapsed = Stopwatch.GetElapsedTime(measured).TotalSeconds;
        measureEnd?.Invoke();
        Volatile.Write(ref stop, true);
        await Task.WhenAll(workers);

        var merged = new long[Buckets];
        foreach (var histogram in histograms)
        {
            for (var i = 0; i < merged.Length; i++) merged[i] += histogram[i];
        }

        var total = counts.Sum();
        return new Load(total / elapsed, Percentile(merged, total, 0.50) / 1000.0, Percentile(merged, total, 0.95) / 1000.0, Percentile(merged, total, 0.99) / 1000.0,
            Percentile(merged, total, 0.999) / 1000.0, errors.Sum(), elapsed);
    }

    // Latency buckets 2% wide on a log scale, up to 10 s: bucket i holds [1.02^i, 1.02^(i+1)) µs. About 800 buckets per connection,
    // with the same relative resolution at 50 µs and at 5 s.
    private const double BucketGrowth = 1.02;
    private static readonly int Buckets = Bucket(10_000_000) + 1;

    private static int Bucket(double micros) => micros < 1 ? 0 : (int)(Math.Log(micros) / Math.Log(BucketGrowth));

    // In microseconds: the geometric middle of the bucket that holds the quantile.
    private static double Percentile(long[] histogram, long total, double quantile)
    {
        var target = (long)Math.Ceiling(total * quantile);
        long seen = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            seen += histogram[i];
            if (seen >= target) return Math.Pow(BucketGrowth, i + 0.5);
        }

        return Math.Pow(BucketGrowth, histogram.Length - 0.5);
    }

    private static double RssMb(int pid)
    {
        foreach (var line in File.ReadLines($"/proc/{pid}/status"))
        {
            if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
            {
                return long.Parse(line.AsSpan(6).Trim().TrimEnd("kB").Trim(), CultureInfo.InvariantCulture) / 1024.0;
            }
        }

        throw new InvalidOperationException("VmRSS not found.");
    }

    // The server's child processes and theirs (the managed SSR process), by parent PID: /proc/<pid>/task/*/children is not on every kernel.
    private static List<int> Descendants(int root)
    {
        var parents = new Dictionary<int, List<int>>();
        var processes = Directory.EnumerateDirectories("/proc")
            .Select(dir => (Dir: dir, Pid: int.TryParse(Path.GetFileName(dir), CultureInfo.InvariantCulture, out var pid) ? pid : -1))
            .Where(p => p.Pid >= 0);
        foreach (var (dir, pid) in processes)
        {
            try
            {
                var stat = File.ReadAllText($"{dir}/stat");
                var ppid = int.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[1], CultureInfo.InvariantCulture);
                (parents.TryGetValue(ppid, out var children) ? children : parents[ppid] = []).Add(pid);
            }
            catch (IOException)
            {
                // exited while the list was read
            }
        }

        var found = new List<int>();
        var pending = new Queue<int>([root]);
        while (pending.TryDequeue(out var pid))
        {
            foreach (var child in parents.GetValueOrDefault(pid) ?? [])
            {
                found.Add(child);
                pending.Enqueue(child);
            }
        }

        return found;
    }

    // RSS of the server's descendants; one that exits (or is a zombie, without VmRSS) while they are read counts as 0.
    private static double ChildRssMb(int pid) => Descendants(pid).Sum(child => { try { return RssMb(child); } catch (Exception e) when (e is IOException or InvalidOperationException) { return 0; } });

    // CPU of the server and its descendants; a child that exits in the window takes its time with it.
    private static long TreeCpuTicks(int pid) => CpuTicks(pid) + Descendants(pid).Sum(child => { try { return CpuTicks(child); } catch (IOException) { return 0; } });

    // utime + stime in clock ticks (USER_HZ, 100 on Linux), from /proc/<pid>/stat after the parenthesized command name.
    private static long CpuTicks(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        return long.Parse(fields[11], CultureInfo.InvariantCulture) + long.Parse(fields[12], CultureInfo.InvariantCulture);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static double Median<T>(List<T> runs, Func<T, double> value)
    {
        var sorted = runs.Select(value).Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static int Option(string[] options, string name, int fallback)
    {
        var index = Array.IndexOf(options, name);
        return index >= 0 && index + 1 < options.Length ? int.Parse(options[index + 1], CultureInfo.InvariantCulture) : fallback;
    }

    /// <summary>One keep-alive HTTP/1.1 connection that sends a request and reads the whole response (Content-Length or chunked).</summary>
    private sealed class Connection : IAsyncDisposable
    {
        private readonly Socket _socket;
        private readonly byte[] _buffer = new byte[64 * 1024];
        private int _start;
        private int _end;

        private Connection(Socket socket) => _socket = socket;

        public long LastBodyBytes { get; private set; }

        public bool CaptureCookie { get; set; }

        public string? LastSetCookie { get; private set; }

        /// <summary>Whether <see cref="SendAsync"/> keeps the response body in <see cref="LastBody"/>.</summary>
        public bool CaptureBody { get; set; }

        public byte[]? LastBody { get; private set; }

        private readonly MemoryStream _body = new();

        public static async Task<Connection> OpenAsync(IPEndPoint endpoint)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(endpoint);
                return new Connection(socket);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public async ValueTask<int> SendAsync(byte[] request)
        {
            await _socket.SendAsync(request, SocketFlags.None);
            var headerEnd = await FindAsync(HeaderEnd);
            var (status, length, chunked) = ParseHeaders(_buffer.AsSpan(_start, headerEnd - _start));
            if (CaptureCookie)
            {
                var headers = Encoding.ASCII.GetString(_buffer, _start, headerEnd - _start);
                var at = headers.IndexOf("\r\nSet-Cookie: ", StringComparison.OrdinalIgnoreCase);
                LastSetCookie = at < 0 ? null : headers[(at + 14)..].Split(';')[0];
            }

            _start = headerEnd + 4;
            _body.SetLength(0);
            long body = 0;
            if (length >= 0)
            {
                body = length;
                await SkipAsync(body, CaptureBody);
            }
            else if (chunked)
            {
                while (true)
                {
                    var lineEnd = await FindAsync(LineEnd);
                    var size = ParseHex(_buffer.AsSpan(_start, lineEnd - _start));
                    _start = lineEnd + 2;
                    if (size == 0)
                    {
                        _start = await FindAsync(LineEnd) + 2;
                        break;
                    }

                    body += size;
                    await SkipAsync(size, CaptureBody);
                    await SkipAsync(2, false);
                }
            }

            LastBodyBytes = body;
            if (CaptureBody) LastBody = _body.ToArray();
            return status;
        }

        private static readonly byte[] HeaderEnd = "\r\n\r\n"u8.ToArray();
        private static readonly byte[] LineEnd = "\r\n"u8.ToArray();

        // Status code, Content-Length (-1 when absent) and chunked transfer encoding, read without allocating.
        private static (int Status, long Length, bool Chunked) ParseHeaders(ReadOnlySpan<byte> headers)
        {
            var status = (headers[9] - '0') * 100 + (headers[10] - '0') * 10 + (headers[11] - '0');
            long length = -1;
            var chunked = false;
            while (headers.IndexOf(LineEnd) is var next and >= 0)
            {
                headers = headers[(next + 2)..];
                var line = headers.IndexOf(LineEnd) is var end and >= 0 ? headers[..end] : headers;
                if (line.StartsWith("Content-Length:"u8) || line.StartsWith("content-length:"u8))
                {
                    length = long.Parse(line[15..].Trim((byte)' '), CultureInfo.InvariantCulture);
                }
                else if (line.StartsWith("Transfer-Encoding: chunked"u8) || line.StartsWith("transfer-encoding: chunked"u8))
                {
                    chunked = true;
                }
            }

            return (status, length, chunked);
        }

        private static int ParseHex(ReadOnlySpan<byte> line)
        {
            var value = 0;
            foreach (var b in line)
            {
                var digit = b is >= (byte)'0' and <= (byte)'9' ? b - '0' : b is >= (byte)'a' and <= (byte)'f' ? b - 'a' + 10 : b is >= (byte)'A' and <= (byte)'F' ? b - 'A' + 10 : -1;
                if (digit < 0) break;
                value = value * 16 + digit;
            }

            return value;
        }

        private async ValueTask<int> FindAsync(byte[] marker)
        {
            while (true)
            {
                var index = _buffer.AsSpan(_start, _end - _start).IndexOf(marker);
                if (index >= 0) return _start + index;
                await FillAsync();
            }
        }

        private async ValueTask SkipAsync(long count, bool capture)
        {
            while (count > 0)
            {
                if (_start == _end) await FillAsync();
                var take = (int)Math.Min(count, _end - _start);
                if (capture) _body.Write(_buffer, _start, take);
                _start += take;
                count -= take;
            }
        }

        private async ValueTask FillAsync()
        {
            if (_start == _end)
            {
                _start = _end = 0;
            }
            else if (_end == _buffer.Length)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }

            var read = await _socket.ReceiveAsync(_buffer.AsMemory(_end), SocketFlags.None);
            if (read == 0) throw new IOException("The server closed the connection.");
            _end += read;
        }

        public ValueTask DisposeAsync()
        {
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
