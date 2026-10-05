using System.Text.Json.Nodes;

namespace Inertia.Net.Tests;

/// <summary>Port of inertia-laravel 3.x tests/PropsResolverTest.php.</summary>
public class PropsResolverTests
{
    private readonly Harness _harness = new(o => o.Version = "123");

    private Task<JsonObject> Page(object props, Action<Microsoft.AspNetCore.Http.HttpContext>? arrange = null) => _harness.PageAsync(props, arrange);

    private static ScrollMetadata Metadata => new("page", null, 2, 1);

    [Fact]
    public async Task Nested_closure_is_resolved()
    {
        var page = await Page(new InertiaProps { ["auth"] = Inertia.Prop(() => new InertiaProps { ["user"] = "Jonathan" }) });
        Assert.Equal("Jonathan", (string?)page["props"]!["auth"]!["user"]);
    }

    [Fact]
    public async Task Nested_closure_inside_array_is_resolved()
    {
        var page = await Page(new InertiaProps { ["auth"] = new InertiaProps { ["user"] = Inertia.Prop(() => "Jonathan") } });
        Assert.Equal("Jonathan", (string?)page["props"]!["auth"]!["user"]);
    }

    [Fact]
    public async Task Nested_always_prop_is_resolved()
    {
        var page = await Page(new InertiaProps { ["auth"] = new InertiaProps { ["user"] = Inertia.Always(() => "Jonathan") } });
        Assert.Equal("Jonathan", (string?)page["props"]!["auth"]!["user"]);
    }

    [Fact]
    public async Task Nested_merge_prop_is_resolved()
    {
        var page = await Page(new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Merge(new[] { new { id = 1 } }) } });
        JsonAssert.Equal("""[{"id":1}]""", page["props"]!["feed"]!["posts"]);
    }

    [Fact]
    public async Task Nested_once_prop_is_resolved_on_initial_load()
    {
        var page = await Page(new InertiaProps { ["config"] = new InertiaProps { ["locale"] = Inertia.Once(() => "en") } });
        Assert.Equal("en", (string?)page["props"]!["config"]!["locale"]);
    }

    [Fact]
    public async Task Nested_optional_prop_is_excluded_from_initial_load()
    {
        var resolved = false;
        var page = await Page(new InertiaProps
        {
            ["auth"] = new InertiaProps
            {
                ["user"] = "Jonathan",
                ["permissions"] = Inertia.Optional(() =>
                {
                    resolved = true;
                    return new[] { "admin" };
                }),
            },
        });

        Assert.Equal("Jonathan", (string?)page["props"]!["auth"]!["user"]);
        JsonAssert.Missing(page["props"]!["auth"], "permissions");
        Assert.False(resolved);
    }

    [Fact]
    public async Task Nested_defer_prop_is_excluded_from_initial_load()
    {
        var resolved = false;
        var page = await Page(new InertiaProps
        {
            ["auth"] = new InertiaProps
            {
                ["user"] = "Jonathan",
                ["notifications"] = Inertia.Defer(() =>
                {
                    resolved = true;
                    return Array.Empty<string>();
                }),
            },
        });

        JsonAssert.Missing(page["props"]!["auth"], "notifications");
        Assert.False(resolved);
    }

    [Fact]
    public async Task Rescued_defer_prop_is_omitted_and_reported_on_partial_request()
    {
        var page = await Page(
            new InertiaProps { ["auth"] = new InertiaProps { ["notifications"] = Inertia.Defer<string>(() => throw new InvalidOperationException("Rescue this deferred prop")).Rescue() } },
            c => c.AsPartial("auth.notifications"));

        JsonAssert.Missing(page["props"]!["auth"], "notifications");
        JsonAssert.Equal("""["auth.notifications"]""", page["rescuedProps"]);
    }

    [Fact]
    public async Task Excluded_props_are_not_resolved_on_initial_load()
    {
        var optionalResolved = false;
        var deferResolved = false;
        var page = await Page(new InertiaProps
        {
            ["name"] = "Jonathan",
            ["permissions"] = Inertia.Optional(() => optionalResolved = true),
            ["notifications"] = Inertia.Defer(() => deferResolved = true),
        });

        Assert.Equal("Jonathan", (string?)page["props"]!["name"]);
        JsonAssert.Missing(page["props"], "permissions");
        JsonAssert.Missing(page["props"], "notifications");
        Assert.False(optionalResolved);
        Assert.False(deferResolved);
    }

    [Fact]
    public async Task Closure_returning_optional_prop_is_excluded_from_initial_load()
    {
        var resolved = false;
        var page = await Page(new InertiaProps
        {
            ["auth"] = Inertia.Prop(() => new InertiaProps
            {
                ["user"] = "Jonathan",
                ["permissions"] = Inertia.Optional(() => resolved = true),
            }),
        });

        Assert.Equal("Jonathan", (string?)page["props"]!["auth"]!["user"]);
        JsonAssert.Missing(page["props"]!["auth"], "permissions");
        Assert.False(resolved);
    }

    [Fact]
    public async Task Closure_returning_defer_prop_is_excluded_from_initial_load()
    {
        var resolved = false;
        var page = await Page(new InertiaProps
        {
            ["auth"] = Inertia.Prop(() => new InertiaProps
            {
                ["user"] = "Jonathan",
                ["notifications"] = Inertia.Defer(() => resolved = true),
            }),
        });

        JsonAssert.Missing(page["props"]!["auth"], "notifications");
        Assert.False(resolved);
    }

    [Fact]
    public async Task Closure_returning_merge_prop_resolves_with_metadata()
    {
        var page = await Page(new InertiaProps { ["posts"] = Inertia.Prop(() => Inertia.Merge(new[] { 1 })) });
        JsonAssert.Equal("[1]", page["props"]!["posts"]);
        JsonAssert.Equal("""["posts"]""", page["mergeProps"]);
    }

    [Fact]
    public async Task Closure_returning_once_prop_resolves_with_metadata()
    {
        var page = await Page(new InertiaProps { ["locale"] = Inertia.Prop(() => Inertia.Once(() => "en")) });
        Assert.Equal("en", (string?)page["props"]!["locale"]);
        JsonAssert.Equal("""{"locale":{"prop":"locale","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Closure_returning_defer_prop_collects_deferred_and_merge_metadata()
    {
        var page = await Page(new InertiaProps { ["posts"] = Inertia.Prop(() => Inertia.Defer(() => new[] { 1 }).Append()) });
        JsonAssert.Missing(page["props"], "posts");
        JsonAssert.Equal("""{"default":["posts"]}""", page["deferredProps"]);
        JsonAssert.Equal("""["posts"]""", page["mergeProps"]);
    }

    [Fact]
    public async Task Nested_optional_prop_is_included_on_partial_request()
    {
        var page = await Page(
            new InertiaProps { ["auth"] = new InertiaProps { ["user"] = "Jonathan", ["permissions"] = Inertia.Optional(() => new[] { "admin" }) } },
            c => c.AsPartial("auth.permissions"));
        JsonAssert.Equal("""{"permissions":["admin"]}""", page["props"]!["auth"]);
    }

    [Fact]
    public async Task Nested_defer_prop_is_included_on_partial_request()
    {
        var page = await Page(
            new InertiaProps { ["auth"] = new InertiaProps { ["user"] = "Jonathan", ["notifications"] = Inertia.Defer(() => new[] { "new message" }) } },
            c => c.AsPartial("auth.notifications"));
        JsonAssert.Equal("""["new message"]""", page["props"]!["auth"]!["notifications"]);
    }

    [Fact]
    public async Task Nested_always_prop_is_included_on_partial_request()
    {
        var page = await Page(
            new InertiaProps { ["auth"] = new InertiaProps { ["user"] = "Jonathan", ["errors"] = Inertia.Always(() => new InertiaProps { ["name"] = "required" }) } },
            c => c.AsPartial("auth.user"));
        JsonAssert.Equal("""{"user":"Jonathan","errors":{"name":"required"}}""", page["props"]!["auth"]);
    }

    [Fact]
    public async Task Top_level_always_prop_is_included_when_not_requested()
    {
        var page = await Page(
            new InertiaProps { ["other"] = "value", ["always"] = Inertia.Always(() => new InertiaProps { ["name"] = "required" }) },
            c => c.AsPartial("other"));
        JsonAssert.Equal("""{"errors":{},"other":"value","always":{"name":"required"}}""", page["props"]);
    }

    [Fact]
    public async Task Nested_merge_prop_is_included_on_partial_request()
    {
        var page = await Page(new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Merge(new[] { 1 }) } }, c => c.AsPartial("feed.posts"));
        JsonAssert.Equal("[1]", page["props"]!["feed"]!["posts"]);
    }

    [Fact]
    public async Task Nested_prop_is_excluded_via_except_header()
    {
        var page = await Page(
            new InertiaProps { ["auth"] = new InertiaProps { ["user"] = "Jonathan", ["token"] = "secret" } },
            c => c.AsPartial("auth", "auth.token"));
        JsonAssert.Equal("""{"user":"Jonathan"}""", page["props"]!["auth"]);
    }

    [Fact]
    public async Task Partial_request_for_parent_resolves_all_nested_prop_types()
    {
        var page = await Page(
            new InertiaProps
            {
                ["dashboard"] = new InertiaProps
                {
                    ["stats"] = "visible",
                    ["feed"] = Inertia.Merge(new[] { 1 }),
                    ["notifications"] = Inertia.Defer(() => new[] { "msg" }),
                    ["settings"] = Inertia.Optional(() => new InertiaProps { ["theme"] = "dark" }),
                    ["locale"] = Inertia.Once(() => "en"),
                },
            },
            c => c.AsPartial("dashboard"));

        JsonAssert.Equal("""{"stats":"visible","feed":[1],"notifications":["msg"],"settings":{"theme":"dark"},"locale":"en"}""", page["props"]!["dashboard"]);
        JsonAssert.Equal("""["dashboard.feed"]""", page["mergeProps"]);
        JsonAssert.Equal("""{"dashboard.locale":{"prop":"dashboard.locale","expiresAt":null}}""", page["onceProps"]);
        JsonAssert.Missing(page, "deferredProps");
    }

    [Fact]
    public async Task Nested_defer_prop_metadata_is_collected()
    {
        var page = await Page(new InertiaProps { ["auth"] = new InertiaProps { ["user"] = "Jonathan", ["notifications"] = Inertia.Defer(() => 1) } });
        JsonAssert.Equal("""{"default":["auth.notifications"]}""", page["deferredProps"]);
    }

    [Fact]
    public async Task Nested_defer_prop_metadata_preserves_group()
    {
        var page = await Page(new InertiaProps
        {
            ["auth"] = new InertiaProps { ["notifications"] = Inertia.Defer(() => 1, "sidebar"), ["messages"] = Inertia.Defer(() => 1, "sidebar") },
        });
        JsonAssert.Equal("""{"sidebar":["auth.notifications","auth.messages"]}""", page["deferredProps"]);
    }

    [Fact]
    public async Task Closure_returning_defer_prop_metadata_is_collected()
    {
        var page = await Page(new InertiaProps
        {
            ["auth"] = Inertia.Prop(() => new InertiaProps { ["user"] = "Jonathan", ["notifications"] = Inertia.Defer(() => 1, "alerts") }),
        });
        JsonAssert.Equal("""{"alerts":["auth.notifications"]}""", page["deferredProps"]);
    }

    [Fact]
    public async Task Nested_merge_metadata_variants_are_collected()
    {
        var page = await Page(new InertiaProps
        {
            ["feed"] = new InertiaProps
            {
                ["posts"] = Inertia.Merge(new[] { 1 }),
                ["older"] = Inertia.Merge(new[] { 1 }).Prepend(),
                ["paged"] = Inertia.Merge(new InertiaProps { ["data"] = new[] { 1 } }).Append("data"),
                ["matched"] = Inertia.Merge(new[] { 1 }).MatchOn("id").DeepMerge(),
            },
            ["settings"] = new InertiaProps { ["preferences"] = Inertia.Merge(new InertiaProps { ["theme"] = "dark" }).DeepMerge() },
        });

        JsonAssert.Equal("""["feed.posts","feed.paged.data"]""", page["mergeProps"]);
        JsonAssert.Equal("""["feed.older"]""", page["prependProps"]);
        JsonAssert.Equal("""["feed.matched","settings.preferences"]""", page["deepMergeProps"]);
        JsonAssert.Equal("""["feed.matched.id"]""", page["matchPropsOn"]);
    }

    [Fact]
    public async Task Nested_defer_with_merge_metadata_is_collected()
    {
        var page = await Page(new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Defer(() => new[] { 1 }).Append() } });
        JsonAssert.Equal("""{"default":["feed.posts"]}""", page["deferredProps"]);
        JsonAssert.Equal("""["feed.posts"]""", page["mergeProps"]);
    }

    [Theory]
    [InlineData("feed.posts")]
    [InlineData("feed")]
    public async Task Nested_merge_metadata_is_collected_on_partial_request(string only)
    {
        var page = await Page(new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Merge(new[] { 1 }) } }, c => c.AsPartial(only));
        JsonAssert.Equal("[1]", page["props"]!["feed"]!["posts"]);
        JsonAssert.Equal("""["feed.posts"]""", page["mergeProps"]);
    }

    [Fact]
    public async Task Nested_merge_prop_metadata_is_suppressed_by_reset_header()
    {
        var page = await Page(
            new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Merge(new[] { 1 }) } },
            c => c.AsPartial("feed.posts").WithHeader(InertiaHeaders.Reset, "feed.posts"));
        JsonAssert.Equal("[1]", page["props"]!["feed"]!["posts"]);
        JsonAssert.Missing(page, "mergeProps");
    }

    [Fact]
    public async Task Nested_once_prop_metadata_is_collected()
    {
        var page = await Page(new InertiaProps { ["config"] = new InertiaProps { ["locale"] = Inertia.Once(() => "en") } });
        JsonAssert.Equal("""{"config.locale":{"prop":"config.locale","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Nested_once_prop_with_custom_key_metadata_is_collected()
    {
        var page = await Page(new InertiaProps { ["config"] = new InertiaProps { ["locale"] = Inertia.Once(() => "en").As("app-locale") } });
        JsonAssert.Equal("""{"app-locale":{"prop":"config.locale","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Nested_once_prop_is_excluded_when_already_loaded()
    {
        var page = await Page(
            new InertiaProps { ["config"] = new InertiaProps { ["locale"] = Inertia.Once(() => "en"), ["timezone"] = "UTC" } },
            c => c.AsInertia().WithHeader(InertiaHeaders.ExceptOnceProps, "config.locale"));
        JsonAssert.Equal("""{"timezone":"UTC"}""", page["props"]!["config"]);
        JsonAssert.Equal("""{"config.locale":{"prop":"config.locale","expiresAt":null}}""", page["onceProps"]);
    }

    [Theory]
    [InlineData("config.locale")]
    [InlineData("config")]
    public async Task Nested_once_metadata_is_collected_on_partial_request(string only)
    {
        var page = await Page(new InertiaProps { ["config"] = new InertiaProps { ["locale"] = Inertia.Once(() => "en") } }, c => c.AsPartial(only));
        Assert.Equal("en", (string?)page["props"]!["config"]!["locale"]);
        JsonAssert.Equal("""{"config.locale":{"prop":"config.locale","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Nested_scroll_prop_metadata_is_collected()
    {
        var page = await Page(new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Scroll(new InertiaProps { ["data"] = new[] { 1 } }, "data", Metadata) } });
        JsonAssert.Equal("""{"feed.posts":{"pageName":"page","previousPage":null,"nextPage":2,"currentPage":1,"reset":false}}""", page["scrollProps"]);
    }

    [Fact]
    public async Task Nested_deferred_scroll_prop_is_excluded_from_initial_load()
    {
        var page = await Page(new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Scroll(new InertiaProps { ["data"] = new[] { 1 } }, "data", Metadata).Defer() } });
        JsonAssert.Equal("{}", page["props"]!["feed"]);
        JsonAssert.Equal("""{"default":["feed.posts"]}""", page["deferredProps"]);
    }

    [Fact]
    public async Task Nested_scroll_prop_is_included_on_partial_request()
    {
        var page = await Page(
            new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Scroll(new InertiaProps { ["data"] = new[] { 1 } }, "data", Metadata) } },
            c => c.AsPartial("feed.posts"));
        JsonAssert.Equal("""{"data":[1]}""", page["props"]!["feed"]!["posts"]);
        JsonAssert.Equal("""{"feed.posts":{"pageName":"page","previousPage":null,"nextPage":2,"currentPage":1,"reset":false}}""", page["scrollProps"]);
    }

    [Fact]
    public async Task Nested_scroll_prop_reset_flag_is_set_by_reset_header()
    {
        var page = await Page(
            new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Scroll(new InertiaProps { ["data"] = new[] { 1 } }, "data", Metadata) } },
            c => c.WithHeader(InertiaHeaders.Reset, "feed.posts"));
        Assert.True((bool)page["scrollProps"]!["feed.posts"]!["reset"]!);
    }

    [Fact]
    public async Task Nested_defer_once_prop_suppresses_deferred_metadata_when_already_loaded()
    {
        var page = await Page(
            new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Defer(() => 1).Once() } },
            c => c.WithHeader(InertiaHeaders.ExceptOnceProps, "feed.posts"));
        JsonAssert.Missing(page, "deferredProps");
    }

    [Fact]
    public async Task Nested_defer_once_prop_includes_deferred_metadata_on_first_load()
    {
        var page = await Page(new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Defer(() => 1).Once() } });
        JsonAssert.Equal("""{"default":["feed.posts"]}""", page["deferredProps"]);
        JsonAssert.Equal("""{"feed.posts":{"prop":"feed.posts","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Nested_props_on_non_partial_inertia_request_behave_like_initial_load()
    {
        var page = await Page(
            new InertiaProps
            {
                ["dashboard"] = new InertiaProps
                {
                    ["stats"] = "visible",
                    ["feed"] = Inertia.Merge(new[] { 1 }),
                    ["notifications"] = Inertia.Defer(() => 1),
                    ["settings"] = Inertia.Optional(() => 1),
                },
            },
            c => c.AsInertia());

        JsonAssert.Equal("""{"stats":"visible","feed":[1]}""", page["props"]!["dashboard"]);
        JsonAssert.Equal("""["dashboard.feed"]""", page["mergeProps"]);
        JsonAssert.Equal("""{"default":["dashboard.notifications"]}""", page["deferredProps"]);
    }

    [Fact]
    public async Task Except_header_suppresses_nested_merge_metadata()
    {
        var page = await Page(
            new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Merge(new[] { 1 }), ["comments"] = Inertia.Merge(new[] { 2 }) } },
            c => c.AsPartial("feed.posts,feed.comments", "feed.posts"));
        JsonAssert.Equal("""{"comments":[2]}""", page["props"]!["feed"]);
        JsonAssert.Equal("""["feed.comments"]""", page["mergeProps"]);
    }

    [Fact]
    public async Task Except_header_for_parent_suppresses_all_nested_metadata()
    {
        var page = await Page(
            new InertiaProps { ["feed"] = new InertiaProps { ["posts"] = Inertia.Merge(new[] { 1 }) }, ["other"] = "value" },
            c => c.AsPartial("feed,other", "feed"));
        JsonAssert.Equal("""{"errors":{},"other":"value"}""", page["props"]);
        JsonAssert.Missing(page, "mergeProps");
    }

    [Fact]
    public async Task Deeply_nested_props_use_full_paths()
    {
        var page = await Page(new InertiaProps
        {
            ["app"] = new InertiaProps
            {
                ["auth"] = new InertiaProps { ["notifications"] = Inertia.Defer(() => 1, "alerts") },
                ["feed"] = new InertiaProps { ["posts"] = Inertia.Merge(new[] { 1 }) },
            },
        });
        JsonAssert.Equal("{}", page["props"]!["app"]!["auth"]);
        JsonAssert.Equal("""{"alerts":["app.auth.notifications"]}""", page["deferredProps"]);
        JsonAssert.Equal("""["app.feed.posts"]""", page["mergeProps"]);
    }

    [Fact]
    public async Task Deeply_nested_optional_prop_is_included_on_partial_request()
    {
        var page = await Page(
            new InertiaProps { ["app"] = new InertiaProps { ["auth"] = new InertiaProps { ["permissions"] = Inertia.Optional(() => new[] { "admin" }) } } },
            c => c.AsPartial("app.auth.permissions"));
        JsonAssert.Equal("""["admin"]""", page["props"]!["app"]!["auth"]!["permissions"]);
    }

    [Fact]
    public async Task Multiple_nested_prop_types_are_handled_together()
    {
        var page = await Page(new InertiaProps
        {
            ["dashboard"] = new InertiaProps
            {
                ["stats"] = "visible",
                ["feed"] = Inertia.Merge(new[] { 1 }),
                ["notifications"] = Inertia.Defer(() => 1),
                ["settings"] = Inertia.Optional(() => 1),
                ["locale"] = Inertia.Once(() => "en"),
            },
        });

        JsonAssert.Equal("""{"stats":"visible","feed":[1],"locale":"en"}""", page["props"]!["dashboard"]);
        JsonAssert.Equal("""["dashboard.feed"]""", page["mergeProps"]);
        JsonAssert.Equal("""{"default":["dashboard.notifications"]}""", page["deferredProps"]);
        JsonAssert.Equal("""{"dashboard.locale":{"prop":"dashboard.locale","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Deferred_props_at_mixed_depths_collect_metadata_and_resolve_on_partial_request()
    {
        InertiaProps Props() => new()
        {
            ["foo"] = Inertia.Defer(() => "bar"),
            ["nested"] = new InertiaProps { ["a"] = "b", ["c"] = Inertia.Defer(() => "d") },
        };

        var initial = await Page(Props());
        JsonAssert.Equal("""{"errors":{},"nested":{"a":"b"}}""", initial["props"]);
        JsonAssert.Equal("""{"default":["foo","nested.c"]}""", initial["deferredProps"]);

        var partial = await Page(Props(), c => c.AsPartial("foo,nested.c"));
        JsonAssert.Equal("""{"errors":{},"foo":"bar","nested":{"c":"d"}}""", partial["props"]);
        JsonAssert.Missing(partial, "deferredProps");
    }

    [Fact]
    public async Task Dot_notation_prop_merges_into_existing_nested_structure()
    {
        var page = await Page(new InertiaProps
        {
            ["auth"] = new InertiaProps { ["user"] = new InertiaProps { ["name"] = "Jonathan Reinink", ["email"] = "jonathan@example.com" } },
            ["auth.user.permissions"] = Inertia.Prop(() => new[] { "edit-posts", "delete-posts" }),
        });

        JsonAssert.Equal(
            """{"errors":{},"auth":{"user":{"name":"Jonathan Reinink","email":"jonathan@example.com","permissions":["edit-posts","delete-posts"]}}}""",
            page["props"]);
    }

    [Fact]
    public async Task Dot_notation_prop_merges_when_parent_is_a_closure()
    {
        var page = await Page(new InertiaProps
        {
            ["auth"] = Inertia.Prop(() => new InertiaProps { ["user"] = new InertiaProps { ["name"] = "Jonathan Reinink" } }),
            ["auth.user.permissions"] = Inertia.Prop(() => new[] { "edit-posts" }),
        });

        JsonAssert.Equal("""{"user":{"name":"Jonathan Reinink","permissions":["edit-posts"]}}""", page["props"]!["auth"]);
    }

    [Fact]
    public async Task Dot_notation_optional_prop_is_excluded_on_initial_load_and_included_on_partial_request()
    {
        InertiaProps Props() => new()
        {
            ["auth"] = new InertiaProps { ["user"] = new InertiaProps { ["name"] = "Jonathan Reinink" } },
            ["auth.user.permissions"] = Inertia.Optional(() => new[] { "edit-posts" }),
        };

        var initial = await Page(Props());
        JsonAssert.Equal("""{"errors":{},"auth":{"user":{"name":"Jonathan Reinink"}}}""", initial["props"]);

        var partial = await Page(Props(), c => c.AsPartial("auth.user.permissions"));
        JsonAssert.Equal("""{"errors":{},"auth":{"user":{"permissions":["edit-posts"]}}}""", partial["props"]);
    }

    [Fact]
    public async Task Optional_props_inside_indexed_arrays_are_excluded_on_initial_load_and_resolved_on_partial_request()
    {
        var resolved = 0;
        InertiaProps Props() => new()
        {
            ["foos"] = new[]
            {
                new InertiaProps { ["name"] = "First", ["bar"] = Inertia.Optional(() => $"expensive-data-{++resolved}") },
                new InertiaProps { ["name"] = "Second", ["bar"] = Inertia.Optional(() => $"expensive-data-{++resolved}") },
            },
        };

        var initial = await Page(Props());
        JsonAssert.Equal("""[{"name":"First"},{"name":"Second"}]""", initial["props"]!["foos"]);
        Assert.Equal(0, resolved);

        var partial = await Page(Props(), c => c.AsPartial("foos"));
        JsonAssert.Equal("""[{"name":"First","bar":"expensive-data-1"},{"name":"Second","bar":"expensive-data-2"}]""", partial["props"]!["foos"]);
    }

    [Fact]
    public async Task Deferred_props_inside_closure_are_excluded_on_initial_load_and_resolved_on_partial_request()
    {
        var resolved = false;
        InertiaProps Props() => new()
        {
            ["auth"] = Inertia.Prop(() => new InertiaProps
            {
                ["user"] = new InertiaProps { ["name"] = "Jonathan Reinink" },
                ["notifications"] = Inertia.Defer(() => { resolved = true; return new[] { "You have a new follower" }; }),
                ["roles"] = Inertia.Defer(() => new[] { "admin" }),
            }),
        };

        var initial = await Page(Props());
        JsonAssert.Equal("""{"user":{"name":"Jonathan Reinink"}}""", initial["props"]!["auth"]);
        JsonAssert.Equal("""{"default":["auth.notifications","auth.roles"]}""", initial["deferredProps"]);
        Assert.False(resolved);

        var partial = await Page(Props(), c => c.AsPartial("auth.notifications,auth.roles"));
        JsonAssert.Equal("""["You have a new follower"]""", partial["props"]!["auth"]!["notifications"]);
        JsonAssert.Equal("""["admin"]""", partial["props"]!["auth"]!["roles"]);
    }

    // Laravel: test_prop_types_nested_in_json_serializable_props_are_resolved. Here: a typed POCO with prop members.
    [Fact]
    public async Task Prop_types_nested_in_typed_objects_are_resolved()
    {
        DashboardPage Props() => new("Home", Inertia.Defer(() => 42), new Sidebar("Menu", Inertia.Defer(() => new[] { "hi" }, "side")));

        var initial = await Page(Props());
        JsonAssert.Equal("""{"errors":{},"pageTitle":"Home","sidebar":{"label":"Menu"}}""", initial["props"]);
        JsonAssert.Equal("""{"default":["stats"],"side":["sidebar.notifications"]}""", initial["deferredProps"]);

        var partial = await Page(Props(), c => c.AsPartial("sidebar.notifications"));
        JsonAssert.Equal("""{"errors":{},"sidebar":{"notifications":["hi"]}}""", partial["props"]);
    }
}
