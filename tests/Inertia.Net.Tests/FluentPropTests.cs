namespace Inertia.Net.Tests;

public sealed class FluentPropTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fresh_implies_once_and_controls_loaded_once_exclusion(bool fresh)
    {
        var prop = Inertia.Prop(() => 42).Fresh(fresh);
        Assert.True(prop.IsOnce);
        var page = await new Harness().PageAsync(new InertiaProps { ["count"] = prop },
            context => context.WithHeader(InertiaHeaders.Inertia, "true").WithHeader(InertiaHeaders.ExceptOnceProps, "count"));
        Assert.Equal(fresh, page["props"]!.AsObject().ContainsKey("count"));
        JsonAssert.Equal("""{"count":{"prop":"count","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task WithGroup_implies_defer_and_does_not_run_the_loader_initially()
    {
        var calls = 0;
        var prop = Inertia.Prop(() => ++calls).WithGroup("stats");
        var page = await new Harness().PageAsync(new InertiaProps { ["count"] = prop });
        Assert.Equal(0, calls);
        Assert.False(page["props"]!.AsObject().ContainsKey("count"));
        JsonAssert.Equal("""{"stats":["count"]}""", page["deferredProps"]);
    }

    [Fact]
    public async Task MatchOn_accumulates_with_append_and_prepend_match_keys()
    {
        var prop = Inertia.Prop(() => 1).Append("items", "id").MatchOn("uuid", "key").Prepend("older", "id").MatchOn("other");
        var page = await new Harness().PageAsync(new InertiaProps { ["feed"] = prop });
        JsonAssert.Equal("""["feed.items.id","feed.uuid","feed.key","feed.older.id","feed.other"]""", page["matchPropsOn"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Conflicting_explicit_root_merge_calls_throw_without_changing_the_direction(bool appendFirst)
    {
        var prop = Inertia.Prop(() => 1);
        if (appendFirst) prop.Append();
        else prop.Prepend();
        var exception = Assert.Throws<InvalidOperationException>(() => appendFirst ? prop.Prepend() : prop.Append());
        Assert.Contains("Append()", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Prepend()", exception.Message, StringComparison.Ordinal);
        Assert.Equal(appendFirst, prop.AppendAtRoot);
    }

    [Fact]
    public async Task Merge_default_can_be_overridden_by_an_explicit_root_prepend()
    {
        var page = await new Harness().PageAsync(new InertiaProps { ["feed"] = Inertia.Merge(new[] { 1 }).Prepend().Prepend() });
        JsonAssert.Equal("""["feed"]""", page["prependProps"]);
        Assert.Null(page["mergeProps"]);
    }
}
