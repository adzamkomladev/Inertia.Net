using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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
    public async Task Keys_are_separate_and_a_removed_key_reloads()
    {
        await _harness.PageAsync(new InertiaProps { ["a"] = Stats("a"), ["b"] = Stats("b") });
        Assert.Equal(2, _loads);

        await _harness.Services.GetRequiredService<IInertiaCache>().RemovePropAsync("a", TestContext.Current.CancellationToken);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => Prop(() => 1).Cache("k", TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentException>(() => Prop(() => 1).Cache("k", TimeSpan.FromMinutes(1), tags: [""]));
    }

    [Fact]
    public async Task Entries_have_a_size_so_a_size_limited_app_cache_accepts_them()
    {
        var harness = new Harness(configureServices: s => s.AddMemoryCache(o => o.SizeLimit = 1024));
        var page = await harness.PageAsync(new InertiaProps { ["n"] = Prop(() => 7).Cache("sized", TimeSpan.FromMinutes(1)) });
        Assert.Equal(7, (int?)page["props"]!["n"]);
        Assert.NotNull(await harness.Services.GetRequiredService<IInertiaCacheStore>().GetAsync(PropKey("sized"), TestContext.Current.CancellationToken));
        Assert.Equal(1024, harness.Services.GetRequiredService<IOptions<MemoryCacheOptions>>().Value.SizeLimit);
    }

    [Fact]
    public async Task Tags_remove_only_the_props_that_carry_them()
    {
        InertiaProp Orders(string user) => Prop(() => $"{user} {++_loads}").Cache($"orders:{user}", TimeSpan.FromMinutes(5), tags: [$"user:{user}", "orders"]);
        InertiaProps Page() => new() { ["ann"] = Orders("ann"), ["bob"] = Orders("bob"), ["stats"] = Stats() };
        var cache = _harness.Services.GetRequiredService<IInertiaCache>();

        await _harness.PageAsync(Page());
        await cache.RemoveByTagAsync("user:ann", TestContext.Current.CancellationToken);
        var page = await _harness.PageAsync(Page());
        Assert.Equal("ann 4", (string?)page["props"]!["ann"]);
        Assert.Equal("bob 2", (string?)page["props"]!["bob"]);

        await cache.RemoveByTagAsync("orders", TestContext.Current.CancellationToken);
        page = await _harness.PageAsync(Page());
        Assert.Equal("ann 5", (string?)page["props"]!["ann"]);
        Assert.Equal("bob 6", (string?)page["props"]!["bob"]);
        Assert.Equal("load 3", (string?)page["props"]!["stats"]!["name"]);
    }

    [Fact]
    public async Task Clearing_the_props_removes_every_cached_prop()
    {
        await _harness.PageAsync(new InertiaProps { ["a"] = Stats("a"), ["b"] = Stats("b") });
        await _harness.Services.GetRequiredService<IInertiaCache>().ClearPropsAsync(TestContext.Current.CancellationToken);
        await _harness.PageAsync(new InertiaProps { ["a"] = Stats("a"), ["b"] = Stats("b") });
        Assert.Equal(4, _loads);
    }

    [Fact]
    public async Task A_value_can_be_set_without_its_loader()
    {
        var cache = _harness.Services.GetRequiredService<IInertiaCache>();
        await cache.SetPropAsync("stats", new User("saved", "s@example.com"), TimeSpan.FromMinutes(5), cancellationToken: TestContext.Current.CancellationToken);
        var page = await _harness.PageAsync(new InertiaProps { ["stats"] = Stats() });
        JsonAssert.Equal("""{"name":"saved","email":"s@example.com"}""", page["props"]!["stats"]);
        Assert.Equal(0, _loads);

        // A set value carries tags like a loaded one, and the scroll metadata of a value that provides it.
        await cache.SetPropAsync("posts", new PagedUsers([new User("set")], 2, HasMore: false), TimeSpan.FromMinutes(5), tags: ["feed"], cancellationToken: TestContext.Current.CancellationToken);
        InertiaProp Posts() => Scroll(() => new PagedUsers([new User($"u{++_loads}")], 1, HasMore: true)).Cache("posts", TimeSpan.FromMinutes(1));
        page = await _harness.PageAsync(new InertiaProps { ["posts"] = Posts() });
        Assert.Equal("set", (string?)page["props"]!["posts"]!["data"]![0]!["name"]);
        Assert.Equal(1, (int?)page["scrollProps"]!["posts"]!["previousPage"]);
        await cache.RemoveByTagAsync("feed", TestContext.Current.CancellationToken);
        page = await _harness.PageAsync(new InertiaProps { ["posts"] = Posts() });
        Assert.Equal("u1", (string?)page["props"]!["posts"]!["data"]![0]!["name"]);
    }

    [Fact]
    public async Task A_stale_value_is_served_and_refreshed_after_the_response()
    {
        var time = new FakeTimeProvider();
        var harness = new Harness(timeProvider: time);
        InertiaProp Feed() => Prop(() => $"load {++_loads}").Cache("feed", TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromMinutes(5));

        var (first, completion) = await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });
        Assert.Equal("load 1", (string?)first["props"]!["feed"]);
        Assert.Equal(0, completion.Pending); // a fresh or loaded value schedules nothing

        time.Advance(TimeSpan.FromMinutes(2));
        var (stale, refresh) = await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });
        Assert.Equal("load 1", (string?)stale["props"]!["feed"]);
        Assert.Equal(1, _loads); // the loader waits for the response to be sent

        // Another request meanwhile serves the stale value too, without a second refresh.
        var (again, other) = await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });
        Assert.Equal("load 1", (string?)again["props"]!["feed"]);
        Assert.Equal(0, other.Pending);

        await refresh.CompleteAsync();
        Assert.Equal(2, _loads);
        var (fresh, _) = await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });
        Assert.Equal("load 2", (string?)fresh["props"]!["feed"]);

        // Past the stale time too, the loader runs during the request again.
        time.Advance(TimeSpan.FromMinutes(10));
        var (expired, _) = await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });
        Assert.Equal("load 3", (string?)expired["props"]!["feed"]);
    }

    [Fact]
    public async Task A_refresh_skips_its_loader_when_a_fresh_value_was_stored_since()
    {
        var time = new FakeTimeProvider();
        var harness = new Harness(timeProvider: time);
        InertiaProp Feed() => Prop(() => $"load {++_loads}").Cache("feed", TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromMinutes(5));

        await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });
        time.Advance(TimeSpan.FromMinutes(2));
        var (_, refresh) = await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });

        // Another refresh (or another app instance sharing the store) stores a fresh value before this response's refresh runs.
        await harness.Services.GetRequiredService<IInertiaCache>().SetPropAsync("feed", "saved", TimeSpan.FromMinutes(1), cancellationToken: TestContext.Current.CancellationToken);
        await refresh.CompleteAsync();
        Assert.Equal(1, _loads);
        var (page, _) = await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });
        Assert.Equal("saved", (string?)page["props"]!["feed"]);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_stale_value_and_is_logged()
    {
        var time = new FakeTimeProvider();
        var harness = new Harness(timeProvider: time);
        var fail = false;
        InertiaProp Feed() => Prop(() => fail ? throw new InvalidOperationException("db down") : $"load {++_loads}")
            .Cache("feed", TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromMinutes(5));

        await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });
        time.Advance(TimeSpan.FromMinutes(2));
        fail = true;
        var (_, refresh) = await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });
        await refresh.CompleteAsync();
        Assert.Contains(harness.Logs.Entries, e => e.Level == LogLevel.Warning && e.Exception?.Message == "db down");

        // The next stale read tries again.
        fail = false;
        var (page, retry) = await RenderAsync(harness, new InertiaProps { ["feed"] = Feed() });
        Assert.Equal("load 1", (string?)page["props"]!["feed"]);
        await retry.CompleteAsync();
        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task Stale_props_of_one_response_refresh_one_after_another()
    {
        var time = new FakeTimeProvider();
        var harness = new Harness(timeProvider: time);
        var running = 0;
        var overlapped = false;
        InertiaProp Slow(string key) => Prop(async ct =>
        {
            overlapped |= Interlocked.Increment(ref running) > 1;
            await Task.Delay(10, ct);
            Interlocked.Decrement(ref running);
            return ++_loads;
        }).Parallel().Cache(key, TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromMinutes(5));

        await RenderAsync(harness, new InertiaProps { ["a"] = Slow("a"), ["b"] = Slow("b") });
        time.Advance(TimeSpan.FromMinutes(2));
        var (_, refresh) = await RenderAsync(harness, new InertiaProps { ["a"] = Slow("a"), ["b"] = Slow("b") });
        Assert.Equal(1, refresh.Pending);
        Assert.Equal(2, _loads);
        overlapped = false;
        await refresh.CompleteAsync();
        Assert.Equal(4, _loads);
        Assert.False(overlapped);
    }

    [Fact]
    public async Task A_custom_store_gets_prefixed_keys_and_the_tags()
    {
        var store = new RecordingStore();
        var harness = new Harness(configureServices: s => s.AddSingleton<IInertiaCacheStore>(store));
        var props = new InertiaProps { ["n"] = Prop(() => ++_loads).Cache("n", TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromMinutes(2), tags: ["t"]) };

        await harness.PageAsync(props);
        var page = await harness.PageAsync(props);
        Assert.Equal(1, (int?)page["props"]!["n"]);
        var (key, options) = Assert.Single(store.Sets);
        Assert.Equal(PropKey("n"), key);
        Assert.Equal("inertia:prop:n", key.ToString());
        Assert.Equal(TimeSpan.FromMinutes(3), options.Expiration);
        Assert.Equal(new[] { "t", InertiaCacheTags.Props }, options.Tags);
    }

    [Fact]
    public async Task A_failing_store_renders_without_the_cache_and_logs_once()
    {
        var store = new RecordingStore { Fail = true };
        var harness = new Harness(configureServices: s => s.AddSingleton<IInertiaCacheStore>(store));
        for (var i = 0; i < 3; i++)
        {
            var page = await harness.PageAsync(new InertiaProps { ["stats"] = Stats() });
            Assert.Equal($"load {i + 1}", (string?)page["props"]!["stats"]!["name"]);
        }

        Assert.Single(harness.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("cache store failed", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Services.GetRequiredService<IInertiaCache>().RemovePropAsync("stats", TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Values_from_another_format_read_as_a_miss()
    {
        var store = new RecordingStore();
        store.Values[PropKey("stats")] = [9, 0, 1, 2, 3];
        var harness = new Harness(configureServices: s => s.AddSingleton<IInertiaCacheStore>(store));
        var page = await harness.PageAsync(new InertiaProps { ["stats"] = Stats() });
        Assert.Equal("load 1", (string?)page["props"]!["stats"]!["name"]);
    }

    [Fact]
    public async Task Parallel_cached_props_wait_for_a_slow_store_before_their_loader()
    {
        var store = new RecordingStore { Yield = true };
        var harness = new Harness(configureServices: s => s.AddSingleton<IInertiaCacheStore>(store));
        InertiaProp Slow() => Prop(async ct =>
        {
            await Task.Yield();
            return ++_loads;
        }).Parallel().Cache("slow", TimeSpan.FromMinutes(1));

        await harness.PageAsync(new InertiaProps { ["slow"] = Slow(), ["other"] = Prop(async ct => await Task.FromResult(0)).Parallel() });
        var hit = await harness.PageAsync(new InertiaProps { ["slow"] = Slow(), ["other"] = Prop(async ct => await Task.FromResult(0)).Parallel() });
        Assert.Equal(1, (int?)hit["props"]!["slow"]);
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task A_replaced_value_is_only_removed_by_its_own_tags()
    {
        var store = _harness.Services.GetRequiredService<IInertiaCacheStore>();
        await store.SetAsync(PropKey("k"), [1], new InertiaCacheEntryOptions(TimeSpan.FromMinutes(1), ["x"]), TestContext.Current.CancellationToken);
        byte[] replaced = [2];
        await store.SetAsync(PropKey("k"), replaced, new InertiaCacheEntryOptions(TimeSpan.FromMinutes(1), ["y"]), TestContext.Current.CancellationToken);

        await store.RemoveByTagAsync("x", TestContext.Current.CancellationToken);
        Assert.Same(replaced, await store.GetAsync(PropKey("k"), TestContext.Current.CancellationToken));
        await store.RemoveByTagAsync("y", TestContext.Current.CancellationToken);
        Assert.Null(await store.GetAsync(PropKey("k"), TestContext.Current.CancellationToken));
    }

    private static InertiaCacheKey PropKey(string key) => new(InertiaCacheEntryKind.Prop, key);

    private static async Task<(System.Text.Json.Nodes.JsonObject Page, CompletingResponseFeature Completion)> RenderAsync(Harness harness, InertiaProps props)
    {
        var context = harness.Context();
        var completion = CompletingResponseFeature.On(context);
        var body = await Harness.ExecuteAsync(context, Render("TestComponent", props));
        return (Harness.ParsePage(body), completion);
    }

    /// <summary>A dictionary store that records what it is given, and can fail or answer asynchronously.</summary>
    internal sealed class RecordingStore : IInertiaCacheStore
    {
        public Dictionary<InertiaCacheKey, byte[]> Values { get; } = [];

        public List<(InertiaCacheKey Key, InertiaCacheEntryOptions Options)> Sets { get; } = [];

        public bool Fail { get; init; }

        public bool Yield { get; init; }

        public async ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken cancellationToken)
        {
            await Pause();
            return Values.GetValueOrDefault(key);
        }

        public async ValueTask SetAsync(InertiaCacheKey key, byte[] value, InertiaCacheEntryOptions options, CancellationToken cancellationToken)
        {
            await Pause();
            Values[key] = value;
            Sets.Add((key, options));
        }

        public async ValueTask RemoveAsync(InertiaCacheKey key, CancellationToken cancellationToken)
        {
            await Pause();
            Values.Remove(key);
        }

        public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken) => throw new NotSupportedException();

        private async ValueTask Pause()
        {
            if (Fail) throw new InvalidOperationException("store down");
            if (Yield) await Task.Yield();
        }
    }
}
