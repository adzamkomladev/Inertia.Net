using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

/// <summary>The SSR response cache (<see cref="SsrCacheOptions"/>, <see cref="InertiaResult.WithSsrCache"/>).</summary>
public sealed class SsrCacheTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;

        public Func<int, CancellationToken, Task>? BeforeSend;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref Calls);
            if (BeforeSend is { } before) await before(call, cancellationToken);
            var json = $$"""{"head":["<title>SSR {{call}}</title>"],"body":"<div id=\"app\" data-server-rendered=\"true\">render {{call}}</div>"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static (Harness Harness, CountingHandler Handler) Create(Action<SsrCacheOptions>? cache = null, FakeTimeProvider? time = null)
    {
        var handler = new CountingHandler();
        var harness = new Harness(
            o =>
            {
                o.Ssr.Mode = SsrMode.Preferred;
                cache?.Invoke(o.Ssr.Cache);
            },
            time,
            s => s.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        return (harness, handler);
    }

    private static Task<string> RenderAsync(Harness harness, int value = 1, TimeSpan? cache = null) =>
        RenderAsync(harness, out _, value, cache);

    private static Task<string> RenderAsync(Harness harness, out CompletingResponseFeature completion, int value = 1, TimeSpan? cache = null, TimeSpan? staleFor = null)
    {
        var result = Render("Home", new InertiaProps { ["value"] = value });
        if (cache is { } duration)
        {
            if (staleFor is { } stale) result.WithSsrCache(duration, stale);
            else result.WithSsrCache(duration);
        }

        var context = harness.Context();
        completion = CompletingResponseFeature.On(context);
        return Harness.ExecuteAsync(context, result);
    }

    private static Task<string> RenderAsync(Harness harness, CancellationToken cancellationToken)
    {
        var context = harness.Context();
        context.RequestAborted = cancellationToken;
        return Harness.ExecuteAsync(context, Render("Home", new InertiaProps { ["value"] = 1 }));
    }

    [Fact]
    public async Task Without_a_duration_every_page_is_rendered()
    {
        var (harness, handler) = Create();
        Assert.Contains("render 1", await RenderAsync(harness), StringComparison.Ordinal);
        Assert.Contains("render 2", await RenderAsync(harness), StringComparison.Ordinal);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task The_same_page_json_reuses_the_render_until_it_expires()
    {
        var time = new FakeTimeProvider();
        var (harness, handler) = Create(c => c.Duration = TimeSpan.FromMinutes(1), time);

        var first = await RenderAsync(harness);
        var second = await RenderAsync(harness);
        Assert.Contains("render 1", second, StringComparison.Ordinal);
        Assert.Contains("<title>SSR 1</title>", second, StringComparison.Ordinal);
        Assert.Equal(first, second);

        // Different props make different page JSON, so a different entry.
        Assert.Contains("render 2", await RenderAsync(harness, value: 2), StringComparison.Ordinal);
        Assert.Equal(2, handler.Calls);

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Contains("render 3", await RenderAsync(harness), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_misses_render_once_per_page_including_after_expiration()
    {
        var time = new FakeTimeProvider();
        var (harness, handler) = Create(c => c.Duration = TimeSpan.FromMinutes(1), time);
        for (var round = 1; round <= 2; round++)
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            handler.BeforeSend = (_, ct) => release.Task.WaitAsync(ct);
            var requests = Enumerable.Range(0, 16).Select(_ => RenderAsync(harness)).ToArray();
            try
            {
                Assert.Equal(round, handler.Calls);
                Assert.All(requests, request => Assert.False(request.IsCompleted));
            }
            finally
            {
                release.TrySetResult();
            }

            var pages = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.All(pages, page => Assert.Equal(pages[0], page));
            Assert.Contains($"render {round}", pages[0], StringComparison.Ordinal);
            Assert.Equal(round, handler.Calls);
            time.Advance(TimeSpan.FromMinutes(2));
        }
    }

    [Fact]
    public async Task Cancelling_a_waiter_does_not_cancel_the_ssr_fill()
    {
        var (harness, handler) = Create(c => c.Duration = TimeSpan.FromMinutes(1));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.BeforeSend = (_, ct) => release.Task.WaitAsync(ct);
        using var cancelled = new CancellationTokenSource();
        var first = RenderAsync(harness);
        var waiter = RenderAsync(harness, cancelled.Token);
        try
        {
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
            Assert.False(first.IsCompleted);
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            release.TrySetResult();
        }

        Assert.Contains("render 1", await first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_misses_share_renders_even_when_the_store_cannot_keep_them(bool reject)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CountingHandler { BeforeSend = (_, ct) => release.Task.WaitAsync(ct) };
        var harness = new Harness(o =>
        {
            o.Ssr.Mode = SsrMode.Required;
            o.Ssr.Cache.Duration = TimeSpan.FromMinutes(1);
            if (reject) o.Ssr.Cache.MaxBytes = 1;
        }, configureServices: s =>
        {
            if (!reject) s.AddSingleton<IInertiaCacheStore>(new CachedPropsTests.RecordingStore { FailWrites = true });
            s.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        });
        var requests = Enumerable.Range(0, 16).Select(_ => RenderAsync(harness)).ToArray();
        release.SetResult();
        var pages = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.All(pages, page => Assert.Contains("render 1", page, StringComparison.Ordinal));
        Assert.Equal(1, handler.Calls);
        Assert.Contains("render 2", await RenderAsync(harness), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SsrMode.Preferred)]
    [InlineData(SsrMode.Required)]
    public async Task An_infinite_timeout_allows_a_cold_cached_render_and_its_waiter(SsrMode mode)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CountingHandler { BeforeSend = (_, ct) => release.Task.WaitAsync(ct) };
        var harness = new Harness(o =>
        {
            o.Ssr.Mode = mode;
            o.Ssr.Timeout = Timeout.InfiniteTimeSpan;
            o.Ssr.Cache.Duration = TimeSpan.FromMinutes(1);
        }, configureServices: s => s.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        var first = RenderAsync(harness);
        var waiter = RenderAsync(harness);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        release.SetResult();
        var pages = await Task.WhenAll(first, waiter).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.All(pages, page => Assert.Contains("render 1", page, StringComparison.Ordinal));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_cancelled_ssr_fill_releases_the_waiting_request()
    {
        var (harness, handler) = Create(c => c.Duration = TimeSpan.FromMinutes(1));
        handler.BeforeSend = async (call, ct) =>
        {
            if (call == 1) await Task.Delay(Timeout.Infinite, ct);
        };
        using var cancelled = new CancellationTokenSource();
        var first = RenderAsync(harness, cancelled.Token);
        var second = RenderAsync(harness);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Contains("render 2", await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(SsrMode.Preferred)]
    [InlineData(SsrMode.Required)]
    public async Task A_failed_ssr_fill_releases_the_waiting_request(SsrMode mode)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CountingHandler
        {
            BeforeSend = async (call, ct) =>
            {
                if (call != 1) return;
                await release.Task.WaitAsync(ct);
                throw new HttpRequestException("renderer unavailable");
            },
        };
        var harness = new Harness(o =>
        {
            o.Ssr.Mode = mode;
            o.Ssr.Cache.Duration = TimeSpan.FromMinutes(1);
        }, configureServices: s => s.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        var first = RenderAsync(harness);
        var second = RenderAsync(harness);
        release.SetResult();
        if (mode == SsrMode.Required)
        {
            await Assert.ThrowsAsync<InertiaSsrException>(() => first);
        }
        else
        {
            Assert.DoesNotContain("data-server-rendered", await first, StringComparison.Ordinal);
        }

        Assert.Contains("render 2", await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(SsrMode.Preferred)]
    [InlineData(SsrMode.Required)]
    public async Task Waiting_for_an_ssr_fill_obeys_the_render_deadline(SsrMode mode)
    {
        var handler = new CountingHandler();
        var harness = new Harness(o =>
        {
            o.Ssr.Mode = mode;
            o.Ssr.Timeout = TimeSpan.FromMilliseconds(50);
            o.Ssr.Cache.Duration = TimeSpan.FromMinutes(1);
        }, configureServices: s => s.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        var cache = harness.Services.GetRequiredService<InertiaCaching>();
        var key = InertiaCaching.SsrKey("{}"u8);
        Assert.True(cache.TryBeginFill(key, out var completion));
        var renderer = harness.Services.GetRequiredService<IInertiaSsrRenderer>();
        var context = new InertiaRootViewContext(harness.Context(), "Home", "app.html", new Dictionary<string, object?>(), "{}"u8.ToArray(), "app");
        try
        {
            if (mode == SsrMode.Required)
            {
                var error = await Assert.ThrowsAsync<InertiaSsrException>(() => renderer.RenderAsync(context).AsTask());
                Assert.Equal("timeout", error.Type);
            }
            else
            {
                Assert.Null(await renderer.RenderAsync(context));
            }

            Assert.Equal(0, handler.Calls);
        }
        finally
        {
            cache.EndFill(key, completion);
        }

        using var retry = await renderer.RenderAsync(context);
        Assert.NotNull(retry);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_cached_render_timeout_still_counts_as_a_server_failure()
    {
        var handler = new CountingHandler { BeforeSend = (_, ct) => Task.Delay(Timeout.Infinite, ct) };
        var harness = new Harness(o =>
        {
            o.Ssr.Mode = SsrMode.Preferred;
            o.Ssr.Timeout = TimeSpan.FromMilliseconds(100);
            o.Ssr.Cache.Duration = TimeSpan.FromMinutes(1);
            o.Ssr.CircuitBreaker.FailureThreshold = 1;
        }, configureServices: s => s.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));

        await RenderAsync(harness);
        var gateway = harness.Services.GetRequiredService<SsrGateway>();
        Assert.Equal(SsrCircuitState.Open, gateway.Endpoints[0].Breaker.State);
        await RenderAsync(harness);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_response_can_turn_the_cache_on_or_off()
    {
        var (harness, handler) = Create();
        await RenderAsync(harness, cache: TimeSpan.FromMinutes(1));
        Assert.Contains("render 1", await RenderAsync(harness, cache: TimeSpan.FromMinutes(1)), StringComparison.Ordinal);

        var (enabled, enabledHandler) = Create(c => c.Duration = TimeSpan.FromMinutes(1));
        await RenderAsync(enabled, cache: TimeSpan.Zero);
        Assert.Contains("render 2", await RenderAsync(enabled, cache: TimeSpan.Zero), StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(2, enabledHandler.Calls);
    }

    [Fact]
    public async Task Renders_larger_than_the_cache_are_not_kept()
    {
        var (harness, handler) = Create(c =>
        {
            c.Duration = TimeSpan.FromMinutes(1);
            c.MaxBytes = 10;
        });
        await RenderAsync(harness);
        await RenderAsync(harness);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public void Negative_durations_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Render("Home").WithSsrCache(TimeSpan.FromSeconds(-1)));
        var options = new InertiaOptions();
        options.Ssr.Cache.Duration = TimeSpan.FromSeconds(-1);
        options.Ssr.Cache.StaleFor = TimeSpan.FromSeconds(-1);
        options.Ssr.Cache.MaxBytes = 0;
        var result = new InertiaOptionsValidator().Validate(null, options);
        Assert.Contains("Ssr.Cache.Duration must not be negative.", result.Failures!);
        Assert.Contains("Ssr.Cache.StaleFor must not be negative.", result.Failures!);
        Assert.Contains("Ssr.Cache.MaxBytes must be positive.", result.Failures!);
        Assert.Throws<ArgumentOutOfRangeException>(() => Render("Home").WithSsrCache(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task Clearing_the_renders_renders_again_and_keeps_the_props()
    {
        var (harness, handler) = Create(c => c.Duration = TimeSpan.FromMinutes(1));
        await RenderAsync(harness);
        var cache = harness.Services.GetRequiredService<IInertiaCache>();
        await cache.ClearPropsAsync(TestContext.Current.CancellationToken);
        Assert.Contains("render 1", await RenderAsync(harness), StringComparison.Ordinal);

        await cache.ClearSsrAsync(TestContext.Current.CancellationToken);
        Assert.Contains("render 2", await RenderAsync(harness), StringComparison.Ordinal);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_stale_render_is_served_and_rendered_again_after_the_response()
    {
        var time = new FakeTimeProvider();
        var (harness, handler) = Create(c =>
        {
            c.Duration = TimeSpan.FromMinutes(1);
            c.StaleFor = TimeSpan.FromMinutes(5);
        }, time);

        await RenderAsync(harness);
        time.Advance(TimeSpan.FromMinutes(2));
        var stale = await RenderAsync(harness, out var refresh);
        Assert.Contains("render 1", stale, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);

        // Another request meanwhile gets the stale render too, without a second refresh.
        Assert.Contains("render 1", await RenderAsync(harness, out var other), StringComparison.Ordinal);
        Assert.Equal(0, other.Pending);

        await refresh.CompleteAsync();
        Assert.Equal(2, handler.Calls);
        Assert.Contains("render 2", await RenderAsync(harness), StringComparison.Ordinal);

        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Contains("render 3", await RenderAsync(harness), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refresh_skips_the_render_when_a_fresh_one_was_stored_since()
    {
        var time = new FakeTimeProvider();
        var (harness, handler) = Create(c =>
        {
            c.Duration = TimeSpan.FromMinutes(1);
            c.StaleFor = TimeSpan.FromMinutes(5);
        }, time);

        await RenderAsync(harness);
        time.Advance(TimeSpan.FromMinutes(2));
        await RenderAsync(harness, out var refresh);

        // The page is rendered and stored fresh before the stale read's refresh runs.
        await harness.Services.GetRequiredService<IInertiaCache>().ClearSsrAsync(TestContext.Current.CancellationToken);
        Assert.Contains("render 2", await RenderAsync(harness), StringComparison.Ordinal);
        await refresh.CompleteAsync();
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_response_sets_its_own_stale_time()
    {
        var time = new FakeTimeProvider();
        var (harness, handler) = Create(time: time);

        await RenderAsync(harness, out _, cache: TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromMinutes(5));
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Contains("render 1", await RenderAsync(harness, out var refresh, cache: TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromMinutes(5)), StringComparison.Ordinal);
        Assert.Equal(1, refresh.Pending);

        // Without a stale time an expired render is rendered again during the request.
        await RenderAsync(harness, out _, value: 2, cache: TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Contains($"render {handler.Calls + 1}", await RenderAsync(harness, out var none, value: 2, cache: TimeSpan.FromMinutes(1)), StringComparison.Ordinal);
        Assert.Equal(0, none.Pending);
    }

    [Fact]
    public async Task A_custom_store_holds_the_renders_under_their_hash()
    {
        var store = new CachedPropsTests.RecordingStore();
        var handler = new CountingHandler();
        var harness = new Harness(
            o =>
            {
                o.Ssr.Mode = SsrMode.Preferred;
                o.Ssr.Cache.Duration = TimeSpan.FromMinutes(1);
            },
            configureServices: s =>
            {
                s.AddSingleton<IInertiaCacheStore>(store);
                s.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
            });

        await RenderAsync(harness);
        Assert.Contains("render 1", await RenderAsync(harness), StringComparison.Ordinal);
        var (key, options) = Assert.Single(store.Sets);
        Assert.Equal(InertiaCacheEntryKind.Ssr, key.Kind);
        Assert.Matches("^inertia:ssr:[0-9A-F]{64}$", key.ToString());
        Assert.Equal(new[] { InertiaCacheTags.Ssr }, options.Tags);
    }
}
