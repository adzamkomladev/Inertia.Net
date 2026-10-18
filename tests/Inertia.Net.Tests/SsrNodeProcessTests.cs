using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
          if (req.url === '/render') {
            const page = JSON.parse(body)
            return res.end(JSON.stringify({ head: ['<title>fixture</title>'], body: `<div data-server-rendered="true" id="app">${page.component}</div>` }))
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

    private static (Harness Harness, SsrNodeProcess Process, string Url) Create(bool writeBundle = true, Action<InertiaOptions>? configure = null, Action<IServiceCollection>? configureServices = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "inertia-ssr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ssr"));
        File.Copy(Path.Combine(Harness.DefaultContentRoot, "app.html"), Path.Combine(root, "app.html"));
        if (writeBundle)
        {
            File.WriteAllText(Path.Combine(root, "ssr", "ssr.mjs"), Fixture);
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        listener.Stop();

        var harness = new Harness(
            o =>
            {
                o.Ssr.Url = url;
                o.Ssr.UseNodeProcess(n => n.StartupTimeout = TimeSpan.FromSeconds(15));
                configure?.Invoke(o);
            },
            contentRoot: root,
            configureServices: configureServices);
        return (harness, harness.Services.GetServices<IHostedService>().OfType<SsrNodeProcess>().Single(), url);
    }

    private static Task<string> RenderAsync(Harness harness) => Harness.ExecuteAsync(harness.Context(), Render("Home"));

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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var started = Stopwatch.GetTimestamp();
        while (!await condition())
        {
            Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(15), "Timed out.");
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
    [InlineData("0.0.0.0", true)]
    [InlineData("127.0.0.1", false)]
    public async Task Warns_when_the_ssr_server_is_reachable_beyond_loopback(string host, bool exposed)
    {
        Assert.SkipUnless(NodeAvailable, "node is not on PATH");
        var nonLoopback = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address));
        Assert.SkipUnless(nonLoopback, "no non-loopback IPv4 interface");
        var (harness, ssr, _) = Create(configure: o => o.Ssr.NodeProcess!.Environment["FIXTURE_HOST"] = host);
        try
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            await WaitUntilAsync(() => Task.FromResult(harness.Logs.Entries.Any(e => e.Message.StartsWith("The SSR server at", StringComparison.Ordinal))));
            await Task.Delay(exposed ? 0 : 3000, TestContext.Current.CancellationToken); // the negative case: give the probe time to finish
            if (exposed)
            {
                await WaitUntilAsync(() => Task.FromResult(harness.Logs.Entries.Any(e => e is { Level: LogLevel.Warning } && e.Message.Contains("also accepts connections", StringComparison.Ordinal))));
            }
            else
            {
                Assert.DoesNotContain(harness.Logs.Entries, e => e.Message.Contains("also accepts connections", StringComparison.Ordinal));
            }
        }
        finally
        {
            await ssr.StopAsync(CancellationToken.None);
        }
    }

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
    public async Task Missing_bundle_throws_with_ThrowOnError()
    {
        var (_, ssr, _) = Create(writeBundle: false, o => o.Ssr.ThrowOnError = true);
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

        var plain = new Harness(o => o.Ssr.Enabled = true).Services.GetServices<IHostedService>().OfType<SsrNodeProcess>().Single();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Managed_render_does_not_follow_a_changed_url_to_another_server(bool throwOnError)
    {
        Assert.SkipUnless(NodeAvailable, "node is not on PATH");
        var (harness, ssr, _) = Create(configure: o => o.Ssr.ThrowOnError = throwOnError);
        using var other = new TcpListener(IPAddress.Loopback, 0);
        other.Start();
        using (ssr)
        {
            await ssr.StartAsync(TestContext.Current.CancellationToken);
            var pid = Assert.NotNull(ssr.ProcessId);
            harness.Services.GetRequiredService<IOptionsMonitor<InertiaOptions>>().CurrentValue.Ssr.Url =
                $"http://127.0.0.1:{((IPEndPoint)other.LocalEndpoint).Port}";
            if (throwOnError)
            {
                var error = await Assert.ThrowsAsync<InertiaSsrException>(() => RenderAsync(harness));
                Assert.Equal("startup", error.Type);
            }
            else Assert.DoesNotContain("data-server-rendered", await RenderAsync(harness), StringComparison.Ordinal);
            Assert.False(other.Pending());
            Assert.Equal(pid, ssr.ProcessId);
            Assert.True(ssr.IsReady);
        }
    }

    [Fact]
    public async Task Managed_ssr_rejects_external_urls_and_can_throw_on_an_occupied_endpoint()
    {
        var (_, remote, _) = Create(configure: o => { o.Ssr.Url = "https://ssr.example.com"; o.Ssr.ThrowOnError = true; });
        using (remote)
        {
            var error = await Assert.ThrowsAsync<InertiaSsrException>(() => remote.StartAsync(TestContext.Current.CancellationToken));
            Assert.Equal("startup", error.Type);
            Assert.Null(remote.ProcessId);
        }
        var (_, occupied, url) = Create(configure: o => o.Ssr.ThrowOnError = true);
        using var listener = new TcpListener(IPAddress.Loopback, new Uri(url).Port);
        listener.Start();
        using (occupied)
            await Assert.ThrowsAsync<InertiaSsrException>(() => occupied.StartAsync(TestContext.Current.CancellationToken));
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
