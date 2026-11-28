namespace Inertia.Net.Tests;

public sealed class PropTimeoutTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    // One loader that honors its token and one that ignores it: both stop holding the render at the timeout.
    private static InertiaProp Hanging(bool ignoresToken) => ignoresToken
        ? Inertia.Prop(() => new TaskCompletionSource<int>().Task)
        : Inertia.Prop(async ct => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return 1; });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_rescued_loader_that_times_out_is_listed_in_rescuedProps(bool parallel, bool ignoresToken)
    {
        var prop = Hanging(ignoresToken).Timeout(Short).Rescue();
        if (parallel) prop.Parallel();

        var page = await new Harness().PageAsync(new InertiaProps { ["slow"] = prop, ["ok"] = 1 }, c => c.AsInertia())
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        JsonAssert.Equal("""{"errors":{},"ok":1}""", page["props"]);
        JsonAssert.Equal("""["slow"]""", page["rescuedProps"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_loader_that_times_out_without_Rescue_fails_the_render(bool parallel)
    {
        var prop = Hanging(ignoresToken: false).Timeout(Short);
        if (parallel) prop.Parallel();

        await Assert.ThrowsAsync<TimeoutException>(() => new Harness().PageAsync(new InertiaProps { ["slow"] = prop })
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_loader_that_finishes_in_time_is_unaffected()
    {
        var page = await new Harness().PageAsync(new InertiaProps { ["fast"] = Inertia.Prop(async ct => { await Task.Yield(); return 7; }).Timeout(TimeSpan.FromSeconds(30)) }, c => c.AsInertia());
        Assert.Equal(7, (int)page["props"]!["fast"]!);
    }

    [Fact]
    public void Timeout_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Inertia.Prop(() => 1).Timeout(TimeSpan.Zero));
}
