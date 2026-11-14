using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Inertia.Net.HostingProbe;

/// <summary>
/// Starts each server build in its own process and measures it from this one: RSS at rest, under load and after, requests per second,
/// latency percentiles, CPU used and startup time. Linux only (reads /proc).
/// </summary>
internal static class Bench
{
    private sealed record Scenario(string Name, string Mode, string Path, bool InertiaVisit);

    private sealed record Build(string Name, bool Aot, bool ServerGc);

    private sealed record Result(double Rps, double P50Ms, double P99Ms, double RestMb, double PeakMb, double AfterMb, double Cores, double StartupMs, long Bytes, long Errors)
    {
        // Server CPU time per request: holds up better than req/s when other work on the machine takes CPU from the client or server.
        public double CpuMicros => Cores * 1_000_000 / Rps;
    }

    private static readonly Scenario[] Scenarios =
    [
        new("Plain JSON API", "plain", "/api/users", false),
        new("Plain HTML shell", "plain", "/users", false),
        new("Inertia visit (JSON)", "inertia", "/users", true),
        new("Inertia first load (HTML)", "inertia", "/users", false),
    ];

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
        var connections = Option(options, "--connections", 64);
        var repeat = Option(options, "--repeat", 3);
        var cpus = options.SkipWhile(o => o != "--server-cpus").Skip(1).FirstOrDefault() ?? "0-3";
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"{connections} keep-alive connections, {warmup} s warmup, {seconds} s measured, {repeat} rounds; each round starts a fresh server process for every row, in table order. Server on CPUs {cpus}. Medians across rounds.");
        report.AppendLine();
        report.AppendLine("| Build | Scenario | Response | Req/s | p50 ms | p99 ms | Server CPU per request | RSS at rest | Peak RSS under load | RSS after load | Startup |");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        // Rounds outermost, so a slow patch on a shared machine hits every row of a round rather than all rounds of one row.
        var results = new Dictionary<(Build, Scenario), List<Result>>();
        for (var i = 0; i < repeat; i++)
        {
            foreach (var build in Builds)
            {
                foreach (var scenario in Scenarios)
                {
                    var client = Process.GetCurrentProcess().TotalProcessorTime;
                    var result = await RunOnceAsync(build, scenario, build.Aot ? aotBinary : jitDll, cpus, warmup, seconds, connections);
                    var clientCores = (Process.GetCurrentProcess().TotalProcessorTime - client).TotalSeconds / (warmup + seconds);
                    Console.WriteLine(FormattableString.Invariant(
                        $"{build.Name} | {scenario.Name} | round {i + 1}: {result.Rps:F0} req/s, p50 {result.P50Ms:F2} ms, p99 {result.P99Ms:F2} ms, rest {result.RestMb:F1} MB, peak {result.PeakMb:F1} MB, after {result.AfterMb:F1} MB, server {result.Cores:F2} cores ({result.CpuMicros:F0} µs/request), client ~{clientCores:F2} cores, startup {result.StartupMs:F0} ms, errors {result.Errors}"));
                    (results.TryGetValue((build, scenario), out var list) ? list : results[(build, scenario)] = []).Add(result);
                }
            }
        }

        foreach (var build in Builds)
        {
            foreach (var scenario in Scenarios)
            {
                var runs = results[(build, scenario)];
                report.AppendLine(FormattableString.Invariant(
                    $"| {build.Name} | {scenario.Name} | {runs[0].Bytes:N0} B | {Median(runs, r => r.Rps):N0} | {Median(runs, r => r.P50Ms):F2} | {Median(runs, r => r.P99Ms):F2} | {Median(runs, r => r.CpuMicros):F0} µs | {Median(runs, r => r.RestMb):F0} MB | {Median(runs, r => r.PeakMb):F0} MB | {Median(runs, r => r.AfterMb):F0} MB | {Median(runs, r => r.StartupMs):F0} ms |"));
            }
        }

        await File.WriteAllTextAsync(output, report.ToString());
        Console.WriteLine();
        Console.WriteLine(report);
    }

    private static async Task<Result> RunOnceAsync(Build build, Scenario scenario, string program, string cpus, int warmup, int seconds, int connections)
    {
        var port = FreePort();
        var start = new ProcessStartInfo("taskset") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in build.Aot ? ["-c", cpus, program] : new[] { "-c", cpus, "dotnet", program })
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["PROBE_MODE"] = scenario.Mode;
        start.Environment["DOTNET_gcServer"] = build.ServerGc ? "1" : "0";
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["DOTNET_TieredPGO"] = null; // the runtime default
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
            var request = Encoding.ASCII.GetBytes(
                $"GET {scenario.Path} HTTP/1.1\r\nHost: probe\r\nAccept: text/html, application/json\r\n" +
                (scenario.InertiaVisit ? "X-Inertia: true\r\nX-Inertia-Version: 1\r\n" : "") + "\r\n");
            long bytes;
            await using (var first = await Connection.OpenAsync(endpoint))
            {
                if (await first.SendAsync(request) != 200) throw new InvalidOperationException($"{scenario.Name} did not answer 200.");
                bytes = first.LastBodyBytes;
            }

            await Task.Delay(2000);
            var restMb = RssMb(server.Id);

            var measuring = false;
            var stop = false;
            var histograms = new long[connections][];
            var counts = new long[connections];
            var errors = new long[connections];
            var peakMb = restMb;
            var sampler = Task.Run(async () =>
            {
                while (!Volatile.Read(ref stop))
                {
                    peakMb = Math.Max(peakMb, RssMb(server.Id));
                    await Task.Delay(100);
                }
            });
            var workers = Enumerable.Range(0, connections).Select(c => Task.Run(async () =>
            {
                var histogram = histograms[c] = new long[MaxMicros + 1];
                await using var connection = await Connection.OpenAsync(endpoint);
                while (!Volatile.Read(ref stop))
                {
                    var record = Volatile.Read(ref measuring);
                    var begin = Stopwatch.GetTimestamp();
                    var status = await connection.SendAsync(request);
                    if (!record || Volatile.Read(ref stop)) continue;
                    if (status != 200) { errors[c]++; continue; }
                    var micros = (long)Stopwatch.GetElapsedTime(begin).TotalMicroseconds;
                    histogram[Math.Min(micros, MaxMicros)]++;
                    counts[c]++;
                }
            })).ToArray();

            await Task.Delay(TimeSpan.FromSeconds(warmup));
            var cpuBefore = CpuTicks(server.Id);
            var measured = Stopwatch.GetTimestamp();
            Volatile.Write(ref measuring, true);
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            var elapsed = Stopwatch.GetElapsedTime(measured).TotalSeconds;
            var cpuAfter = CpuTicks(server.Id);
            Volatile.Write(ref stop, true);
            await Task.WhenAll(workers);
            await sampler;
            await Task.Delay(3000);
            var afterMb = RssMb(server.Id);

            var merged = new long[MaxMicros + 1];
            foreach (var histogram in histograms)
            {
                for (var i = 0; i < merged.Length; i++) merged[i] += histogram[i];
            }

            var total = counts.Sum();
            return new Result(total / elapsed, Percentile(merged, total, 0.50) / 1000.0, Percentile(merged, total, 0.99) / 1000.0, restMb, peakMb, afterMb,
                (cpuAfter - cpuBefore) / 100.0 / elapsed, startupMs, bytes, errors.Sum());
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    private const int MaxMicros = 1_000_000;

    private static long Percentile(long[] histogram, long total, double quantile)
    {
        var target = (long)Math.Ceiling(total * quantile);
        long seen = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            seen += histogram[i];
            if (seen >= target) return i;
        }

        return histogram.Length - 1;
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

    private static double Median(List<Result> runs, Func<Result, double> value)
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
            _start = headerEnd + 4;
            long body = 0;
            if (length >= 0)
            {
                body = length;
                await SkipAsync(body);
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
                    await SkipAsync(size + 2);
                }
            }

            LastBodyBytes = body;
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

        private async ValueTask SkipAsync(long count)
        {
            while (count > 0)
            {
                if (_start == _end) await FillAsync();
                var take = (int)Math.Min(count, _end - _start);
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
