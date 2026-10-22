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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref Calls);
            var json = $$"""{"head":["<title>SSR {{call}}</title>"],"body":"<div id=\"app\" data-server-rendered=\"true\">render {{call}}</div>"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
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
