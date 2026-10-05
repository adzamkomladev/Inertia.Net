using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;

namespace Inertia.Net.Tests;

/// <summary>Port of inertia-laravel 3.x tests/ResponseTest.php.</summary>
public class ResponseTests
{
    private const string Script = """<script data-page="app" type="application/json">""";
    private const string Div = """</script><div id="app"></div>""";

    // Laravel builds these responses without middleware-shared props; errors is always present here, sharedProps is turned off.
    private readonly Harness _harness = new(o =>
    {
        o.Version = "123";
        o.ExposeSharedPropKeys = false;
    });

    private static InertiaProps UserProps(params (string Key, object? Value)[] extra)
    {
        var props = new InertiaProps { ["user"] = new InertiaProps { ["name"] = "Jonathan" } };
        foreach (var (key, value) in extra)
        {
            props[key] = value;
        }

        return props;
    }

    private async Task<string> Html(string component, object props, Action<HttpContext>? arrange = null)
    {
        var context = _harness.Context("/user/123");
        arrange?.Invoke(context);
        var body = await Harness.ExecuteAsync(context, Inertia.Render(component, props));
        var start = body.IndexOf(Script, StringComparison.Ordinal);
        return body[start..body.IndexOf("</body>", StringComparison.Ordinal)];
    }

    private Task<JsonObject> Json(object props, Action<HttpContext>? arrange = null, string component = "User/Edit", string url = "/user/123") =>
        _harness.PageAsync(props, c =>
        {
            c.AsInertia();
            arrange?.Invoke(c);
        }, component, url);

    [Fact]
    public async Task Server_response()
    {
        var html = await Html("User/Edit", UserProps());
        Assert.Equal(
            Script + """{"component":"User\u002FEdit","props":{"errors":{},"user":{"name":"Jonathan"}},"url":"\u002Fuser\u002F123","version":"123"}""" + Div,
            html);
    }

    [Fact]
    public async Task Server_response_with_deferred_prop()
    {
        var html = await Html("User/Edit", UserProps(("foo", Inertia.Defer(() => "bar", "default"))));
        Assert.Equal(
            Script + """{"component":"User\u002FEdit","props":{"errors":{},"user":{"name":"Jonathan"}},"url":"\u002Fuser\u002F123","version":"123","deferredProps":{"default":["foo"]}}""" + Div,
            html);
    }

    [Fact]
    public async Task Server_response_with_deferred_prop_and_multiple_groups()
    {
        var html = await Html("User/Edit", UserProps(
            ("foo", Inertia.Defer(() => "foo value")),
            ("bar", Inertia.Defer(() => "bar value")),
            ("baz", Inertia.Defer(() => "baz value", "custom"))));
        Assert.Contains(""","deferredProps":{"default":["foo","bar"],"custom":["baz"]}}""", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Server_response_with_scroll_props(bool reset)
    {
        var page = await _harness.PageAsync(
            new InertiaProps { ["users"] = Inertia.Scroll(new InertiaProps { ["data"] = new[] { new { id = 1 } } }, "data", new ScrollMetadata("page", null, 2, 1)) },
            c =>
            {
                if (reset)
                {
                    c.WithHeader(InertiaHeaders.Reset, "users");
                }
            },
            "User/Index");

        JsonAssert.Equal("""{"data":[{"id":1}]}""", page["props"]!["users"]);
        JsonAssert.Equal($$$"""{"users":{"pageName":"page","previousPage":null,"nextPage":2,"currentPage":1,"reset":{{{(reset ? "true" : "false")}}}}}""", page["scrollProps"]);
    }

    [Fact]
    public async Task Server_response_with_merge_props()
    {
        var html = await Html("User/Edit", UserProps(("foo", Inertia.Merge("foo value")), ("bar", Inertia.Merge("bar value"))));
        Assert.Equal(
            Script + """{"component":"User\u002FEdit","props":{"errors":{},"user":{"name":"Jonathan"},"foo":"foo value","bar":"bar value"},"url":"\u002Fuser\u002F123","version":"123","mergeProps":["foo","bar"]}""" + Div,
            html);
    }

    [Fact]
    public async Task Server_response_with_merge_props_that_should_prepend()
    {
        var html = await Html("User/Edit", UserProps(("foo", Inertia.Merge("foo value").Prepend()), ("bar", Inertia.Merge("bar value"))));
        Assert.Contains(""","mergeProps":["bar"],"prependProps":["foo"]}""", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_response_with_merge_props_that_have_nested_paths_to_append_and_prepend()
    {
        var page = await _harness.PageAsync(UserProps(
            ("foo", Inertia.Merge(new InertiaProps { ["data"] = new[] { 1, 2 } }).Append("data")),
            ("bar", Inertia.Merge(new InertiaProps { ["data"] = new InertiaProps { ["items"] = new[] { 1, 2 } } }).Prepend("data.items"))));

        JsonAssert.Equal("""["foo.data"]""", page["mergeProps"]);
        JsonAssert.Equal("""["bar.data.items"]""", page["prependProps"]);
        JsonAssert.Missing(page, "matchPropsOn");
    }

    [Fact]
    public async Task Server_response_with_merge_props_that_have_nested_paths_and_match_on_strategies()
    {
        var page = await _harness.PageAsync(UserProps(
            ("foo", Inertia.Merge(new InertiaProps { ["data"] = new[] { 1, 2 } }).Append("data", "id")),
            ("bar", Inertia.Merge(new InertiaProps { ["data"] = new InertiaProps { ["items"] = new[] { 1, 2 } } }).Prepend("data.items", "uuid"))));

        JsonAssert.Equal("""["foo.data"]""", page["mergeProps"]);
        JsonAssert.Equal("""["bar.data.items"]""", page["prependProps"]);
        JsonAssert.Equal("""["foo.data.id","bar.data.items.uuid"]""", page["matchPropsOn"]);
    }

    [Fact]
    public async Task Server_response_with_deep_merge_and_match_on_props()
    {
        var html = await Html("User/Edit", UserProps(("foo", Inertia.Merge("foo value").MatchOn("foo-key").DeepMerge()), ("bar", Inertia.DeepMerge("bar value").MatchOn("bar-key"))));
        Assert.Contains(""","deepMergeProps":["foo","bar"],"matchPropsOn":["foo.foo-key","bar.bar-key"]}""", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_response_with_defer_and_merge_props()
    {
        var html = await Html("User/Edit", UserProps(("foo", Inertia.Defer(() => "foo value").Append()), ("bar", Inertia.Merge("bar value"))));
        Assert.Equal(
            Script + """{"component":"User\u002FEdit","props":{"errors":{},"user":{"name":"Jonathan"},"bar":"bar value"},"url":"\u002Fuser\u002F123","version":"123","mergeProps":["foo","bar"],"deferredProps":{"default":["foo"]}}""" + Div,
            html);
    }

    [Fact]
    public async Task Server_response_with_defer_and_deep_merge_props()
    {
        var html = await Html("User/Edit", UserProps(("foo", Inertia.Defer(() => "foo value").DeepMerge()), ("bar", Inertia.DeepMerge("bar value"))));
        Assert.Contains(""","deepMergeProps":["foo","bar"],"deferredProps":{"default":["foo"]}}""", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exclude_merge_props_from_partial_only_response()
    {
        var page = await Json(UserProps(("foo", Inertia.Merge("foo value")), ("bar", Inertia.Merge("bar value"))), c => c.AsPartial("user", component: "User/Edit"));
        JsonAssert.Equal("""{"errors":{},"user":{"name":"Jonathan"}}""", page["props"]);
        JsonAssert.Missing(page, "mergeProps");
    }

    [Fact]
    public async Task Exclude_merge_props_from_partial_except_response()
    {
        var page = await Json(UserProps(("foo", Inertia.Merge("foo value")), ("bar", Inertia.Merge("bar value"))), c => c.AsPartial(except: "foo", component: "User/Edit"));
        JsonAssert.Equal("""{"errors":{},"user":{"name":"Jonathan"},"bar":"bar value"}""", page["props"]);
        JsonAssert.Equal("""["bar"]""", page["mergeProps"]);
    }

    [Fact]
    public async Task Exclude_merge_props_when_passed_in_reset_header()
    {
        var page = await Json(
            UserProps(("foo", Inertia.Merge("foo value")), ("bar", Inertia.Merge("bar value"))),
            c => c.AsPartial("foo", component: "User/Edit").WithHeader(InertiaHeaders.Reset, "foo"));
        JsonAssert.Equal("""{"errors":{},"foo":"foo value"}""", page["props"]);
        JsonAssert.Missing(page, "mergeProps");
    }

    [Fact]
    public async Task Xhr_response()
    {
        var page = await Json(new InertiaProps { ["user"] = new User("Jonathan") });
        JsonAssert.Equal(
            """{"component":"User/Edit","props":{"errors":{},"user":{"name":"Jonathan","email":null}},"url":"/user/123","version":"123"}""",
            page);
    }

    [Fact]
    public async Task Xhr_response_with_deferred_props_includes_deferred_metadata()
    {
        var page = await Json(UserProps(("results", Inertia.Defer(() => new InertiaProps { ["data"] = new[] { "item1", "item2" } }))));
        JsonAssert.Missing(page["props"], "results");
        JsonAssert.Equal("""{"default":["results"]}""", page["deferredProps"]);
    }

    [Fact]
    public async Task Callable_props_response_and_partial_response()
    {
        InertiaProps Props() => new()
        {
            ["users"] = Inertia.Prop(() => new[] { new User("Jonathan") }),
            ["organizations"] = Inertia.Prop(() => new[] { new User("Inertia") }),
        };

        var full = await Json(Props(), component: "User/Index", url: "/users");
        Assert.Equal("/users", (string?)full["url"]);
        Assert.Equal("Inertia", (string?)full["props"]!["organizations"]![0]!["name"]);

        var partial = await Json(Props(), c => c.AsPartial("users", component: "User/Index"), "User/Index", "/users");
        JsonAssert.Equal("""{"errors":{},"users":[{"name":"Jonathan","email":null}]}""", partial["props"]);
    }

    [Fact]
    public async Task Xhr_partial_response()
    {
        var page = await Json(new InertiaProps { ["user"] = new User("Jonathan"), ["partial"] = "partial-data" }, c => c.AsPartial("partial", component: "User/Edit"));
        JsonAssert.Equal("""{"errors":{},"partial":"partial-data"}""", page["props"]);
    }

    [Fact]
    public async Task Exclude_props_from_partial_response()
    {
        var page = await Json(new InertiaProps { ["user"] = new User("Jonathan"), ["partial"] = "partial-data" }, c => c.AsPartial(except: "user", component: "User/Edit"));
        JsonAssert.Equal("""{"errors":{},"partial":"partial-data"}""", page["props"]);
    }

    [Fact]
    public async Task Nested_and_double_nested_closures_are_resolved()
    {
        var page = await Json(new InertiaProps
        {
            ["auth"] = Inertia.Prop(() => new InertiaProps { ["user"] = Inertia.Prop(() => new InertiaProps { ["name"] = "Jonathan" }), ["token"] = "value" }),
            ["single"] = new InertiaProps { ["user"] = Inertia.Prop(() => new InertiaProps { ["name"] = "Jonathan" }) },
        });
        JsonAssert.Equal("""{"user":{"name":"Jonathan"},"token":"value"}""", page["props"]!["auth"]);
        JsonAssert.Equal("""{"user":{"name":"Jonathan"}}""", page["props"]!["single"]);
    }

    [Fact]
    public async Task Nested_optional_prop_inside_closure_is_excluded()
    {
        var page = await Json(new InertiaProps
        {
            ["auth"] = Inertia.Prop(() => new InertiaProps { ["user"] = new InertiaProps { ["name"] = "Jonathan" }, ["pending"] = Inertia.Optional(() => "secret") }),
        });
        JsonAssert.Equal("""{"user":{"name":"Jonathan"}}""", page["props"]!["auth"]);
    }

    [Fact]
    public async Task Nested_partial_props()
    {
        var page = await Json(
            new InertiaProps
            {
                ["auth"] = new InertiaProps
                {
                    ["user"] = Inertia.Optional(() => new InertiaProps { ["name"] = "Jonathan Reinink", ["email"] = "jonathan@example.com" }),
                    ["refresh_token"] = "value",
                    ["token"] = "value",
                },
                ["shared"] = new InertiaProps { ["flash"] = "value" },
            },
            c => c.AsPartial("auth.user,auth.refresh_token", component: "User/Edit"));

        JsonAssert.Equal(
            """{"errors":{},"auth":{"user":{"name":"Jonathan Reinink","email":"jonathan@example.com"},"refresh_token":"value"}}""",
            page["props"]);
    }

    [Fact]
    public async Task Exclude_nested_props_from_partial_response()
    {
        var page = await Json(
            new InertiaProps
            {
                ["auth"] = new InertiaProps { ["user"] = Inertia.Optional(() => "user"), ["refresh_token"] = "value" },
                ["shared"] = new InertiaProps { ["flash"] = "value" },
            },
            c => c.AsPartial("auth", "auth.user", "User/Edit"));

        JsonAssert.Equal("""{"errors":{},"auth":{"refresh_token":"value"}}""", page["props"]);
    }

    [Fact]
    public async Task Optional_props_are_not_included_by_default_but_are_in_partial_reload()
    {
        InertiaProps Props() => new() { ["users"] = Array.Empty<string>(), ["optional"] = Inertia.Optional(() => "An optional value") };

        var page = await Json(Props(), component: "Users");
        JsonAssert.Equal("""{"errors":{},"users":[]}""", page["props"]);

        var partial = await Json(Props(), c => c.AsPartial("optional", component: "Users"), "Users");
        JsonAssert.Equal("""{"errors":{},"optional":"An optional value"}""", partial["props"]);
    }

    [Fact]
    public async Task Defer_props_are_resolved_in_partial_reload()
    {
        var page = await Json(
            new InertiaProps { ["users"] = Array.Empty<string>(), ["defer"] = Inertia.Defer(() => new InertiaProps { ["foo"] = "bar" }) },
            c => c.AsPartial("defer", component: "Users"),
            "Users");
        JsonAssert.Equal("""{"errors":{},"defer":{"foo":"bar"}}""", page["props"]);
    }

    [Fact]
    public async Task Always_props_are_included_on_partial_reload()
    {
        var page = await Json(
            new InertiaProps
            {
                ["user"] = Inertia.Optional(() => "user"),
                ["data"] = new InertiaProps { ["name"] = "Taylor Otwell" },
                ["always"] = Inertia.Always(() => new InertiaProps { ["name"] = "The email field is required." }),
            },
            c => c.AsPartial("data", component: "User/Edit"));

        JsonAssert.Equal("""{"errors":{},"data":{"name":"Taylor Otwell"},"always":{"name":"The email field is required."}}""", page["props"]);
    }

    [Fact]
    public async Task Top_level_dot_props_get_unpacked()
    {
        var page = await Json(new InertiaProps
        {
            ["auth"] = new InertiaProps { ["user"] = new InertiaProps { ["name"] = "Jonathan Reinink" } },
            ["auth.user.can"] = new InertiaProps { ["do.stuff"] = true },
            ["product"] = new InertiaProps { ["name"] = "My example product" },
        });

        JsonAssert.Equal(
            """{"errors":{},"auth":{"user":{"name":"Jonathan Reinink","can":{"do.stuff":true}}},"product":{"name":"My example product"}}""",
            page["props"]);
    }

    [Fact]
    public async Task Nested_dot_props_do_not_get_unpacked()
    {
        var page = await Json(new InertiaProps
        {
            ["auth"] = new InertiaProps { ["user.can"] = new InertiaProps { ["do.stuff"] = true }, ["user"] = new InertiaProps { ["name"] = "Jonathan Reinink" } },
        });

        JsonAssert.Equal("""{"user.can":{"do.stuff":true},"user":{"name":"Jonathan Reinink"}}""", page["props"]!["auth"]);
    }

    [Fact]
    public async Task Once_props_are_always_resolved_on_initial_page_load()
    {
        var html = await Html("User/Edit", new InertiaProps { ["foo"] = Inertia.Once(() => "bar") });
        Assert.Equal(
            Script + """{"component":"User\u002FEdit","props":{"errors":{},"foo":"bar"},"url":"\u002Fuser\u002F123","version":"123","onceProps":{"foo":{"prop":"foo","expiresAt":null}}}""" + Div,
            html);
    }

    [Fact]
    public async Task Fresh_once_props_are_included_on_initial_page_load()
    {
        var page = await _harness.PageAsync(new InertiaProps { ["foo"] = Inertia.Once(() => "bar").Fresh() });
        Assert.Equal("bar", (string?)page["props"]!["foo"]);
        JsonAssert.Equal("""{"foo":{"prop":"foo","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Once_props_are_resolved_with_a_custom_key_and_ttl_value()
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero);
        var harness = new Harness(timeProvider: new FakeTimeProvider(now));
        var page = await harness.PageAsync(new InertiaProps { ["foo"] = Inertia.Once(() => "bar").As("baz").Until(TimeSpan.FromMinutes(1)) }, c => c.AsInertia());

        Assert.Equal("bar", (string?)page["props"]!["foo"]);
        JsonAssert.Equal($$$"""{"baz":{"prop":"foo","expiresAt":{{{now.AddMinutes(1).ToUnixTimeMilliseconds()}}}}}""", page["onceProps"]);
    }

    [Theory]
    [InlineData("foo", false)]
    [InlineData(null, true)]
    [InlineData("baz", true)]
    public async Task Once_props_on_subsequent_requests_depend_on_the_except_once_header(string? header, bool included)
    {
        var page = await Json(new InertiaProps { ["foo"] = Inertia.Once(() => "bar") }, c =>
        {
            if (header is not null)
            {
                c.WithHeader(InertiaHeaders.ExceptOnceProps, header);
            }
        });

        Assert.Equal(included, page["props"]!.AsObject().ContainsKey("foo"));
        JsonAssert.Equal("""{"foo":{"prop":"foo","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Once_props_are_resolved_on_partial_requests_when_included_in_only_headers()
    {
        var page = await Json(
            new InertiaProps { ["foo"] = Inertia.Once(() => "bar"), ["baz"] = Inertia.Once(() => "qux") },
            c => c.AsPartial("foo", component: "User/Edit").WithHeader(InertiaHeaders.ExceptOnceProps, "foo"));

        JsonAssert.Equal("""{"errors":{},"foo":"bar"}""", page["props"]);
        JsonAssert.Equal("""{"foo":{"prop":"foo","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Once_props_are_not_resolved_on_partial_requests_when_excluded_in_except_headers()
    {
        var page = await Json(
            new InertiaProps { ["foo"] = Inertia.Once(() => "bar"), ["baz"] = Inertia.Once(() => "qux") },
            c => c.AsPartial(except: "foo", component: "User/Edit").WithHeader(InertiaHeaders.ExceptOnceProps, "foo"));

        JsonAssert.Equal("""{"errors":{},"baz":"qux"}""", page["props"]);
        JsonAssert.Equal("""{"baz":{"prop":"baz","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Fresh_props_are_resolved_even_when_in_except_once_props_header()
    {
        var page = await Json(
            new InertiaProps { ["foo"] = Inertia.Once(() => "bar").Fresh(), ["baz"] = Inertia.Once(() => "qux") },
            c => c.WithHeader(InertiaHeaders.ExceptOnceProps, "foo,baz"));

        JsonAssert.Equal("""{"errors":{},"foo":"bar"}""", page["props"]);
        JsonAssert.Equal("""{"foo":{"prop":"foo","expiresAt":null},"baz":{"prop":"baz","expiresAt":null}}""", page["onceProps"]);
    }

    [Fact]
    public async Task Defer_props_that_are_once_and_already_loaded_are_excluded_unless_explicitly_requested()
    {
        InertiaProps Props() => new() { ["defer"] = Inertia.Defer(() => "value").Once() };

        var visit = await Json(Props(), c => c.WithHeader(InertiaHeaders.ExceptOnceProps, "defer"));
        JsonAssert.Equal("""{"errors":{}}""", visit["props"]);
        JsonAssert.Missing(visit, "deferredProps");
        JsonAssert.Equal("""{"defer":{"prop":"defer","expiresAt":null}}""", visit["onceProps"]);

        var requested = await Json(Props(), c => c.AsPartial("defer", component: "User/Edit").WithHeader(InertiaHeaders.ExceptOnceProps, "defer"));
        Assert.Equal("value", (string?)requested["props"]!["defer"]);
        JsonAssert.Missing(requested, "deferredProps");
        JsonAssert.Equal("""{"defer":{"prop":"defer","expiresAt":null}}""", requested["onceProps"]);
    }

    [Theory]
    [InlineData("", "/user/123", "", "/user/123")]
    [InlineData("/sub/directory", "/user/123", "", "/sub/directory/user/123")]
    [InlineData("", "/users/", "", "/users/")]
    [InlineData("", "/users/", "?page=1&sort=name", "/users/?page=1&sort=name")]
    [InlineData("", "/users", "", "/users")]
    [InlineData("", "/users", "?page=1&sort=name", "/users?page=1&sort=name")]
    public async Task The_page_url_keeps_path_base_trailing_slash_and_query(string pathBase, string path, string query, string expected)
    {
        var context = _harness.Context(path + query).AsInertia();
        context.Request.PathBase = pathBase;
        var page = Harness.ParsePage(await Harness.ExecuteAsync(context, Inertia.Render("User/Index")));
        Assert.Equal(expected, (string?)page["url"]);
    }
}
