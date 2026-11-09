using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inertia.Net.Tests;

public sealed class RefreshQueueTests
{
    [Fact]
    public async Task A_full_queue_drops_work_and_shutdown_releases_running_and_queued_claims()
    {
        var drops = new ConcurrentQueue<(long Count, string? Kind)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Name == "inertia.cache.refresh.dropped") current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, count, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "inertia.cache.kind") drops.Enqueue((count, tag.Value as string));
            }
        });
        listener.Start();
        var harness = new Harness();
        var worker = harness.Services.GetRequiredService<InertiaRefreshQueue>();
        var started = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = 0;
        var running = 0;
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            for (var i = 0; i < InertiaRefreshQueue.Workers; i++)
            {
                worker.Enqueue(new RefreshWork(async ct =>
                {
                    Interlocked.Increment(ref running);
                    if (Interlocked.Increment(ref started) == InertiaRefreshQueue.Workers) allStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }, () => Interlocked.Increment(ref released), "ssr"));
            }

            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            for (var i = 0; i < InertiaRefreshQueue.Capacity + 2; i++)
            {
                worker.Enqueue(new RefreshWork(_ =>
                {
                    Interlocked.Increment(ref running);
                    return Task.CompletedTask;
                }, () => Interlocked.Increment(ref released), "prop"));
            }

            Assert.Equal(2, released);
            Assert.Contains(drops, drop => drop is (1, "prop"));
            Assert.Equal(InertiaRefreshQueue.Workers + InertiaRefreshQueue.Capacity, worker.PendingCount);
            // One message for both drops: a full queue drops on every stale read.
            Assert.Single(harness.Logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("queue is full", StringComparison.Ordinal));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var total = InertiaRefreshQueue.Workers + InertiaRefreshQueue.Capacity + 2;
        Assert.Equal(total, released);
        Assert.Equal(InertiaRefreshQueue.Workers, running);
        Assert.Equal(0, worker.PendingCount);
        worker.Enqueue(new RefreshWork(_ => throw new InvalidOperationException("must not run"), () => Interlocked.Increment(ref released), "prop"));
        Assert.Equal(total + 1, released);
        Assert.Equal(0, worker.PendingCount);
    }

    [Fact]
    public async Task A_hung_refresh_does_not_hold_up_the_others()
    {
        var harness = new Harness();
        var worker = harness.Services.GetRequiredService<InertiaRefreshQueue>();
        var hung = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            // A loader that ignores its token.
            worker.Enqueue(new RefreshWork(_ =>
            {
                hung.TrySetResult();
                return Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
            }, () => { }, "prop"));
            await hung.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            worker.Enqueue(new RefreshWork(_ =>
            {
                ran.TrySetResult();
                return Task.CompletedTask;
            }, () => { }, "prop"));
            await ran.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }
}
