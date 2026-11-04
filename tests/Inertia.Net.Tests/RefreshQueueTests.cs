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
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = 0;
        var running = 0;
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            worker.Enqueue(new RefreshWork(async ct =>
            {
                Interlocked.Increment(ref running);
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }, () => Interlocked.Increment(ref released), "ssr"));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            for (var i = 0; i < 65; i++)
            {
                worker.Enqueue(new RefreshWork(_ =>
                {
                    Interlocked.Increment(ref running);
                    return Task.CompletedTask;
                }, () => Interlocked.Increment(ref released), "prop"));
            }

            Assert.Equal(1, released);
            Assert.Contains(drops, drop => drop is (1, "prop"));
            Assert.Equal(65, worker.PendingCount);
            Assert.Contains(harness.Logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("queue is full", StringComparison.Ordinal));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(66, released);
        Assert.Equal(1, running);
        Assert.Equal(0, worker.PendingCount);
        worker.Enqueue(new RefreshWork(_ => throw new InvalidOperationException("must not run"), () => Interlocked.Increment(ref released), "prop"));
        Assert.Equal(67, released);
        Assert.Equal(0, worker.PendingCount);
    }
}
