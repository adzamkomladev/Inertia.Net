using Microsoft.Extensions.Logging;

namespace Inertia.Net.Tests;

public sealed class ParallelPropsFailureTests
{
    [Fact]
    public async Task Every_parallel_failure_is_logged_with_its_path_and_the_first_exception_is_preserved()
    {
        var harness = new Harness();
        var first = new InvalidOperationException("first failure");
        var second = new InvalidOperationException("second failure");
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        async Task<int> FailAsync(Exception error)
        {
            if (Interlocked.Increment(ref started) == 2) both.TrySetResult();
            await both.Task.WaitAsync(TestContext.Current.CancellationToken);
            throw error;
        }

        var props = new InertiaProps
        {
            ["nested"] = new InertiaProps
            {
                ["first"] = Inertia.Prop(() => FailAsync(first)).Parallel(),
                ["second"] = Inertia.Prop(() => FailAsync(second)).Parallel(),
            },
        };
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.PageAsync(props));
        Assert.Same(first, thrown);
        Assert.Contains(harness.Logs.Entries, entry => entry.Level == LogLevel.Warning && ReferenceEquals(entry.Exception, first) && entry.Message.Contains("nested.first", StringComparison.Ordinal));
        Assert.Contains(harness.Logs.Entries, entry => entry.Level == LogLevel.Warning && ReferenceEquals(entry.Exception, second) && entry.Message.Contains("nested.second", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_parallel_failure_cancels_a_sequential_loader_without_aborting_the_request(bool cached, bool rescue)
    {
        var harness = new Harness();
        using var request = new CancellationTokenSource();
        var context = harness.Context();
        context.RequestAborted = request.Token;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var failure = new InvalidOperationException("parallel failure");
        var sequential = Inertia.Prop(async ct =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { cancelled = ct.IsCancellationRequested; }
            return 1;
        });
        if (cached) sequential.Cache("slow", TimeSpan.FromMinutes(1));
        if (rescue) sequential.Rescue();
        var props = new InertiaProps
        {
            ["sequential"] = sequential,
            ["parallel"] = Inertia.Prop<int>(async () =>
            {
                await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
                throw failure;
            }).Parallel(),
        };
        var rendering = Harness.ExecuteAsync(context, Inertia.Render("Failure", props));
        try
        {
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => rendering.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
            Assert.Same(failure, thrown);
            Assert.True(cancelled);
            Assert.False(request.IsCancellationRequested);
        }
        finally
        {
            await request.CancelAsync();
            Task completion = rendering;
            await completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }
}
