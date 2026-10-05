using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Tests;

/// <summary>Resolver cases beyond the Laravel port: laziness, unwrap, bypass rules, scroll, combos, rescue, cancellation.</summary>
public class PropsWriterTests
{
    private readonly Harness _harness = new();

    private Task<JsonObject> Page(object props, Action<HttpContext>? arrange = null) => _harness.PageAsync(props, arrange);

    [Fact]
    public async Task Loaders_are_not_invoked_when_excluded_and_run_once_when_included()
    {
        var calls = new Dictionary<string, int>();
        InertiaProp Counted(string name, Func<Func<int>, InertiaProp> make) => make(() => calls[name] = calls.GetValueOrDefault(name) + 1);

        InertiaProps Props() => new()
        {
            ["lazy"] = Counted("lazy", Inertia.Prop),
            ["optional"] = Counted("optional", Inertia.Optional),
            ["deferred"] = Counted("deferred", f => Inertia.Defer(f)),
            ["nested"] = new InertiaProps { ["lazy"] = Counted("nested", Inertia.Prop) },
        };

        await Page(Props());
        Assert.Equal(new Dictionary<string, int> { ["lazy"] = 1, ["nested"] = 1 }, calls);

        calls.Clear();
        await Page(Props(), c => c.AsPartial("optional"));
        Assert.Equal(new Dictionary<string, int> { ["optional"] = 1 }, calls);

        calls.Clear();
        await Page(Props(), c => c.AsPartial(except: "lazy,nested,optional,deferred"));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task A_loader_returning_a_prop_is_unwrapped_once()
    {
        var page = await Page(new InertiaProps
        {
            ["merged"] = Inertia.Prop(() => Inertia.Merge(new[] { 1 })),
            ["deferred"] = Inertia.Prop(() => Inertia.Defer(() => 2, "late")),
            ["always"] = Inertia.Prop(() => Inertia.Always(3)),
        });

        JsonAssert.Equal("""{"errors":{},"merged":[1],"always":3}""", page["props"]);
        JsonAssert.Equal("""["merged"]""", page["mergeProps"]);
        JsonAssert.Equal("""{"late":["deferred"]}""", page["deferredProps"]);
    }

    [Fact]
    public async Task A_prop_nested_twice_is_an_error()
    {
        var context = _harness.Context();
        var result = Inertia.Render("TestComponent", new InertiaProps { ["x"] = Inertia.Prop(() => Inertia.Prop(() => Inertia.Prop(() => 1))) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => result.ExecuteAsync(context));
    }

    [Fact]
    public async Task Children_of_resolved_values_bypass_partial_filtering_but_literal_children_do_not()
    {
        InertiaProps Props() => new()
        {
            ["resolved"] = Inertia.Prop(() => new InertiaProps { ["user"] = "a", ["token"] = "b" }),
            ["literal"] = new InertiaProps { ["user"] = "a", ["token"] = "b" },
        };

        var page = await Page(Props(), c => c.AsPartial("resolved.user,literal.user"));
        JsonAssert.Equal("""{"errors":{},"resolved":{"user":"a","token":"b"},"literal":{"user":"a"}}""", page["props"]);
    }

    [Fact]
    public async Task Except_beats_only_and_always_bypasses_both()
    {
        var page = await Page(
            new InertiaProps { ["a"] = 1, ["b"] = 2, ["c"] = Inertia.Always(3) },
            c => c.AsPartial("a,b", "b,c"));
        JsonAssert.Equal("""{"errors":{},"a":1,"c":3}""", page["props"]);
    }

    [Fact]
    public async Task Prefix_matching_respects_the_dot_boundary()
    {
        var page = await Page(
            new InertiaProps { ["user"] = 1, ["users"] = 2, ["user2"] = new InertiaProps { ["x"] = 3 } },
            c => c.AsPartial("user"));
        JsonAssert.Equal("""{"errors":{},"user":1}""", page["props"]);
    }

    [Fact]
    public async Task Merge_metadata_at_root_and_nested_paths()
    {
        var page = await Page(new InertiaProps
        {
            ["append"] = Inertia.Merge(new[] { 1 }),
            ["prepend"] = Inertia.Merge(new[] { 1 }).Prepend(),
            ["deep"] = Inertia.DeepMerge(new InertiaProps { ["a"] = 1 }),
            ["paths"] = Inertia.Merge(new InertiaProps { ["a"] = new[] { 1 }, ["b"] = new[] { 1 } }).Append("a", "id").Prepend("b"),
            ["matched"] = Inertia.Merge(new[] { 1 }).Append(matchOn: "uuid"),
            ["nested"] = new InertiaProps { ["inner"] = Inertia.Merge(new[] { 1 }).Prepend() },
        });

        JsonAssert.Equal("""["append","paths.a","matched"]""", page["mergeProps"]);
        JsonAssert.Equal("""["prepend","paths.b","nested.inner"]""", page["prependProps"]);
        JsonAssert.Equal("""["deep"]""", page["deepMergeProps"]);
        JsonAssert.Equal("""["paths.a.id","matched.uuid"]""", page["matchPropsOn"]);
    }

    [Fact]
    public async Task Partial_reload_only_reports_metadata_for_requested_paths()
    {
        var page = await Page(
            new InertiaProps
            {
                ["feed"] = new InertiaProps { ["posts"] = Inertia.Merge(new[] { 1 }), ["locale"] = Inertia.Once(() => "en") },
                ["other"] = Inertia.Merge(new[] { 2 }),
            },
            c => c.AsPartial("feed.posts"));

        JsonAssert.Equal("""["feed.posts"]""", page["mergeProps"]);
        JsonAssert.Missing(page, "onceProps");
    }

    [Fact]
    public async Task Scroll_prop_uses_the_merge_intent_header()
    {
        InertiaProps Props() => new() { ["feed"] = Inertia.Scroll(new InertiaProps { ["items"] = new[] { 1 } }, "items", ScrollMetadata.FromPage(2, true)) };

        var append = await Page(Props(), c => c.AsInertia());
        JsonAssert.Equal("""["feed.items"]""", append["mergeProps"]);
        JsonAssert.Equal("""{"feed":{"pageName":"page","previousPage":1,"nextPage":3,"currentPage":2,"reset":false}}""", append["scrollProps"]);

        var prepend = await Page(Props(), c => c.AsInertia().WithHeader(InertiaHeaders.InfiniteScrollMergeIntent, "prepend"));
        JsonAssert.Missing(prepend, "mergeProps");
        JsonAssert.Equal("""["feed.items"]""", prepend["prependProps"]);
    }

    [Fact]
    public async Task Scroll_metadata_comes_from_a_value_that_provides_it()
    {
        var users = new PagedUsers([new User("a")], 1, HasMore: false);
        var constant = await Page(new InertiaProps { ["users"] = Inertia.Scroll(users) });
        var lazy = await Page(new InertiaProps { ["users"] = Inertia.Scroll(() => users) });

        foreach (var page in new[] { constant, lazy })
        {
            JsonAssert.Equal("""{"users":{"pageName":"page","previousPage":null,"nextPage":null,"currentPage":1,"reset":false}}""", page["scrollProps"]);
            JsonAssert.Equal("""["users.data"]""", page["mergeProps"]);
        }
    }

    [Fact]
    public async Task Scroll_without_metadata_throws()
    {
        var result = Inertia.Render("TestComponent", new InertiaProps { ["users"] = Inertia.Scroll(new[] { 1 }) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => result.ExecuteAsync(_harness.Context()));
    }

    [Fact]
    public async Task Scroll_reset_suppresses_merge_metadata_and_flags_reset()
    {
        var page = await Page(
            new InertiaProps { ["feed"] = Inertia.Scroll(new InertiaProps { ["data"] = new[] { 1 } }, metadata: ScrollMetadata.FromPage(1, true)) },
            c => c.AsPartial("feed").WithHeader(InertiaHeaders.Reset, "feed"));

        JsonAssert.Missing(page, "mergeProps");
        Assert.True((bool)page["scrollProps"]!["feed"]!["reset"]!);
    }

    // Deliberate deviation 2: a deferred scroll prop reports "{path}.{wrapper}" (Laravel 3.x reports "{path}").
    [Fact]
    public async Task Deferred_scroll_prop_reports_the_wrapper_merge_path()
    {
        InertiaProps Props() => new() { ["posts"] = Inertia.Scroll(() => new PagedUsers([new User("a")], 1, true)).Defer("feed") };

        var initial = await Page(Props());
        JsonAssert.Equal("""{"errors":{}}""", initial["props"]);
        JsonAssert.Equal("""{"feed":["posts"]}""", initial["deferredProps"]);
        JsonAssert.Equal("""["posts.data"]""", initial["mergeProps"]);
        JsonAssert.Missing(initial, "scrollProps");

        var initialPrepend = await Page(Props(), c => c.WithHeader(InertiaHeaders.InfiniteScrollMergeIntent, "prepend"));
        JsonAssert.Equal("""["posts.data"]""", initialPrepend["prependProps"]);

        var loaded = await Page(Props(), c => c.AsPartial("posts"));
        JsonAssert.Equal("""["posts.data"]""", loaded["mergeProps"]);
        Assert.Equal(2, (int)loaded["scrollProps"]!["posts"]!["nextPage"]!);
        Assert.Equal("a", (string?)loaded["props"]!["posts"]!["data"]![0]!["name"]);
    }

    [Fact]
    public async Task Defer_once_merge_combination()
    {
        InertiaProps Props() => new() { ["feed"] = Inertia.Defer(() => new[] { 1 }, "side").Once(until: TimeSpan.FromSeconds(1)).Append(matchOn: "id") };

        var first = await Page(Props(), c => c.AsInertia());
        JsonAssert.Equal("""{"side":["feed"]}""", first["deferredProps"]);
        JsonAssert.Equal("""["feed"]""", first["mergeProps"]);
        JsonAssert.Equal("""["feed.id"]""", first["matchPropsOn"]);
        Assert.Equal("feed", (string?)first["onceProps"]!["feed"]!["prop"]);

        var loaded = await Page(Props(), c => c.AsInertia().WithHeader(InertiaHeaders.ExceptOnceProps, "feed"));
        JsonAssert.Missing(loaded, "deferredProps");
        JsonAssert.Equal("""["feed"]""", loaded["mergeProps"]);
        Assert.NotNull(loaded["onceProps"]!["feed"]);

        var fetched = await Page(Props(), c => c.AsPartial("feed"));
        JsonAssert.Equal("[1]", fetched["props"]!["feed"]);
        JsonAssert.Equal("""["feed"]""", fetched["mergeProps"]);
        JsonAssert.Missing(fetched, "deferredProps");
    }

    [Fact]
    public async Task Once_with_custom_key_is_matched_by_key_not_path()
    {
        InertiaProps Props() => new() { ["config"] = new InertiaProps { ["locale"] = Inertia.Once(() => "en").As("locale-key") } };

        var byPath = await Page(Props(), c => c.AsInertia().WithHeader(InertiaHeaders.ExceptOnceProps, "config.locale"));
        Assert.Equal("en", (string?)byPath["props"]!["config"]!["locale"]);

        var byKey = await Page(Props(), c => c.AsInertia().WithHeader(InertiaHeaders.ExceptOnceProps, "locale-key"));
        JsonAssert.Equal("{}", byKey["props"]!["config"]);
        JsonAssert.Equal("""{"locale-key":{"prop":"config.locale","expiresAt":null}}""", byKey["onceProps"]);
    }

    [Fact]
    public async Task Initial_html_load_ignores_the_except_once_header()
    {
        var page = await Page(new InertiaProps { ["foo"] = Inertia.Once(() => "bar") }, c => c.WithHeader(InertiaHeaders.ExceptOnceProps, "foo"));
        Assert.Equal("bar", (string?)page["props"]!["foo"]);
    }

    [Fact]
    public async Task Rescued_prop_is_logged_and_reported()
    {
        var page = await Page(
            new InertiaProps { ["stats"] = Inertia.Prop<int>(() => throw new InvalidOperationException("boom")).Rescue(), ["ok"] = 1 },
            c => c.AsInertia());

        JsonAssert.Equal("""{"errors":{},"ok":1}""", page["props"]);
        JsonAssert.Equal("""["stats"]""", page["rescuedProps"]);
        var entry = Assert.Single(_harness.Logs.Entries, e => e.Exception is InvalidOperationException { Message: "boom" });
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level);
        Assert.Contains("stats", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_rescued_exception_propagates_and_nothing_is_written()
    {
        var context = _harness.Context().AsInertia();
        var result = Inertia.Render("TestComponent", new InertiaProps
        {
            ["big"] = new string('x', 100_000),
            ["stats"] = Inertia.Prop<int>(() => throw new InvalidOperationException("boom")),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => result.ExecuteAsync(context));
        Assert.Equal(0, context.Response.Body.Length);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey(InertiaHeaders.Inertia));
    }

    [Fact]
    public async Task Loaders_receive_the_request_aborted_token()
    {
        using var cts = new CancellationTokenSource();
        var seen = new List<CancellationToken>();
        var context = _harness.Context();
        context.RequestAborted = cts.Token;

        await Harness.ExecuteAsync(context, Inertia.Render("TestComponent", new InertiaProps
        {
            ["a"] = Inertia.Prop(ct => { seen.Add(ct); return ValueTask.FromResult(1); }),
            ["b"] = Inertia.Always(ct => { seen.Add(ct); return ValueTask.FromResult(2); }),
        }));

        Assert.Equal([cts.Token, cts.Token], seen);
    }

    [Fact]
    public async Task Cancellation_is_not_rescued()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var context = _harness.Context();
        context.RequestAborted = cts.Token;

        var result = Inertia.Render("TestComponent", new InertiaProps
        {
            ["a"] = Inertia.Prop(async ct => { await Task.Delay(1000, ct); return 1; }).Rescue(),
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.ExecuteAsync(context));
    }

    [Fact]
    public async Task Loaders_run_sequentially()
    {
        var running = 0;
        var maxRunning = 0;
        async ValueTask<int> Load(CancellationToken ct)
        {
            maxRunning = Math.Max(maxRunning, Interlocked.Increment(ref running));
            await Task.Yield();
            Interlocked.Decrement(ref running);
            return 1;
        }

        await Page(new InertiaProps { ["a"] = Inertia.Prop(Load), ["b"] = Inertia.Prop(Load), ["c"] = Inertia.Prop(Load) });
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Lists_of_objects_with_props_are_walked_with_index_paths()
    {
        var rows = new List<Row> { new("a", Inertia.Defer(() => 1)), new("b", null) };
        var page = await Page(new InertiaProps { ["rows"] = rows });

        JsonAssert.Equal("""[{"name":"a"},{"name":"b","extra":null}]""", page["props"]!["rows"]);
        JsonAssert.Equal("""{"default":["rows.0.extra"]}""", page["deferredProps"]);
    }

    // Laravel walks every array; here a prop held in an object-typed slot (object[], List<object>, an object property) used to be
    // handed to System.Text.Json, which silently wrote "{}".
    [Fact]
    public async Task Props_in_object_typed_lists_and_properties_are_walked()
    {
        InertiaProps Props() => new()
        {
            ["array"] = new object[] { new InertiaProps { ["name"] = "a", ["bar"] = Inertia.Optional(() => 1) } },
            ["list"] = new List<object?> { 1, new InertiaProps { ["bar"] = Inertia.Defer(() => 2) } },
            ["boxed"] = new Boxed("b", Inertia.Defer(() => 3)),
        };

        var initial = await Page(Props());
        JsonAssert.Equal("""{"errors":{},"array":[{"name":"a"}],"list":[1,{}],"boxed":{"name":"b"}}""", initial["props"]);
        JsonAssert.Equal("""{"default":["list.1.bar","boxed.value"]}""", initial["deferredProps"]);

        var partial = await Page(Props(), c => c.AsPartial("array,list,boxed"));
        JsonAssert.Equal("""{"errors":{},"array":[{"name":"a","bar":1}],"list":[1,{"bar":2}],"boxed":{"name":"b","value":3}}""", partial["props"]);
    }

    [Fact]
    public void Serializing_a_prop_through_an_object_slot_throws_instead_of_writing_an_empty_object()
    {
        var value = new Dictionary<int, object> { [1] = Inertia.Prop(() => 1) };
        var ex = Assert.Throws<InvalidOperationException>(() => System.Text.Json.JsonSerializer.Serialize(value, _harness.Services.GetRequiredService<InertiaPageWriter>().SerializerOptions));
        Assert.Equal("InertiaProp values can only be serialized by Inertia.Net", ex.Message);
    }

    [Fact]
    public async Task Typed_root_props_use_the_naming_policy_for_keys_and_metadata_paths()
    {
        var page = await Page(new DashboardPage("Home", Inertia.Merge(new[] { 1 }), new Sidebar("Menu", Inertia.Once(() => 2))));

        JsonAssert.Equal("""{"errors":{},"pageTitle":"Home","stats":[1],"sidebar":{"label":"Menu","notifications":2}}""", page["props"]);
        JsonAssert.Equal("""["stats"]""", page["mergeProps"]);
        JsonAssert.Equal("""{"sidebar.notifications":{"prop":"sidebar.notifications","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Plain_objects_are_serialized_with_the_app_json_options()
    {
        var page = await Page(new InertiaProps { ["user"] = new User("Ann", "a@b.c"), ["users"] = new List<User> { new("Bo") } });
        JsonAssert.Equal("""{"name":"Ann","email":"a@b.c"}""", page["props"]!["user"]);
        JsonAssert.Equal("""[{"name":"Bo","email":null}]""", page["props"]!["users"]);
    }

    [Fact]
    public async Task Walked_objects_respect_json_ignore_and_property_names()
    {
        var page = await Page(new InertiaProps
        {
            ["a"] = new IgnoredMembers("n", "secret", null, "r", Inertia.Prop(() => 1)),
            ["b"] = new IgnoredMembers("n", "secret", "here", "r", Inertia.Defer(() => 1)),
        });

        JsonAssert.Equal("""{"name":"n","custom_name":"r","lazy":1}""", page["props"]!["a"]);
        JsonAssert.Equal("""{"name":"n","maybe":"here","custom_name":"r"}""", page["props"]!["b"]);
        JsonAssert.Equal("""{"default":["b.lazy"]}""", page["deferredProps"]);
    }

    [Fact]
    public void Serializing_a_prop_outside_inertia_throws()
    {
        var page = new DashboardPage("x", Inertia.Defer(() => 1), new Sidebar("y", Inertia.Defer(() => 2)));
        var ex = Assert.Throws<InvalidOperationException>(() => System.Text.Json.JsonSerializer.Serialize(page, TestJsonContext.Default.DashboardPage));
        Assert.Equal("InertiaProp values can only be serialized by Inertia.Net", ex.Message);
    }

    [Fact]
    public void Factories_set_the_expected_flags()
    {
        Assert.Equal(PropLoad.Optional, Inertia.Optional(() => 1).Load);
        Assert.Equal("g", Inertia.Defer(() => 1, "g").Group);
        Assert.Equal("h", Inertia.Defer(() => 1).WithGroup("h").Group);
        Assert.True(Inertia.Always(1).IsAlways);
        Assert.True(Inertia.Merge(1).IsMerge);
        Assert.True(Inertia.DeepMerge(() => 1).IsDeepMerge);
        Assert.True(Inertia.Prop(() => 1).As("k").IsOnce);
        Assert.Equal(TimeSpan.FromHours(1), Inertia.Prop(() => 1).Until(TimeSpan.FromHours(1)).OnceTtl);
        Assert.True(Inertia.Once(() => 1).Fresh().OnceFresh);
        Assert.False(Inertia.Once(() => 1).Fresh().Fresh(false).OnceFresh);
        Assert.True(Inertia.Defer(() => 1).Rescue().ShouldRescue);
        Assert.Equal(["a", "b"], Inertia.Merge(1).MatchOn("x").MatchOn("a", "b").MatchOnKeys!);
        var scroll = Inertia.Scroll(1, "items");
        Assert.True(scroll.IsMerge);
        Assert.Equal("items", scroll.ScrollWrapper);
        Assert.Equal(PropLoad.Deferred, scroll.Defer().Load);
    }

    [Fact]
    public void Scroll_metadata_from_page()
    {
        Assert.Equal(new ScrollMetadata("page", null, 2, 1), ScrollMetadata.FromPage(1, hasMore: true));
        Assert.Equal(new ScrollMetadata("p", 2, null, 3), ScrollMetadata.FromPage(3, hasMore: false, "p"));
    }
}
