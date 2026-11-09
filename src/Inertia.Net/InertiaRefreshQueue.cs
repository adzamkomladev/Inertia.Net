using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inertia.Net;

/// <summary>
/// Bounded, host-owned refresh work. Response completion only enqueues; it never awaits a loader or renderer. A few workers drain the
/// queue, and each refresh gets a deadline, so one loader that hangs neither blocks the others nor holds its key's claim for good.
/// </summary>
internal sealed partial class InertiaRefreshQueue(InertiaMetrics metrics, TimeProvider timeProvider, ILogger<InertiaRefreshQueue> logger) : BackgroundService
{
    internal const int Capacity = 64;
    internal const int Workers = 4;
    internal static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DropLogInterval = TimeSpan.FromSeconds(30);

    private readonly Channel<RefreshWork> _work = Channel.CreateBounded<RefreshWork>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = false,
        FullMode = BoundedChannelFullMode.Wait,
        AllowSynchronousContinuations = false,
    });
    private int _pending;
    private long _nextDropLog;
    private int _suppressedDrops;

    internal int PendingCount => Volatile.Read(ref _pending);

    public void Enqueue(RefreshWork work)
    {
        Interlocked.Increment(ref _pending);
        if (!_work.Writer.TryWrite(work))
        {
            try
            {
                metrics.RecordDroppedRefresh(work.Kind);
                LogDrop(work.Kind);
            }
            finally
            {
                work.Release();
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var workers = new Task[Workers];
            for (var i = 0; i < workers.Length; i++)
            {
                workers[i] = RunWorkerAsync(stoppingToken);
            }

            await Task.WhenAll(workers);
        }
        finally
        {
            _work.Writer.TryComplete();
            while (_work.Reader.TryRead(out var work))
            {
                work.Release();
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var work in _work.Reader.ReadAllAsync(stoppingToken))
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(RefreshTimeout);
                try
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    // WaitAsync: a loader that ignores the token must not keep this worker past the deadline.
                    await work.Run(deadline.Token).WaitAsync(deadline.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Host shutdown cancels refreshes; stale values keep their existing expiration.
                }
                catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
                {
                    LogTimedOut(logger, ex, work.Kind, RefreshTimeout);
                }
                catch (Exception ex)
                {
                    LogFailed(logger, ex, work.Kind);
                }
                finally
                {
                    work.Release();
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    // At most one message per interval: a full queue drops a refresh on every stale read.
    private void LogDrop(string kind)
    {
        var now = timeProvider.GetTimestamp();
        var next = Volatile.Read(ref _nextDropLog);
        var due = now + (long)(DropLogInterval.TotalSeconds * timeProvider.TimestampFrequency);
        if (now < next || Interlocked.CompareExchange(ref _nextDropLog, due, next) != next)
        {
            Interlocked.Increment(ref _suppressedDrops);
            return;
        }

        LogDropped(logger, kind, Interlocked.Exchange(ref _suppressedDrops, 0));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _work.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped an Inertia {Kind} cache refresh because the background queue is full or stopping ({Suppressed} similar drops not logged since the last message).")]
    private static partial void LogDropped(ILogger logger, string kind, int suppressed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An Inertia {Kind} background cache refresh did not finish within {Timeout} and was cancelled; the stale entry is kept until it expires.")]
    private static partial void LogTimedOut(ILogger logger, Exception exception, string kind, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An Inertia {Kind} background cache refresh failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string kind);
}

internal sealed record RefreshWork(Func<CancellationToken, Task> Run, Action Release, string Kind);
