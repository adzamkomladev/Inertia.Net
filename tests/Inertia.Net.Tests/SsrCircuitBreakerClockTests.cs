using Microsoft.Extensions.Options;

namespace Inertia.Net.Tests;

public sealed class SsrCircuitBreakerClockTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Wall_clock_jumps_do_not_change_when_the_break_elapses(int days)
    {
        var clock = new IndependentClock();
        var breaker = new SsrCircuitBreaker(new SsrCircuitBreakerOptions { FailureThreshold = 1, BreakDuration = TimeSpan.FromSeconds(10) }, clock);
        breaker.RecordFailure(probe: false);
        clock.JumpUtc(TimeSpan.FromDays(days));
        Assert.False(breaker.TryBeginProbe());
        clock.AdvanceTimestamp(TimeSpan.FromSeconds(10));
        Assert.True(breaker.TryBeginProbe());
    }

    [Fact]
    public void A_maximum_break_duration_does_not_overflow_into_an_immediate_probe()
    {
        var options = new InertiaOptions();
        options.Ssr.CircuitBreaker.FailureThreshold = 1;
        options.Ssr.CircuitBreaker.BreakDuration = TimeSpan.MaxValue;
        Assert.True(new InertiaOptionsValidator().Validate(Options.DefaultName, options).Succeeded);
        var clock = new IndependentClock();
        var breaker = new SsrCircuitBreaker(options.Ssr.CircuitBreaker, clock);
        breaker.RecordFailure(probe: false);
        Assert.False(breaker.TryBeginProbe());
        clock.AdvanceTimestamp(TimeSpan.FromSeconds(10));
        Assert.False(breaker.TryBeginProbe());
    }

    [Fact]
    public void A_one_tick_break_opens_for_exactly_one_elapsed_tick()
    {
        var clock = new IndependentClock();
        var breaker = new SsrCircuitBreaker(new SsrCircuitBreakerOptions { FailureThreshold = 1, BreakDuration = TimeSpan.FromTicks(1) }, clock);
        breaker.RecordFailure(probe: false);
        Assert.False(breaker.TryBeginProbe());
        clock.AdvanceTimestamp(TimeSpan.FromTicks(1));
        Assert.True(breaker.TryBeginProbe());
    }

    private sealed class IndependentClock : TimeProvider
    {
        private DateTimeOffset _utc = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _utc;
        public override long GetTimestamp() => _timestamp;
        public void JumpUtc(TimeSpan duration) => _utc += duration;
        public void AdvanceTimestamp(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
