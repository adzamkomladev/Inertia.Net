using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Tests;

public class ScrollPageTests
{
    private readonly Harness _harness = new();

    private static InertiaProps Props() => new() { ["posts"] = Inertia.Scroll(new Page<int>([3, 4], 2, 2, 5, true)) };

    [Fact]
    public async Task Full_render_and_partial_prepend_emit_page_metadata()
    {
        var full = await _harness.PageAsync(Props(), c => c.AsInertia());
        JsonAssert.Equal("""["posts.data"]""", full["mergeProps"]);
        JsonAssert.Equal("""{"posts":{"pageName":"page","previousPage":1,"nextPage":3,"currentPage":2,"reset":false}}""", full["scrollProps"]);
        var prepend = await _harness.PageAsync(Props(), c => c.AsPartial("posts").WithHeader(InertiaHeaders.InfiniteScrollMergeIntent, "prepend"));
        JsonAssert.Equal("""["posts.data"]""", prepend["prependProps"]);
        JsonAssert.Missing(prepend, "mergeProps");
        JsonAssert.Equal(full["scrollProps"]!.ToJsonString(), prepend["scrollProps"]);
    }

    [Fact]
    public async Task Reset_suppresses_merge_metadata_and_marks_scroll_reset()
    {
        var page = await _harness.PageAsync(Props(), c => c.AsPartial("posts").WithHeader(InertiaHeaders.Reset, "posts"));
        JsonAssert.Missing(page, "mergeProps");
        Assert.True((bool)page["scrollProps"]!["posts"]!["reset"]!);
    }

    [Fact]
    public async Task Deferred_page_only_loads_when_requested()
    {
        var calls = 0;
        InertiaProps Deferred() => new()
        {
            ["posts"] = Inertia.Scroll(() => { calls++; return new Page<int>([1, 2], 1, 2, 5, true); }).Defer(),
        };
        var initial = await _harness.PageAsync(Deferred());
        Assert.Equal(0, calls);
        JsonAssert.Equal("""{"default":["posts"]}""", initial["deferredProps"]);
        JsonAssert.Equal("""["posts.data"]""", initial["mergeProps"]);
        JsonAssert.Missing(initial, "scrollProps");
        var loaded = await _harness.PageAsync(Deferred(), c => c.AsPartial("posts"));
        Assert.Equal(1, calls);
        JsonAssert.Equal("""{"posts":{"pageName":"page","previousPage":null,"nextPage":2,"currentPage":1,"reset":false}}""", loaded["scrollProps"]);
    }

    [Fact]
    public async Task Cached_page_retains_scroll_metadata_on_cache_hits()
    {
        var calls = 0;
        InertiaProps Cached() => new()
        {
            ["posts"] = Inertia.Scroll(() => { calls++; return new Page<int>([1, 2], 1, 2, 5, true); }).Cache("paged-posts", TimeSpan.FromMinutes(1)),
        };
        var first = await _harness.PageAsync(Cached());
        var second = await _harness.PageAsync(Cached());
        Assert.Equal(1, calls);
        JsonAssert.Equal(first["props"]!.ToJsonString(), second["props"]);
        JsonAssert.Equal(first["scrollProps"]!.ToJsonString(), second["scrollProps"]);
        JsonAssert.Equal("""["posts.data"]""", second["mergeProps"]);
    }

    [Fact]
    public async Task Explicit_cache_values_also_retain_page_metadata()
    {
        await _harness.Services.GetRequiredService<IInertiaCache>().SetPropAsync("preset-page", new CursorPage<int>([1], 1, "next", null, true), TimeSpan.FromMinutes(1), cancellationToken: TestContext.Current.CancellationToken);
        var page = await _harness.PageAsync(new InertiaProps
        {
            ["posts"] = Inertia.Scroll<CursorPage<int>>(() => throw new InvalidOperationException()).Cache("preset-page", TimeSpan.FromMinutes(1)),
        });
        JsonAssert.Equal("""{"posts":{"pageName":"cursor","previousPage":null,"nextPage":"next","currentPage":1,"reset":false}}""", page["scrollProps"]);
    }
}
