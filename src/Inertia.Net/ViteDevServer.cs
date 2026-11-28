using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Runs the package's <c>dev</c> script beside the app in Development when <see cref="ViteOptions.StartDevServer"/> is set.</summary>
internal sealed partial class ViteDevServer(IOptions<InertiaOptions> options, ViteAssets vite, IHostEnvironment environment, ILoggerFactory loggers)
    : IHostedService, IDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
    private readonly ILogger _logger = loggers.CreateLogger("Inertia.Net.Vite");
    private Process? _process;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Vite;
        if (!settings.StartDevServer || settings.DevServerUrl is not null || !environment.IsDevelopment() || InertiaTypeScriptWriter.IsGenerateOnly)
        {
            return;
        }

        var hotFile = vite.HotFile;
        if (File.Exists(hotFile))
        {
            if (await AnswersAsync(File.ReadAllText(hotFile).Trim(), cancellationToken))
            {
                LogAlreadyRunning(_logger);
                return;
            }

            File.Delete(hotFile); // left by a dev server that was killed before it could remove it
        }

        var root = environment.ContentRootPath;
        var manager = File.Exists(Path.Combine(root, "bun.lock")) || File.Exists(Path.Combine(root, "bun.lockb")) ? "bun"
            : File.Exists(Path.Combine(root, "pnpm-lock.yaml")) ? "pnpm"
            : File.Exists(Path.Combine(root, "yarn.lock")) ? "yarn" : "npm";
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", ["/c", manager, "run", "dev"])
            : new ProcessStartInfo(manager, ["run", "dev"]);
        start.WorkingDirectory = root;
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.Environment["FORCE_COLOR"] = "0";

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogOutput(_logger, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogError(_logger, e.Data); };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            LogStartFailed(_logger, ex, manager);
            return;
        }

        _process = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        LogStarted(_logger, $"{manager} run dev", root);

        // The first page needs the hot file; until it exists ViteAssets would look for a production build instead.
        var waited = Stopwatch.StartNew();
        while (!File.Exists(hotFile) && !process.HasExited && waited.Elapsed < ReadyTimeout)
        {
            await Task.Delay(100, cancellationToken);
        }

        if (process.HasExited) LogExited(_logger, process.ExitCode);
        else if (!File.Exists(hotFile)) LogNotReady(_logger, ReadyTimeout.TotalSeconds, hotFile);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        if (Interlocked.Exchange(ref _process, null) is not { } process) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        finally
        {
            process.Dispose();
            // A killed dev server cannot remove its hot file; a stale one would point the next run at nothing.
            try { File.Delete(vite.HotFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<bool> AnswersAsync(string origin, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            await client.ConnectAsync(uri.Host, uri.Port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    [LoggerMessage(LogLevel.Information, "The Vite dev server already runs; Vite.StartDevServer leaves it alone.")]
    private static partial void LogAlreadyRunning(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Started the Vite dev server ({Command}) in {Directory}.")]
    private static partial void LogStarted(ILogger logger, string command, string directory);

    [LoggerMessage(LogLevel.Warning, "Could not start the Vite dev server with {Manager}. Is Node installed and on the PATH?")]
    private static partial void LogStartFailed(ILogger logger, Exception exception, string manager);

    [LoggerMessage(LogLevel.Warning, "The Vite dev server exited with code {ExitCode}. Run the dev script yourself to see why (missing node_modules?).")]
    private static partial void LogExited(ILogger logger, int exitCode);

    [LoggerMessage(LogLevel.Warning, "The Vite dev server did not write {HotFile} within {Seconds} seconds; pages use the production build until it does.")]
    private static partial void LogNotReady(ILogger logger, double seconds, string hotFile);

    [LoggerMessage(LogLevel.Information, "vite: {Line}")]
    private static partial void LogOutput(ILogger logger, string line);

    [LoggerMessage(LogLevel.Warning, "vite: {Line}")]
    private static partial void LogError(ILogger logger, string line);
}
