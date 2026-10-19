using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

/// <summary>Runs a tiny JS SSR server fixture under real Node (skipped when <c>node</c> is not on PATH).</summary>
public sealed class SsrNodeProcessTests
{
    // Mirrors the v3 server routes; listens on INERTIA_SSR_PORT like an entry that reads it.
    private const string Fixture = """
        import http from 'node:http'
        console.log('fixture started')
        console.error('fixture stderr')
        http.createServer(async (req, res) => {
          let body = ''
          for await (const chunk of req) body += chunk
          if (req.url === '/shutdown') process.exit(0)
          if (req.url === '/crash') process.exit(3)
          res.writeHead(200, { 'Content-Type': 'application/json' })
          if (req.url === '/hang') {
            res.end('{}')
            const until = Date.now() + 60000
            while (Date.now() < until) {} // blocks the event loop: alive but unresponsive
          }
          if (req.url === '/render') {
            const page = JSON.parse(body)
            const port = `<meta name="port" content="${process.env.INERTIA_SSR_PORT}">`
            return res.end(JSON.stringify({ head: ['<title>fixture</title>', port], body: `<div data-server-rendered="true" id="app">${page.component}</div>` }))
          }
          res.end('{"status":"OK"}')
        }).listen(Number(process.env.INERTIA_SSR_PORT), process.env.FIXTURE_HOST ?? '127.0.0.1')
        """;

    private static readonly bool NodeAvailable = CheckNode();

    private static bool CheckNode()
    {
        try
        {
            using var node = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true });
            node!.WaitForExit();
            return node.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static (Harness Harness, SsrNodeProcess Process, string Url) Create(bool writeBundle = true, Action<InertiaOptions>? configure = null, Action<IServiceCollection>? configureServices = null, int instances = 1)
    {
        var root = Path.Combine(Path.GetTempPath(), "inertia-ssr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ssr"));
        File.Copy(Path.Combine(Harness.DefaultContentRoot, "app.html"), Path.Combine(root, "app.html"));
        if (writeBundle)
        {
            File.WriteAllText(Path.Combine(root, "ssr", "ssr.mjs"), Fixture);
        }

        var port = FreePorts(instances);
        var url = $"http://127.0.0.1:{port}";
        var harness = new Harness(
            o =>
            {
                o.Ssr.UseNodeProcess(n =>
                {
                    n.BasePort = port;
                    n.Instances = instances;
                    n.StartupTimeout = TimeSpan.FromSeconds(15);
                });
                configure?.Invoke(o);
            },
            contentRoot: root,
            configureServices: configureServices);
        return (harness, harness.Services.GetServices<IHostedService>().OfType<SsrNodeProcess>().Single(), url);
    }

    // The first of `count` consecutive free loopback ports.
    private static int FreePorts(int count)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (port + count - 1 <= IPEndPoint.MaxPort && Enumerable.Range(port + 1, count - 1).All(IsFree) || attempt == 20)
            {
                return port;
            }
        }

        static bool IsFree(int port)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }

    private static Task<string> RenderAsync(Harness harness) => Harness.ExecuteAsync(harness.Context(), Render("Home"));

    private static string? RenderedPort(string html)
    {
        const string marker = "<meta name=\"port\" content=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        return start < 0 ? null : html[(start + marker.Length)..html.IndexOf('"', start + marker.Length)];
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var started = Stopwatch.GetTimestamp();
        while (!await condition())
        {
            Assert.True(Stopwatch.GetElapsedTime(started) < (timeout ?? TimeSpan.FromSeconds(15)), "Timed out.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Starts_renders_logs_output_and_stops_without_leaving_the_process()
    {
        Assert.SkipUnless(NodeAvailable, "node is not on PATH");
        var (harness, ssr, _) = Create();
        int pid;
        try
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            pid = Assert.NotNull(ssr.ProcessId);

            var html = await RenderAsync(harness);
            Assert.Contains("<title>fixture</title>", html, StringComparison.Ordinal);
            Assert.Contains("<div data-server-rendered=\"true\" id=\"app\">Home</div>", html, StringComparison.Ordinal);
            await WaitUntilAsync(() => Task.FromResult(harness.Logs.Entries.Any(e => e is { Level: LogLevel.Warning, Message: "fixture stderr" })));
            Assert.Contains(harness.Logs.Entries, e => e is { Level: LogLevel.Information, Message: "fixture started" });
        }
        finally
        {
            await ssr.StopAsync(CancellationToken.None);
        }

        Assert.False(IsAlive(pid));
        Assert.Contains(harness.Logs.Entries, e => e.Message == "The Node SSR process stopped.");
    }

    [Theory]
    [InlineData("0.0.0.0", false)]
    [InlineData("0.0.0.0", true)]
    [InlineData("127.0.0.1", false)]
    public async Task Stops_an_ssr_server_reachable_beyond_loopback_unless_exposure_is_allowed(string host, bool allowExposure)
    {
        Assert.SkipUnless(NodeAvailable, "node is not on PATH");
        var nonLoopback = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address));
        Assert.SkipUnless(nonLoopback, "no non-loopback IPv4 interface");
        var exposed = host == "0.0.0.0";
        var (harness, ssr, _) = Create(configure: o =>
        {
            o.Ssr.NodeProcess!.Environment["FIXTURE_HOST"] = host;
            o.Ssr.NodeProcess.AllowNetworkExposure = allowExposure;
        });
        try
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken); // returns once the exposed instance was stopped
            var stopped = false;
            await WaitUntilAsync(() => Task.FromResult(harness.Logs.Entries.Any(e =>
                e.Message.StartsWith("The SSR server at", StringComparison.Ordinal)
                || (stopped = e is { Level: LogLevel.Error } && e.Message.StartsWith("Stopped the SSR server: it also accepts connections", StringComparison.Ordinal)))));
            await Task.Delay(1500, TestContext.Current.CancellationToken); // no restart follows a stop for exposure
            var warned = harness.Logs.Entries.Any(e => e is { Level: LogLevel.Warning } && e.Message.StartsWith("The SSR server also accepts connections", StringComparison.Ordinal));
            Assert.Equal(exposed && !allowExposure, stopped);
            Assert.Equal(exposed && allowExposure, warned);
            Assert.Equal(!stopped, ssr.IsReady);
            Assert.Equal(!stopped, IsServerRendered(await RenderAsync(harness)));
            Assert.Single(harness.Logs.Entries, e => e.Message.StartsWith("Started the Node SSR process", StringComparison.Ordinal));
        }
        finally
        {
            await ssr.StopAsync(CancellationToken.None);
        }
    }

    private static bool IsServerRendered(string html) => html.Contains("data-server-rendered", StringComparison.Ordinal);

    [Fact]
    public async Task Stops_the_owned_process_without_sending_a_shutdown_request()
    {
        Assert.SkipUnless(NodeAvailable, "node is not on PATH");
        var (_, ssr, _) = Create(configureServices: services => services.AddHttpClient(SsrGateway.HttpClientName)
            .AddHttpMessageHandler(() => new ShutdownSocketHandler()));
        using (ssr)
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            var pid = Assert.NotNull(ssr.ProcessId);
            await ssr.StopAsync(TestContext.Current.CancellationToken);
            Assert.False(IsAlive(pid));
        }
    }

    private sealed class ShutdownSocketHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/shutdown")
            {
                return await base.SendAsync(request, cancellationToken);
            }

            throw new InvalidOperationException("Shutdown must target the owned process, not a URL.");
        }
    }

    [Fact]
    public async Task Restarts_after_a_crash()
    {
        Assert.SkipUnless(NodeAvailable, "node is not on PATH");
        var (harness, ssr, url) = Create();
        try
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            var first = Assert.NotNull(ssr.ProcessId);
            using var http = new HttpClient();
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync($"{url}/crash", TestContext.Current.CancellationToken));

            await WaitUntilAsync(async () => ssr.ProcessId is { } pid && pid != first && (await RenderAsync(harness)).Contains("data-server-rendered", StringComparison.Ordinal));
            Assert.Contains(harness.Logs.Entries, e => e is { Level: LogLevel.Warning, Message: "The Node SSR process exited unexpectedly with code 3." });
            Assert.False(IsAlive(first));
        }
        finally
        {
            await ssr.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Repeated_stop_is_safe_even_after_disposal(bool writeBundle, bool disposeBetweenStops)
    {
        Assert.SkipUnless(!writeBundle || NodeAvailable, "node is not on PATH");
        var (_, ssr, _) = Create(writeBundle);
        using (ssr)
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            var pid = ssr.ProcessId;
            Assert.Equal(writeBundle, pid.HasValue);

            await ssr.StopAsync(CancellationToken.None);
            if (pid is { } processId)
            {
                Assert.False(IsAlive(processId));
            }

            if (disposeBetweenStops)
            {
                ssr.Dispose();
            }

            await ssr.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Missing_bundle_logs_an_error_and_falls_back_to_client_rendering()
    {
        var (harness, ssr, _) = Create(writeBundle: false);
        await ssr.StartAsync(TestContext.Current.CancellationToken);

        Assert.Null(ssr.ProcessId);
        Assert.Contains(harness.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.StartsWith("SSR bundle not found", StringComparison.Ordinal));
        Assert.DoesNotContain("data-server-rendered", await RenderAsync(harness), StringComparison.Ordinal);
        await ssr.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Missing_bundle_throws_when_ssr_is_required()
    {
        var (_, ssr, _) = Create(writeBundle: false, o => o.Ssr.Mode = SsrMode.Required);
        var ex = await Assert.ThrowsAsync<InertiaSsrException>(() => ssr.StartAsync(TestContext.Current.CancellationToken));
        Assert.Equal("startup", ex.Type);
    }

    [Fact]
    public async Task Does_not_spawn_while_the_vite_dev_server_runs_or_when_not_configured()
    {
        var (harness, ssr, _) = Create(configure: o => o.Vite.DevServerUrl = "http://localhost:5173");
        await ssr.StartAsync(TestContext.Current.CancellationToken);
        Assert.Null(ssr.ProcessId);
        Assert.Contains(harness.Logs.Entries, e => e.Message.StartsWith("The Vite dev server is running", StringComparison.Ordinal));

        var plain = new Harness(o => o.Ssr.Mode = SsrMode.Preferred).Services.GetServices<IHostedService>().OfType<SsrNodeProcess>().Single();
        await plain.StartAsync(TestContext.Current.CancellationToken);
        Assert.Null(plain.ProcessId);
    }

    [Fact]
    public async Task Occupied_endpoint_is_not_used_for_health_render_or_shutdown()
    {
        var (harness, ssr, url) = Create();
        using var listener = new TcpListener(IPAddress.Loopback, new Uri(url).Port);
        listener.Start();
        using (ssr)
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            Assert.False(listener.Pending());
            Assert.Null(ssr.ProcessId);
            Assert.False(ssr.IsReady);
            Assert.DoesNotContain("data-server-rendered", await RenderAsync(harness), StringComparison.Ordinal);
            await ssr.StopAsync(TestContext.Current.CancellationToken);
            Assert.False(listener.Pending());
        }
    }

    [Fact]
    public async Task Occupied_port_throws_on_startup_when_ssr_is_required()
    {
        var (_, occupied, url) = Create(configure: o => o.Ssr.Mode = SsrMode.Required);
        using var listener = new TcpListener(IPAddress.Loopback, new Uri(url).Port);
        listener.Start();
        using (occupied)
        {
            var error = await Assert.ThrowsAsync<InertiaSsrException>(() => occupied.StartAsync(TestContext.Current.CancellationToken));
            Assert.Equal("startup", error.Type);
            Assert.Null(occupied.ProcessId);
        }
    }

    [Fact]
    public async Task Kills_and_restarts_a_process_that_stops_answering()
    {
        Assert.SkipUnless(NodeAvailable, "node is not on PATH");
        var (harness, ssr, url) = Create(configure: o =>
        {
            o.Ssr.NodeProcess!.LivenessInterval = TimeSpan.FromMilliseconds(200);
            o.Ssr.NodeProcess.LivenessFailureThreshold = 2;
        });
        try
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            var first = Assert.NotNull(ssr.ProcessId);
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            try
            {
                (await http.GetAsync($"{url}/hang", TestContext.Current.CancellationToken)).Dispose();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // The answer may never arrive once the event loop is blocked; only the hang matters here.
            }

            await WaitUntilAsync(async () => ssr.ProcessId is { } pid && pid != first && IsServerRendered(await RenderAsync(harness)), TimeSpan.FromSeconds(30));
            Assert.Contains(harness.Logs.Entries, e => e.Message.Contains("failed 2 health checks in a row", StringComparison.Ordinal));
            Assert.False(IsAlive(first));
        }
        finally
        {
            await ssr.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Several_instances_share_the_renders_and_cover_for_each_other()
    {
        Assert.SkipUnless(NodeAvailable, "node is not on PATH");
        var (harness, ssr, url) = Create(instances: 2);
        var basePort = new Uri(url).Port;
        try
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(ssr.ProcessIdOf(0));
            Assert.NotNull(ssr.ProcessIdOf(1));
            var ports = new HashSet<string?>();
            for (var i = 0; i < 4; i++)
            {
                ports.Add(RenderedPort(await RenderAsync(harness)));
            }

            Assert.Equal(new[] { $"{basePort}", $"{basePort + 1}" }, ports.Order());

            var first = ssr.ProcessIdOf(0);
            using var http = new HttpClient();
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync($"{url}/crash", TestContext.Current.CancellationToken));
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal($"{basePort + 1}", RenderedPort(await RenderAsync(harness))); // a refused render fails over to the other instance
            }

            await WaitUntilAsync(() => Task.FromResult(ssr.ProcessIdOf(0) is { } pid && pid != first && ssr.IsInstanceReady(0)));
        }
        finally
        {
            await ssr.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Restarts_once_an_occupied_port_is_free_again()
    {
        Assert.SkipUnless(NodeAvailable, "node is not on PATH");
        TcpListener? squatter = null;
        var port = 0;
        var occupier = new LogHook(message =>
        {
            if (squatter is null && message.StartsWith("Restarting the Node", StringComparison.Ordinal))
            {
                squatter = new TcpListener(IPAddress.Loopback, port); // another program takes the port while the restart waits
                squatter.Start();
            }
        });
        var (harness, ssr, url) = Create(configureServices: s => s.AddLogging(b => b.AddProvider(occupier)));
        port = new Uri(url).Port;
        try
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            var first = Assert.NotNull(ssr.ProcessId);
            using var http = new HttpClient();
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync($"{url}/crash", TestContext.Current.CancellationToken));
            await WaitUntilAsync(() => Task.FromResult(harness.Logs.Entries.Any(e => e.Message.EndsWith("is occupied; restart was skipped.", StringComparison.Ordinal))));
            Assert.Null(ssr.ProcessId);
            squatter!.Dispose();

            await WaitUntilAsync(async () => ssr.ProcessId is { } pid && pid != first && IsServerRendered(await RenderAsync(harness)));
        }
        finally
        {
            squatter?.Dispose();
            await ssr.StopAsync(CancellationToken.None);
        }
    }

    private sealed class LogHook(Action<string> onMessage) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) => onMessage(formatter(state, error));
        public void Dispose() { }
    }

    [Fact]
    public async Task Disposal_during_a_queued_restart_joins_the_supervisor_and_prevents_a_new_child()
    {
        Assert.SkipUnless(NodeAvailable, "node is not on PATH");
        using var barrier = new RestartBarrier();
        var (_, ssr, url) = Create(configureServices: s => s.AddLogging(b => b.AddProvider(barrier)));
        Task? disposal = null;
        try
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            var pid = Assert.NotNull(ssr.ProcessId);
            using var client = new HttpClient();
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync($"{url}/crash", TestContext.Current.CancellationToken));
            Assert.True(barrier.RestartEntered.Wait(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
            disposal = Task.Run(ssr.Dispose, TestContext.Current.CancellationToken);
            Assert.True(barrier.StopEntered.Wait(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
            barrier.ReleaseRestart.Set();
            await disposal.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Null(ssr.ProcessId);
            Assert.False(ssr.IsReady);
            Assert.False(IsAlive(pid));
            Assert.Equal(1, barrier.Spawns);
        }
        finally
        {
            barrier.ReleaseRestart.Set();
            if (disposal is not null) await disposal;
            ssr.Dispose();
        }
    }

    private sealed class RestartBarrier : ILoggerProvider, ILogger
    {
        internal ManualResetEventSlim RestartEntered { get; } = new();
        internal ManualResetEventSlim StopEntered { get; } = new();
        internal ManualResetEventSlim ReleaseRestart { get; } = new();
        internal int Spawns;
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, error);
            if (message.StartsWith("Started the Node SSR process", StringComparison.Ordinal)) Interlocked.Increment(ref Spawns);
            if (message.StartsWith("Stopping the owned", StringComparison.Ordinal)) StopEntered.Set();
            if (message.StartsWith("Restarting the Node", StringComparison.Ordinal))
            {
                RestartEntered.Set();
                if (!ReleaseRestart.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Restart barrier was not released.");
            }
        }
        public void Dispose() { RestartEntered.Dispose(); StopEntered.Dispose(); ReleaseRestart.Dispose(); }
    }
}
