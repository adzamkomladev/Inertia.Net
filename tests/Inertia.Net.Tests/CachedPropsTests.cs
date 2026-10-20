using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

/// <summary><see cref="InertiaProp.Cache"/>: values kept as JSON in the app's memory cache.</summary>
public sealed class CachedPropsTests
{
    private readonly Harness _harness = new();
    private int _loads;

    private InertiaProp Stats(string key = "stats") => Prop(() => new User($"load {++_loads}")).Cache(key, TimeSpan.FromMinutes(5));

    [Fact]
    public async Task The_loader_runs_once_and_the_json_is_reused()
    {
        var first = await _harness.PageAsync(new InertiaProps { ["stats"] = Stats() });
        var second = await _harness.PageAsync(new InertiaProps { ["stats"] = Stats() });

        JsonAssert.Equal("""{"name":"load 1","email":null}""", first["props"]!["stats"]);
        JsonAssert.Equal("""{"name":"load 1","email":null}""", second["props"]!["stats"]);
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task Keys_are_separate_and_expire_from_the_app_cache()
    {
        await _harness.PageAsync(new InertiaProps { ["a"] = Stats("a"), ["b"] = Stats("b") });
        Assert.Equal(2, _loads);

        _harness.Services.GetRequiredService<IMemoryCache>().Remove(new PropCacheKey("a"));
        var page = await _harness.PageAsync(new InertiaProps { ["a"] = Stats("a"), ["b"] = Stats("b") });
        Assert.Equal("load 3", (string?)page["props"]!["a"]!["name"]);
        Assert.Equal("load 2", (string?)page["props"]!["b"]!["name"]);
    }

    [Fact]
    public async Task Cached_json_is_html_safe_in_both_responses()
    {
        InertiaProp Script() => Prop(() => "</script><b>&").Cache("script", TimeSpan.FromMinutes(1));
        var json = await Harness.ExecuteAsync(_harness.Context().AsInertia(), Render("TestComponent", new InertiaProps { ["s"] = Script() }));
        var html = await Harness.ExecuteAsync(_harness.Context(), Render("TestComponent", new InertiaProps { ["s"] = Script() }));

        Assert.Equal("</script><b>&", (string?)Harness.ParsePage(json)["props"]!["s"]);
        Assert.DoesNotContain("</script><b>", Harness.ExtractPageJson(html), StringComparison.Ordinal);
        Assert.Equal("</script><b>&", (string?)Harness.ParsePage(html)["props"]!["s"]);
    }

    [Fact]
    public async Task Deferred_cached_props_stay_deferred_and_are_cached_when_loaded()
    {
        InertiaProp Feed() => Defer(() => new List<string> { $"post {++_loads}" }).Cache("feed", TimeSpan.FromMinutes(1));

        var initial = await _harness.PageAsync(new InertiaProps { ["feed"] = Feed() });
        JsonAssert.Missing(initial["props"], "feed");
        JsonAssert.Equal("""{"default":["feed"]}""", initial["deferredProps"]);
        Assert.Equal(0, _loads);

        var loaded = await _harness.PageAsync(new InertiaProps { ["feed"] = Feed() }, c => c.AsPartial(only: "feed"));
        var again = await _harness.PageAsync(new InertiaProps { ["feed"] = Feed() }, c => c.AsPartial(only: "feed"));
        JsonAssert.Equal("""["post 1"]""", loaded["props"]!["feed"]);
        JsonAssert.Equal("""["post 1"]""", again["props"]!["feed"]);
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task Merge_and_scroll_metadata_survive_a_cache_hit()
    {
        InertiaProp Posts() => Scroll(() => new PagedUsers([new User($"u{++_loads}")], 1, HasMore: true)).Cache("posts", TimeSpan.FromMinutes(1));

        await _harness.PageAsync(new InertiaProps { ["posts"] = Posts() });
        var hit = await _harness.PageAsync(new InertiaProps { ["posts"] = Posts() });

        Assert.Equal(1, _loads);
        JsonAssert.Equal("""["posts.data"]""", hit["mergeProps"]);
        Assert.Equal(2, (int?)hit["scrollProps"]!["posts"]!["nextPage"]);
    }

    [Fact]
    public async Task A_rescued_failure_is_not_cached()
    {
        var calls = 0;
        InertiaProp Flaky() => Prop<int>(() => ++calls == 1 ? throw new InvalidOperationException("down") : 42).Rescue().Cache("flaky", TimeSpan.FromMinutes(1));

        var failed = await _harness.PageAsync(new InertiaProps { ["n"] = Flaky() });
        JsonAssert.Equal("""["n"]""", failed["rescuedProps"]);
        var recovered = await _harness.PageAsync(new InertiaProps { ["n"] = Flaky() });
        Assert.Equal(42, (int?)recovered["props"]!["n"]);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Parallel_cached_props_do_not_start_their_loader_on_a_hit()
    {
        InertiaProp Slow() => Prop(async ct =>
        {
            await Task.Yield();
            return ++_loads;
        }).Parallel().Cache("slow", TimeSpan.FromMinutes(1));

        await _harness.PageAsync(new InertiaProps { ["slow"] = Slow() });
        var hit = await _harness.PageAsync(new InertiaProps { ["slow"] = Slow() });
        Assert.Equal(1, (int?)hit["props"]!["slow"]);
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task A_loader_that_returns_a_prop_cannot_be_cached()
    {
        var page = new InertiaProps { ["p"] = Prop(() => (object)Prop(() => 1)).Cache("nested", TimeSpan.FromMinutes(1)) };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.PageAsync(page));
        Assert.Contains("Cache() needs a plain value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Arguments_are_checked()
    {
        Assert.Throws<ArgumentException>(() => Prop(() => 1).Cache("", TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Prop(() => 1).Cache("k", TimeSpan.Zero));
    }

    [Fact]
    public async Task Entries_have_a_size_so_a_size_limited_app_cache_accepts_them()
    {
        var harness = new Harness(configureServices: s => s.AddMemoryCache(o => o.SizeLimit = 1024));
        var page = await harness.PageAsync(new InertiaProps { ["n"] = Prop(() => 7).Cache("sized", TimeSpan.FromMinutes(1)) });
        Assert.Equal(7, (int?)page["props"]!["n"]);
        Assert.True(harness.Services.GetRequiredService<IMemoryCache>().TryGetValue(new PropCacheKey("sized"), out _));
    }
}
