using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Inertia.Net.Tests;

public sealed class AdapterParityTests
{
    [Fact]
    public async Task Flushing_shares_is_request_local_and_new_shares_can_be_added()
    {
        var calls = 0;
        var harness = new Harness(options => options.Share("global", _ => { calls++; return "configured"; }));
        var http = harness.Context().AsInertia();
        http.Inertia().Share("request", "before").FlushShared().Share("after", "new")
            .WithErrors(new Dictionary<string, string> { ["email"] = "required" });
        var page = Harness.ParsePage(await Harness.ExecuteAsync(http, Inertia.Render("Page")));
        Assert.Null(page["props"]!["global"]);
        Assert.Null(page["props"]!["request"]);
        Assert.Equal("new", (string?)page["props"]!["after"]);
        Assert.Equal("required", (string?)page["props"]!["errors"]!["email"]);
        Assert.Equal(0, calls);
        var next = await harness.PageAsync(null);
        Assert.Equal("configured", (string?)next["props"]!["global"]);
        Assert.Null(next["props"]!["after"]);
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task Live_channel_event_products_keep_order_and_literal_channel_names()
    {
        var harness = new Harness();
        var page = await harness.PageAsync(new InertiaProps
        {
            ["order"] = Inertia.Prop(() => 1)
                .Live(LiveChannel.Public("private-orders.1"), "A", "B")
                .Live(LiveChannel.Private("b"), "A", "B"),
            ["always"] = Inertia.Always(2).Live(LiveChannel.Public("a"), "Changed"),
            ["feed"] = Inertia.Merge(new[] { 1 }),
        });
        JsonAssert.Equal("""["feed"]""", page["mergeProps"]);
        JsonAssert.Equal("""
            {"order":{"listeners":[{"channel":{"name":"private-orders.1","type":"public"},"events":["A","B"]},
                                  {"channel":{"name":"b","type":"private"},"events":["A","B"]}]},
             "always":{"listeners":[{"channel":{"name":"a","type":"public"},"events":["Changed"]}]}}
            """, page["liveProps"]);
        Assert.Equal(2, (int?)page["props"]!["always"]);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("request")]
    [InlineData("global")]
    public async Task Explicit_errors_override_the_empty_default(string source)
    {
        var errors = Inertia.Always(new InertiaProps { ["custom"] = "message" });
        var harness = new Harness(options =>
        {
            if (source == "global") options.Share("errors", errors);
        });
        var http = harness.Context().AsInertia();
        if (source == "request") http.Inertia().Share("errors", errors);
        var page = Harness.ParsePage(await Harness.ExecuteAsync(http, Inertia.Render("Page",
            source == "page" ? new InertiaProps { ["errors"] = errors } : null)));
        JsonAssert.Equal("""{"custom":"message"}""", page["props"]!["errors"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Big_integer_preservation_keeps_numeric_objects_and_serializer_shapes(bool preserve)
    {
        var harness = new Harness(options => options.PreserveBigIntegers = preserve);
        var date = DateTime.Parse("2026-11-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var shared = new SharedNumber(9007199254740993L);
        var page = await harness.PageAsync(new InertiaProps
        {
            ["first"] = shared,
            ["nested"] = new InertiaProps { ["second"] = shared },
            ["numbers"] = new InertiaProps { ["0"] = 9007199254740993L, ["1"] = -9007199254740993L },
            ["empty"] = new InertiaProps(),
            ["date"] = date,
            ["id"] = id,
            ["collection"] = new[] { 1, 2 },
        });
        var expected = preserve ? """{"$bigint":"9007199254740993"}""" : "9007199254740993";
        JsonAssert.Equal(expected, page["props"]!["first"]!["large"]);
        JsonAssert.Equal(expected, page["props"]!["nested"]!["second"]!["large"]);
        Assert.Equal(9007199254740993L, shared.Large);
        Assert.IsType<System.Text.Json.Nodes.JsonObject>(page["props"]!["numbers"]);
        JsonAssert.Equal(expected, page["props"]!["numbers"]!["0"]);
        JsonAssert.Equal(preserve ? """{"$bigint":"-9007199254740993"}""" : "-9007199254740993", page["props"]!["numbers"]!["1"]);
        JsonAssert.Equal("{}", page["props"]!["empty"]);
        JsonAssert.Equal(System.Text.Json.JsonSerializer.Serialize(date), page["props"]!["date"]);
        JsonAssert.Equal(System.Text.Json.JsonSerializer.Serialize(id), page["props"]!["id"]);
        JsonAssert.Equal("[1,2]", page["props"]!["collection"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cyclic_literal_props_fail_before_output_and_do_not_poison_the_writer_pool(bool preserve)
    {
        var harness = new Harness(options => options.PreserveBigIntegers = preserve);
        var cyclic = new InertiaProps();
        cyclic["self"] = cyclic;
        var http = harness.Context().AsInertia();
        await Assert.ThrowsAnyAsync<Exception>(() => Harness.ExecuteAsync(http, Inertia.Render("Page", cyclic)));
        Assert.Empty(Harness.Body(http));
        var node = new CyclicNode();
        node.Next = node;
        var typed = harness.Context().AsInertia();
        await Assert.ThrowsAnyAsync<Exception>(() => Harness.ExecuteAsync(typed, Inertia.Render("Page", new InertiaProps { ["node"] = node })));
        Assert.Empty(Harness.Body(typed));
        var valid = await harness.PageAsync(new InertiaProps { ["ok"] = 1 });
        Assert.Equal(1, (int?)valid["props"]!["ok"]);
    }

    private sealed class CyclicNode
    {
        public CyclicNode? Next { get; set; }
    }

    private sealed record SharedNumber(long Large);
    [Fact]
    public async Task Loaded_once_props_are_resolved_on_unfiltered_partial_reloads()
    {
        var harness = new Harness();
        var loads = 0;
        var page = await harness.PageAsync(new InertiaProps { ["settings"] = Inertia.Once(() => ++loads) },
            context => context.AsPartial().WithHeader(InertiaHeaders.ExceptOnceProps, "settings"));
        Assert.Equal(1, loads);
        Assert.Equal(1, (int?)page["props"]!["settings"]);
        Assert.Equal("settings", (string?)page["onceProps"]!["settings"]!["prop"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loaded_shared_once_values_keep_shared_and_once_metadata(bool global)
    {
        var loads = 0;
        var harness = new Harness(options =>
        {
            if (global) options.Share("settings", Inertia.Once(() => ++loads));
        });
        var context = harness.Context().AsInertia().WithHeader(InertiaHeaders.ExceptOnceProps, "settings");
        if (!global) context.Inertia().Share("settings", Inertia.Once(() => ++loads));
        var page = Harness.ParsePage(await Harness.ExecuteAsync(context, Inertia.Render("Page")));
        Assert.Equal(0, loads);
        Assert.Null(page["props"]!["settings"]);
        Assert.Contains("settings", page["sharedProps"]!.AsArray().Select(value => (string?)value));
        Assert.Equal("settings", (string?)page["onceProps"]!["settings"]!["prop"]);
    }
    [Theory]
    [InlineData(true, "Pages/Home")]
    [InlineData(false, "Home")]
    public async Task Component_transformation_happens_before_rendering(bool transform, string expected)
    {
        var harness = new Harness(options => options.ComponentTransformer = name => transform ? "Pages/" + name : null);
        var page = await harness.PageAsync(new InertiaProps { ["title"] = "Home" }, component: "Home");
        Assert.Equal(expected, (string?)page["component"]);
    }

    [Fact]
    public async Task Cached_big_integers_can_be_rendered_with_both_settings_without_reloading()
    {
        var harness = new Harness();
        var calls = 0;
        var props = new InertiaProps { ["id"] = Inertia.Prop(() => { calls++; return 9007199254740993L; }).Cache("parity-id", TimeSpan.FromMinutes(1)) };
        foreach (var preserve in new[] { true, false, true })
        {
            var context = harness.Context().AsInertia();
            var page = Harness.ParsePage(await Harness.ExecuteAsync(context, Inertia.Render("Page", props).PreserveBigIntegers(preserve)));
            JsonAssert.Equal(preserve ? """{"$bigint":"9007199254740993"}""" : "9007199254740993", page["props"]!["id"]);
        }
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cached_arbitrary_precision_integers_keep_their_numeric_value()
    {
        var harness = new Harness(options => options.PreserveBigIntegers = true);
        const string digits = "10000000000000000000000000000000000000000";
        var props = new InertiaProps
        {
            ["id"] = Inertia.Prop(() => System.Numerics.BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture))
                .Cache("parity-arbitrary", TimeSpan.FromMinutes(1)),
        };
        foreach (var preserve in new[] { true, false })
        {
            var context = harness.Context().AsInertia();
            var page = Harness.ParsePage(await Harness.ExecuteAsync(context, Inertia.Render("Page", props).PreserveBigIntegers(preserve)));
            JsonAssert.Equal(preserve ? """{"$bigint":"10000000000000000000000000000000000000000"}""" : digits, page["props"]!["id"]);
        }
    }

    [Theory]
    [InlineData(true, false, 303)]
    [InlineData(true, true, 303)]
    [InlineData(false, false, 302)]
    public async Task Query_redirects_follow_with_get(bool inertia, bool empty, int expected)
    {
        var harness = new Harness();
        var app = new ApplicationBuilder(harness.Services);
        app.UseInertia();
        app.Run(context => empty ? Task.CompletedTask : Inertia.Back().ExecuteAsync(context));
        var context = harness.Context("/submit", "QUERY");
        if (inertia) context.AsInertia();
        await app.Build()(context);
        Assert.Equal(expected, context.Response.StatusCode);
        Assert.Equal("/", context.Response.Headers.Location);
    }

    [Fact]
    public async Task Forget_flash_handles_literal_nested_and_restored_values_without_mutating_the_source()
    {
        var harness = new Harness();
        var context = harness.Context().AsInertia();
        var original = JsonNode.Parse("""{"name":"Ada","id":1}""")!;
        context.Inertia().Flash("user", original).Flash("user.name", "literal").Flash("toast", "saved");
        context.Inertia().ForgetFlashed("user.name");
        Assert.Equal("Ada", (string?)original["name"]);
        context.Inertia().ForgetFlashed(new[] { "user.name", "missing" });
        var page = Harness.ParsePage(await Harness.ExecuteAsync(context, Inertia.Render("Page")));
        JsonAssert.Equal("""{"user":{"id":1},"toast":"saved"}""", page["flash"]);
        Assert.Equal("Ada", (string?)original["name"]);
        context.Inertia().ForgetFlashed("user", "toast");
        var again = Harness.ParsePage(await Harness.ExecuteAsync(harness.Context().AsInertia(), Inertia.Render("Page")));
        Assert.Null(again["flash"]);

        var restored = harness.Context().AsInertia();
        InertiaStateSerializer.TryRestore(restored.Inertia(), """{"flash":{"toast":"saved","other":1}}"""u8.ToArray());
        restored.Inertia().ForgetFlashed("toast");
        var restoredPage = Harness.ParsePage(await Harness.ExecuteAsync(restored, Inertia.Render("Page")));
        JsonAssert.Equal("""{"other":1}""", restoredPage["flash"]);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    public async Task Big_integer_override_applies_to_props_and_flash(bool global, bool preserve, bool html)
    {
        var harness = new Harness(options => options.PreserveBigIntegers = global);
        var context = harness.Context();
        if (!html) context.AsInertia();
        context.Inertia().Flash("id", 9007199254740993L);
        var result = Inertia.Render("Page", new InertiaProps { ["id"] = 9007199254740993L }).PreserveBigIntegers(preserve);
        var page = Harness.ParsePage(await Harness.ExecuteAsync(context, result));
        Assert.Equal(preserve, page["preserveBigIntegers"]?.GetValue<bool>() == true);
        var expected = preserve ? """{"$bigint":"9007199254740993"}""" : "9007199254740993";
        JsonAssert.Equal(expected, page["props"]!["id"]);
        JsonAssert.Equal(expected, page["flash"]!["id"]);
        var next = await harness.PageAsync(new InertiaProps { ["id"] = 9007199254740993L });
        Assert.Equal(global, next["preserveBigIntegers"]?.GetValue<bool>() == true);
    }

    [Fact]
    public async Task Broadcast_resolves_every_category_keeps_dot_keys_and_omits_rescued_values()
    {
        var harness = new Harness();
        var context = harness.Context().AsPartial("unrelated");
        context.Request.Headers[InertiaHeaders.ExceptOnceProps] = "once";
        var props = await Inertia.BroadcastPropsAsync(context, new InertiaProps
        {
            ["auth.user"] = Inertia.Prop(() => "Ada"),
            ["optional"] = Inertia.Optional(async () => { await Task.Yield(); return 2; }),
            ["defer"] = Inertia.Defer(() => 3),
            ["once"] = Inertia.Once(() => 4),
            ["always"] = Inertia.Always(5),
            ["rescued"] = Inertia.Defer<int>(() => throw new InvalidOperationException()).Rescue(),
            ["returned"] = Inertia.Prop(() => Inertia.Optional(() => 6)),
            ["returnedRescue"] = Inertia.Prop(() => Inertia.Defer<int>(() => throw new InvalidOperationException()).Rescue()),
        });
        Assert.Equal("""{"auth.user":"Ada","optional":2,"defer":3,"once":4,"always":5,"returned":6}""", props.GetRawText());
        Assert.Null(context.Features.Get<DevToolsCapture>());
    }

    [Fact]
    public async Task Broadcast_accepts_typed_dictionaries_and_scroll_without_page_metadata()
    {
        var harness = new Harness();
        var values = await Inertia.BroadcastPropsAsync(harness.Context(), new Dictionary<string, int> { ["count"] = 2 });
        Assert.Equal("""{"count":2}""", values.GetRawText());
        var scroll = await Inertia.BroadcastPropsAsync(harness.Context(), new InertiaProps
        {
            ["items"] = Inertia.Scroll(new[] { 1, 2 }),
        });
        Assert.Equal("""{"items":[1,2]}""", scroll.GetRawText());
    }

    [Fact]
    public async Task Inspector_reads_leave_pending_state_untouched()
    {
        var harness = new Harness();
        var app = new ApplicationBuilder(harness.Services);
        app.UseInertia();
        app.Run(context =>
        {
            Assert.Null(context.Features.Get<InertiaFeature>());
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });
        var context = harness.Context("/_inertia/devtools/entries/example");
        context.Request.Headers.Cookie = ".Inertia.State=invalid";
        await app.Build()(context);
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }
}
