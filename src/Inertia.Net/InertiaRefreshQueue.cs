using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inertia.Net;

/// <summary>Bounded, host-owned refresh work. Response completion only enqueues; it never awaits a loader or renderer.</summary>
internal sealed class InertiaRefreshQueue(InertiaMetrics metrics, ILogger<InertiaRefreshQueue> logger) : BackgroundService
{
    private readonly Channel<RefreshWork> _work = Channel.CreateBounded<RefreshWork>(new BoundedChannelOptions(64)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
        AllowSynchronousContinuations = false,
    });
    private int _pending;

    internal int PendingCount => Volatile.Read(ref _pending);

    public void Enqueue(RefreshWork work)
    {
        Interlocked.Increment(ref _pending);
        if (!_work.Writer.TryWrite(work))
        {
            try
            {
                metrics.RecordDroppedRefresh(work.Kind);
                logger.LogWarning("Dropped an Inertia {Kind} cache refresh because the background queue is full or stopping.", work.Kind);
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
            await foreach (var work in _work.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await work.Run(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Host shutdown cancels refreshes; stale values keep their existing expiration.
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "An Inertia {Kind} background cache refresh failed.", work.Kind);
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _work.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }
}

internal sealed record RefreshWork(Func<CancellationToken, Task> Run, Action Release, string Kind);
