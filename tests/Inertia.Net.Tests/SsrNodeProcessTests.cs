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
          if (req.url === '/render') {
            const page = JSON.parse(body)
            return res.end(JSON.stringify({ head: ['<title>fixture</title>'], body: `<div data-server-rendered="true" id="app">${page.component}</div>` }))
          }
          res.end('{"status":"OK"}')
        }).listen(Number(process.env.INERTIA_SSR_PORT), '127.0.0.1')
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

    private static (Harness Harness, SsrNodeProcess Process, string Url) Create(bool writeBundle = true, Action<InertiaOptions>? configure = null)
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
            contentRoot: root);
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
}
