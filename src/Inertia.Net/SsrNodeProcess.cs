using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
    private readonly object _gate = new();
    private Task _supervisor = Task.CompletedTask;
    private Task? _stopTask;
    private Process? _process;
    private Uri? _managedUrl;
    private string? _managedUrlText;
    private bool _ready;
    private bool _stopRequested;
    private bool _disposed;

    /// <summary>The id of the running Node process; null when none runs.</summary>
    internal int? ProcessId { get { lock (_gate) return _process is { HasExited: false } process ? process.Id : null; } }

    internal bool IsReady { get { lock (_gate) return !_disposed && !_stopRequested && _ready && _process is { HasExited: false }; } }

    // Called on every server-rendered page: the configured URL string is compared first, so the Uri is only parsed after a change.
    internal bool IsReadyFor(string url)
    {
        lock (_gate)
        {
            return IsReady && (string.Equals(url, _managedUrlText, StringComparison.Ordinal)
                || (Uri.TryCreate(url, UriKind.Absolute, out var endpoint) && endpoint.Equals(_managedUrl)));
        }
    }

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

        if (!Uri.TryCreate(ssr.Url, UriKind.Absolute, out var url) || !url.IsLoopback || url.Scheme is not ("http" or "https"))
        {
            StartupFailure(ssr, "Managed SSR requires an HTTP(S) loopback URL. Use unmanaged SSR for an external server.");
            return;
        }
        if (node.StartupTimeout <= TimeSpan.Zero)
        {
            StartupFailure(ssr, "The managed SSR startup timeout must be positive.");
            return;
        }
        lock (_gate)
        {
            if (_disposed || _stopRequested || _process is { HasExited: false }) return;
        }
        if (IsOccupied(url, cancellationToken))
        {
            StartupFailure(ssr, $"The managed SSR endpoint {url} is already occupied; no process was started.");
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
        startInfo.Environment["INERTIA_SSR_PORT"] = url.Port.ToString(CultureInfo.InvariantCulture);

        foreach (var (name, value) in node.Environment)
        {
            startInfo.Environment[name] = value;
        }

        lock (_gate)
        {
            if (_disposed || _stopRequested) return;
            if (!TryStart(startInfo))
            {
                Fail(ssr, $"Could not start '{node.Executable}'.");
                return;
            }
            _managedUrl = url;
            _managedUrlText = ssr.Url;
            _supervisor = SuperviseAsync(startInfo, url, node.RestartOnExit, _stopping.Token);
        }
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < node.StartupTimeout)
        {
            if (IsReady)
            {
                return;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        LogStartupTimeout(_logger, ssr.Url, node.StartupTimeout);
        Fail(ssr, $"The SSR server did not become healthy within {node.StartupTimeout}.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task stop;
        lock (_gate)
        {
            if (_disposed) return;
            stop = BeginStop();
        }
        await stop.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Called under the lifecycle gate; the worker never captures a caller's synchronization context.
    private Task BeginStop()
    {
        _stopRequested = true;
        _ready = false;
        return _stopTask ??= Task.Run(StopCoreAsync);
    }

    private async Task StopCoreAsync()
    {
        LogStopping(_logger);
        Process? process;
        try
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
            await _supervisor.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                process = _process;
                Kill();
            }
            if (process is not null)
            {
                await process.WaitForExitAsync().WaitAsync(ShutdownGrace).ConfigureAwait(false);
                LogStopped(_logger);
            }
        }
    }

    /// <summary>Kills the process tree if it still runs (e.g. the host was disposed without being stopped).</summary>
    public void Dispose()
    {
        Task stop;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            stop = BeginStop();
        }
        try
        {
            stop.GetAwaiter().GetResult();
        }
        finally
        {
            lock (_gate)
            {
                _process?.Dispose();
                _process = null;
                _stopping.Dispose();
            }
        }
    }

    private async Task SuperviseAsync(ProcessStartInfo startInfo, Uri url, bool restart, CancellationToken stopping)
    {
        var delay = MinRestartDelay;
        try
        {
            while (true)
            {
                var started = Stopwatch.GetTimestamp();
                Process? process;
                lock (_gate) process = _process;
                if (process is not null)
                {
                    while (!process.HasExited && !IsReady)
                    {
                        stopping.ThrowIfCancellationRequested();
                        if (await PingAsync(url, TimeSpan.FromSeconds(1), stopping).ConfigureAwait(false))
                        {
                            lock (_gate)
                            {
                                if (!_disposed && !_stopRequested && !process.HasExited && ReferenceEquals(process, _process)) _ready = true;
                            }
                            if (IsReady)
                            {
                                LogHealthy(_logger, url.ToString());
                                if (await FindExposedAddressAsync(url.Port, stopping).ConfigureAwait(false) is { } exposed)
                                {
                                    LogExposed(_logger, exposed.ToString(), url.Port);
                                }
                            }
                        }
                        if (!IsReady) await Task.Delay(100, stopping).ConfigureAwait(false);
                    }
                    await process.WaitForExitAsync(stopping).ConfigureAwait(false);
                    lock (_gate) _ready = false;
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
                await Task.Delay(delay, stopping).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRestartDelay.Ticks));
                if (IsOccupied(url, stopping))
                {
                    LogStartupFailure(_logger, $"The managed SSR endpoint {url} is occupied; restart was skipped.");
                    continue;
                }
                lock (_gate)
                {
                    if (_disposed || _stopRequested) return;
                    TryStart(startInfo);
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
    }

    private bool TryStart(ProcessStartInfo startInfo)
    {
        if (_disposed || _stopRequested) return false;
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
        _ready = false;
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

    private async Task<bool> PingAsync(Uri url, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            using var response = await httpClients.CreateClient(SsrGateway.HttpClientName).GetAsync($"{url.ToString().TrimEnd('/')}/health", HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or OperationCanceledException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    // The v3 SSR server binds the host baked into the bundle (upstream default 0.0.0.0) and serves unauthenticated /render and
    // /shutdown. Once healthy, a connection attempt to each local non-loopback address tells whether it is reachable from the network.
    internal static async Task<IPAddress?> FindExposedAddressAsync(int port, CancellationToken cancellationToken)
    {
        IEnumerable<IPAddress> addresses;
        try
        {
            addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => a.Address))
                .Where(a => !IPAddress.IsLoopback(a) && !a.IsIPv6LinkLocal && (a.AddressFamily == AddressFamily.InterNetwork || Socket.OSSupportsIPv6))
                .Take(8)
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return null;
        }

        foreach (var address in addresses)
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
            try
            {
                await socket.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
                return address;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // Refused or timed out: the server is not reachable on this address; try the next one.
                continue;
            }
        }

        return null;
    }

    private static bool IsOccupied(Uri url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPAddress[] addresses = IPAddress.TryParse(url.DnsSafeHost, out var address)
            ? [address] : [IPAddress.Loopback, IPAddress.IPv6Loopback];
        foreach (var local in addresses)
        {
            if (local.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6) continue;
            using var socket = new Socket(local.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
            try { socket.Bind(new IPEndPoint(local, url.Port)); }
            catch (SocketException) { return true; }
        }
        return false;
    }

    private void StartupFailure(SsrOptions ssr, string message)
    {
        LogStartupFailure(_logger, message);
        Fail(ssr, message);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SSR server also accepts connections on {Address}:{Port}. It serves unauthenticated /render and /shutdown endpoints; bind it to loopback with inertia({{ ssr: {{ host: '127.0.0.1' }} }}) in the Vite config.")]
    private static partial void LogExposed(ILogger logger, string address, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SSR server at {Url} did not become healthy within {Timeout}; pages render on the client until it does.")]
    private static partial void LogStartupTimeout(ILogger logger, string url, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Managed SSR startup failed: {Reason}")]
    private static partial void LogStartupFailure(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping the owned Node SSR process.")]
    private static partial void LogStopping(ILogger logger);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped the owned Node SSR process tree {ProcessId}.")]
    private static partial void LogKilled(ILogger logger, int processId);
}
