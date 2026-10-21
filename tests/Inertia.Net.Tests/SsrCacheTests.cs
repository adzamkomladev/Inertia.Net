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

    private static Task<string> RenderAsync(Harness harness, int value = 1, TimeSpan? cache = null)
    {
        var result = Render("Home", new InertiaProps { ["value"] = value });
        if (cache is { } duration)
        {
            result.WithSsrCache(duration);
        }

        return Harness.ExecuteAsync(harness.Context(), result);
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
        options.Ssr.Cache.MaxBytes = 0;
        var result = new InertiaOptionsValidator().Validate(null, options);
        Assert.Contains("Ssr.Cache.Duration must not be negative.", result.Failures!);
        Assert.Contains("Ssr.Cache.MaxBytes must be positive.", result.Failures!);
    }
}
