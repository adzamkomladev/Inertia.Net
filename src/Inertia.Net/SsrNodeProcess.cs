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
/// Starts, supervises and stops <c>node {bundle}</c> instances when <see cref="SsrOptions.UseNodeProcess"/> is set.
/// The v3 SSR server takes its port from the build (<c>inertia({ ssr: { port } })</c>, default 13714), not from the command line;
/// each instance gets <c>INERTIA_SSR_PORT</c> (its own port) for entries that read it, which several instances require.
/// </summary>
internal sealed partial class SsrNodeProcess(
    IOptions<InertiaOptions> options,
    ViteAssets vite,
    IHttpClientFactory httpClients,
    IHostEnvironment environment,
    InertiaMetrics metrics,
    ILoggerFactory loggers) : IHostedService, IDisposable
{
    private static readonly string[] BundleCandidates = ["ssr/ssr.js", "ssr/ssr.mjs", "bootstrap/ssr/ssr.js", "bootstrap/ssr/ssr.mjs", "dist/ssr.js", "dist/ssr.mjs"];
    private static readonly TimeSpan MinRestartDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LivenessTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger _logger = loggers.CreateLogger("Inertia.Net.Ssr");
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _gate = new();
    private Instance[] _instances = [];
    private Task? _stopTask;
    private bool _stopRequested;
    private bool _disposed;

    /// <summary>Raised with the instance index whenever an instance becomes healthy (after startup or a restart).</summary>
    internal event Action<int>? InstanceReady;

    /// <summary>The URL of the managed instance listening on <paramref name="port"/>.</summary>
    internal static string InstanceUrl(int port) => $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The id of the first instance's running Node process; null when none runs.</summary>
    internal int? ProcessId => ProcessIdOf(0);

    internal int? ProcessIdOf(int instance)
    {
        lock (_gate)
        {
            return instance < _instances.Length && _instances[instance].Process is { HasExited: false } process ? process.Id : null;
        }
    }

    /// <summary>Whether at least one instance is healthy.</summary>
    internal bool IsReady
    {
        get
        {
            lock (_gate)
            {
                return _instances.Any(IsReadyLocked);
            }
        }
    }

    // Called for every server-rendered page: one lock and a field read per candidate instance.
    internal bool IsInstanceReady(int instance)
    {
        lock (_gate)
        {
            return instance < _instances.Length && IsReadyLocked(_instances[instance]);
        }
    }

    private bool IsReadyLocked(Instance instance) => !_disposed && !_stopRequested && instance.Ready && instance.Process is { HasExited: false };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var ssr = options.Value.Ssr;
        if (ssr.Mode != SsrMode.Disabled && ssr.NodeProcess is null)
        {
            WarnAboutPlainHttpRemotes(ssr);
        }

        if (ssr is not { Mode: not SsrMode.Disabled, NodeProcess: { } node })
        {
            return;
        }

        if (vite.IsDevServerRunning)
        {
            LogDevServer(_logger);
            return;
        }

        if (node.StartupTimeout <= TimeSpan.Zero)
        {
            StartupFailure(ssr, "The managed SSR startup timeout must be positive.");
            return;
        }

        if (node.Instances < 1 || node.BasePort < 1 || node.BasePort + node.Instances - 1 > IPEndPoint.MaxPort)
        {
            StartupFailure(ssr, "NodeProcess.Instances must be at least 1 and every instance port (BasePort + index) a valid TCP port.");
            return;
        }

        lock (_gate)
        {
            if (_disposed || _stopRequested || _instances.Length > 0) return;
        }

        var ports = Enumerable.Range(node.BasePort, node.Instances).ToArray();
        if (ports.FirstOrDefault(port => IsOccupied(port, cancellationToken)) is var occupied and > 0)
        {
            StartupFailure(ssr, $"The managed SSR endpoint {InstanceUrl(occupied)} is already occupied; no process was started.");
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

        lock (_gate)
        {
            if (_disposed || _stopRequested) return;
            _instances = [.. ports.Select((port, index) => new Instance(index, port, CreateStartInfo(node, bundle, port)))];
            foreach (var instance in _instances)
            {
                if (!TryStart(instance))
                {
                    Fail(ssr, $"Could not start '{node.Executable}'.");
                    return;
                }
            }

            foreach (var instance in _instances)
            {
                instance.Supervisor = SuperviseAsync(instance, node, _stopping.Token);
            }
        }

        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < node.StartupTimeout)
        {
            var settled = true;
            for (var i = 0; i < ports.Length; i++)
            {
                if (!IsInstanceReady(i) && !_instances[i].Supervisor.IsCompleted) settled = false;
            }

            if (settled)
            {
                if (!IsReady) Fail(ssr, "No managed SSR process is running."); // e.g. stopped for network exposure
                return;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        var waiting = string.Join(", ", ports.Where((_, i) => !IsInstanceReady(i)).Select(InstanceUrl));
        if (ports.Length > 1)
        {
            LogInstancesNotHealthy(_logger, waiting, node.StartupTimeout);
        }
        else
        {
            LogStartupTimeout(_logger, waiting, node.StartupTimeout);
        }

        Fail(ssr, $"The SSR server did not become healthy within {node.StartupTimeout} at {waiting}.");
    }

    private ProcessStartInfo CreateStartInfo(SsrNodeProcessOptions node, string bundle, int port)
    {
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
        foreach (var (name, value) in node.Environment)
        {
            startInfo.Environment[name] = value;
        }

        startInfo.Environment["INERTIA_SSR_PORT"] = port.ToString(CultureInfo.InvariantCulture);
        return startInfo;
    }

    // Page props travel to the renderer in clear text over plain HTTP: fine on loopback or a private network, worth a warning otherwise.
    private void WarnAboutPlainHttpRemotes(SsrOptions ssr)
    {
        foreach (var url in ssr.Urls)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            {
                LogPlainHttpRemote(_logger, uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped));
            }
        }
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
        foreach (var instance in _instances)
        {
            instance.Ready = false;
        }

        return _stopTask ??= Task.Run(StopCoreAsync);
    }

    private async Task StopCoreAsync()
    {
        LogStopping(_logger);
        Instance[] instances;
        lock (_gate) instances = _instances;
        try
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(instances.Select(i => i.Supervisor)).ConfigureAwait(false);
        }
        finally
        {
            var exits = new List<Task>();
            lock (_gate)
            {
                foreach (var instance in instances)
                {
                    if (instance.Process is { } process)
                    {
                        Kill(process);
                        exits.Add(process.WaitForExitAsync().WaitAsync(ShutdownGrace));
                    }
                }
            }

            if (exits.Count > 0)
            {
                await Task.WhenAll(exits).ConfigureAwait(false);
                LogStopped(_logger);
            }
        }
    }

    /// <summary>Kills the process trees if they still run (e.g. the host was disposed without being stopped).</summary>
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
                foreach (var instance in _instances)
                {
                    instance.Process?.Dispose();
                    instance.Process = null;
                }

                _stopping.Dispose();
            }
        }
    }

    private async Task SuperviseAsync(Instance instance, SsrNodeProcessOptions node, CancellationToken stopping)
    {
        var delay = MinRestartDelay;
        Process? process;
        lock (_gate) process = instance.Process;
        try
        {
            while (true)
            {
                var started = Stopwatch.GetTimestamp();
                var cause = "exit";
                if (process is not null)
                {
                    cause = await RunAsync(instance, process, node, stopping).ConfigureAwait(false);
                    lock (_gate) instance.Ready = false;
                    if (cause == "exit") LogExited(_logger, process.ExitCode);
                    if (cause == "exposure") return; // restarting would expose it again
                }

                if (!node.RestartOnExit)
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
                if (IsOccupied(instance.Port, stopping))
                {
                    LogStartupFailure(_logger, $"The managed SSR endpoint {InstanceUrl(instance.Port)} is occupied; restart was skipped.");
                    process = null; // nothing of ours runs: back off again instead of waiting on the old exit
                    continue;
                }

                lock (_gate)
                {
                    if (_disposed || _stopRequested) return;
                    process = TryStart(instance) ? instance.Process : null;
                }

                if (process is not null)
                {
                    metrics.RecordRestart(cause);
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
    }

    // Waits for the instance to become healthy, then checks it every LivenessInterval until it exits or stops answering.
    // Returns why it ended: "exit", "liveness" (killed after repeated failed checks) or "exposure" (killed for answering beyond loopback).
    private async Task<string> RunAsync(Instance instance, Process process, SsrNodeProcessOptions node, CancellationToken stopping)
    {
        var url = InstanceUrl(instance.Port);
        while (!process.HasExited && !IsInstanceReady(instance.Index))
        {
            stopping.ThrowIfCancellationRequested();
            if (await PingAsync(url, TimeSpan.FromSeconds(1), stopping).ConfigureAwait(false))
            {
                // Checked before the instance takes renders: an exposed instance never serves a page.
                if (await FindExposedAddressAsync(instance.Port, stopping).ConfigureAwait(false) is { } exposed)
                {
                    if (!node.AllowNetworkExposure)
                    {
                        LogExposedStopped(_logger, exposed.ToString(), instance.Port);
                        lock (_gate) Kill(process);
                        await process.WaitForExitAsync(stopping).ConfigureAwait(false);
                        return "exposure";
                    }

                    LogExposed(_logger, exposed.ToString(), instance.Port);
                }

                lock (_gate)
                {
                    if (!_disposed && !_stopRequested && !process.HasExited && ReferenceEquals(process, instance.Process)) instance.Ready = true;
                }

                if (IsInstanceReady(instance.Index))
                {
                    LogHealthy(_logger, url);
                    InstanceReady?.Invoke(instance.Index);
                }
            }

            if (!IsInstanceReady(instance.Index)) await Task.Delay(100, stopping).ConfigureAwait(false);
        }

        // Alive is not healthy: a blocked event loop keeps the process running but answers nothing.
        var exited = process.WaitForExitAsync(stopping);
        var failures = 0;
        while (!process.HasExited)
        {
            var completed = await Task.WhenAny(exited, Task.Delay(node.LivenessInterval, stopping)).ConfigureAwait(false);
            await completed.ConfigureAwait(false); // surfaces cancellation on stop
            if (completed == exited)
            {
                break;
            }

            if (await PingAsync(url, LivenessTimeout, stopping).ConfigureAwait(false))
            {
                failures = 0;
            }
            else if (++failures >= node.LivenessFailureThreshold && !process.HasExited)
            {
                LogUnresponsive(_logger, url, failures);
                lock (_gate)
                {
                    instance.Ready = false;
                    Kill(process);
                }

                await exited.ConfigureAwait(false);
                return "liveness";
            }
        }

        await exited.ConfigureAwait(false);
        return "exit";
    }

    // Called under the lifecycle gate.
    private bool TryStart(Instance instance)
    {
        if (_disposed || _stopRequested) return false;
        var startInfo = instance.StartInfo;
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
        instance.Process?.Dispose();
        instance.Process = process;
        instance.Ready = false;
        LogSpawned(_logger, process.Id, startInfo.FileName, string.Join(' ', startInfo.ArgumentList));
        return true;
    }

    private void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
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

    private async Task<bool> PingAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            using var response = await httpClients.CreateClient(SsrGateway.HttpClientName).GetAsync($"{url}/health", HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or InvalidOperationException or FormatException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
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

    private static bool IsOccupied(int port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        try { socket.Bind(new IPEndPoint(IPAddress.Loopback, port)); }
        catch (SocketException) { return true; }
        return false;
    }

    private void StartupFailure(SsrOptions ssr, string message)
    {
        LogStartupFailure(_logger, message);
        Fail(ssr, message);
    }

    private static void Fail(SsrOptions ssr, string message)
    {
        if (ssr.Mode == SsrMode.Required)
        {
            throw new InertiaSsrException($"Inertia SSR failed (startup): {message}", "startup");
        }
    }

    private sealed class Instance(int index, int port, ProcessStartInfo startInfo)
    {
        public int Index { get; } = index;
        public int Port { get; } = port;
        public ProcessStartInfo StartInfo { get; } = startInfo;
        public Process? Process { get; set; }
        public bool Ready { get; set; }
        public Task Supervisor { get; set; } = Task.CompletedTask;
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Stopped the SSR server: it also accepts connections on {Address}:{Port} and serves unauthenticated /render and /shutdown endpoints. Bind it to loopback with inertia({{ ssr: {{ host: '127.0.0.1' }} }}) in the Vite config, or set NodeProcess.AllowNetworkExposure. Pages render on the client.")]
    private static partial void LogExposedStopped(ILogger logger, string address, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SSR server at {Url} did not become healthy within {Timeout}; pages render on the client until it does.")]
    private static partial void LogStartupTimeout(ILogger logger, string url, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "The SSR servers at {Urls} did not become healthy within {Timeout}. With NodeProcess.Instances > 1 every instance needs its own port: the SSR entry must listen on process.env.INERTIA_SSR_PORT, e.g. createServer(render, {{ port: Number(process.env.INERTIA_SSR_PORT) }}).")]
    private static partial void LogInstancesNotHealthy(ILogger logger, string urls, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SSR server at {Url} failed {Failures} health checks in a row; killing it so it can be restarted.")]
    private static partial void LogUnresponsive(ILogger logger, string url, int failures);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SSR server {Url} is reached over plain HTTP: page props cross the network unencrypted. Use HTTPS or keep the renderer on a private network.")]
    private static partial void LogPlainHttpRemote(ILogger logger, string url);

    [LoggerMessage(Level = LogLevel.Error, Message = "Managed SSR startup failed: {Reason}")]
    private static partial void LogStartupFailure(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping the owned Node SSR processes.")]
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
