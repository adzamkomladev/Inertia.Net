using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>
/// Starts, supervises and stops <c>node {bundle}</c> when <see cref="SsrOptions.UseNodeProcess"/> is set.
/// The v3 SSR server takes its port from the build (<c>inertia({ ssr: { port } })</c>, default 13714), not from the command line;
/// the process gets <c>INERTIA_SSR_PORT</c> (the port of <see cref="SsrOptions.Url"/>) for entries that read it.
/// </summary>
internal sealed partial class SsrNodeProcess(
    IOptions<InertiaOptions> options,
    ViteAssets vite,
    IHttpClientFactory httpClients,
    IHostEnvironment environment,
    ILoggerFactory loggers) : IHostedService, IDisposable
{
    private static readonly string[] BundleCandidates = ["ssr/ssr.js", "ssr/ssr.mjs", "bootstrap/ssr/ssr.js", "bootstrap/ssr/ssr.mjs", "dist/ssr.js", "dist/ssr.mjs"];
    private static readonly TimeSpan MinRestartDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger = loggers.CreateLogger("Inertia.Net.Ssr");
    private readonly CancellationTokenSource _stopping = new();
    private Task _supervisor = Task.CompletedTask;
    private Process? _process;
    private volatile bool _running;

    /// <summary>The id of the running Node process; null when none runs.</summary>
    internal int? ProcessId { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var ssr = options.Value.Ssr;
        if (ssr is not { Enabled: true, NodeProcess: { } node })
        {
            return;
        }

        if (vite.IsDevServerRunning)
        {
            LogDevServer(_logger);
            return;
        }

        var configured = node.BundlePath ?? ssr.BundlePath;
        var bundle = (configured is { Length: > 0 } ? [configured] : BundleCandidates)
            .Select(path => Path.Combine(environment.ContentRootPath, path))
            .FirstOrDefault(File.Exists);
        if (bundle is null)
        {
            LogBundleMissing(_logger, configured ?? string.Join(", ", BundleCandidates), environment.ContentRootPath);
            Fail(ssr, "The SSR bundle was not found.");
            return;
        }

        var startInfo = new ProcessStartInfo(node.Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.Combine(environment.ContentRootPath, node.WorkingDirectory ?? ""),
        };
        foreach (var argument in node.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(bundle);
        startInfo.Environment.TryAdd("NODE_ENV", "production");
        if (Uri.TryCreate(ssr.Url, UriKind.Absolute, out var url))
        {
            startInfo.Environment["INERTIA_SSR_PORT"] = url.Port.ToString(CultureInfo.InvariantCulture);
        }

        foreach (var (name, value) in node.Environment)
        {
            startInfo.Environment[name] = value;
        }

        if (!TryStart(startInfo))
        {
            Fail(ssr, $"Could not start '{node.Executable}'.");
            return;
        }

        _supervisor = SuperviseAsync(startInfo, node.RestartOnExit, _stopping.Token);
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < node.StartupTimeout)
        {
            if (_running && await PingAsync("/health", TimeSpan.FromSeconds(1), cancellationToken))
            {
                LogHealthy(_logger, ssr.Url);
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        LogStartupTimeout(_logger, ssr.Url, node.StartupTimeout);
        Fail(ssr, $"The SSR server did not become healthy within {node.StartupTimeout}.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        await _supervisor;
        if (_process is not { HasExited: false } process)
        {
            return;
        }

        // The v3 server exits on any request to /shutdown (it never answers, so the request fails).
        await PingAsync("/shutdown", TimeSpan.FromSeconds(2), cancellationToken);
        using var grace = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        grace.CancelAfter(ShutdownGrace);
        try
        {
            await process.WaitForExitAsync(grace.Token);
            LogStopped(_logger);
        }
        catch (OperationCanceledException)
        {
            Kill();
        }
    }

    /// <summary>Kills the process tree if it still runs (e.g. the host was disposed without being stopped).</summary>
    public void Dispose()
    {
        _stopping.Cancel();
        Kill();
        _process?.Dispose();
        _stopping.Dispose();
    }

    private async Task SuperviseAsync(ProcessStartInfo startInfo, bool restart, CancellationToken stopping)
    {
        var delay = MinRestartDelay;
        try
        {
            while (true)
            {
                var started = Stopwatch.GetTimestamp();
                if (_process is { } process && _running)
                {
                    await process.WaitForExitAsync(stopping);
                    _running = false;
                    ProcessId = null;
                    LogExited(_logger, process.ExitCode);
                }

                if (!restart)
                {
                    return;
                }

                if (Stopwatch.GetElapsedTime(started) > MaxRestartDelay)
                {
                    delay = MinRestartDelay;
                }

                LogRestarting(_logger, delay);
                await Task.Delay(delay, stopping);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRestartDelay.Ticks));
                TryStart(startInfo);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
    }

    private bool TryStart(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                LogOutput(_logger, line);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                LogErrorOutput(_logger, line);
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            LogStartFailed(_logger, ex, startInfo.FileName);
            process.Dispose();
            return false;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process?.Dispose();
        _process = process;
        ProcessId = process.Id;
        _running = true;
        LogSpawned(_logger, process.Id, startInfo.FileName, string.Join(' ', startInfo.ArgumentList));
        return true;
    }

    private void Kill()
    {
        try
        {
            if (_process is { HasExited: false } process)
            {
                process.Kill(entireProcessTree: true);
                LogKilled(_logger, process.Id);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited, or never started.
        }
    }

    private async Task<bool> PingAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            using var response = await httpClients.CreateClient(SsrGateway.HttpClientName).GetAsync($"{options.Value.Ssr.Url.TrimEnd('/')}{path}", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static void Fail(SsrOptions ssr, string message)
    {
        if (ssr.ThrowOnError)
        {
            throw new InertiaSsrException($"Inertia SSR failed (startup): {message}", "startup");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The Vite dev server is running: SSR goes through it and no Node SSR process is started.")]
    private static partial void LogDevServer(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "SSR bundle not found ({Bundle}, under {ContentRoot}); no Node SSR process is started and pages render on the client. Build it with `vite build --ssr`.")]
    private static partial void LogBundleMissing(ILogger logger, string bundle, string contentRoot);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not start the Node SSR process '{Executable}'. Is Node installed and on PATH?")]
    private static partial void LogStartFailed(ILogger logger, Exception exception, string executable);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started the Node SSR process {ProcessId}: {Executable} {Arguments}")]
    private static partial void LogSpawned(ILogger logger, int processId, string executable, string arguments);

    [LoggerMessage(Level = LogLevel.Information, Message = "The SSR server at {Url} is healthy.")]
    private static partial void LogHealthy(ILogger logger, string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SSR server at {Url} did not become healthy within {Timeout}; pages render on the client until it does.")]
    private static partial void LogStartupTimeout(ILogger logger, string url, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Line}")]
    private static partial void LogOutput(ILogger logger, string line);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Line}")]
    private static partial void LogErrorOutput(ILogger logger, string line);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Node SSR process exited unexpectedly with code {ExitCode}.")]
    private static partial void LogExited(ILogger logger, int exitCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restarting the Node SSR process in {Delay}.")]
    private static partial void LogRestarting(ILogger logger, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Node SSR process stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Killed the Node SSR process {ProcessId} (it did not stop in time).")]
    private static partial void LogKilled(ILogger logger, int processId);
}
