using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Inertia.Net.Tests;

public sealed class AdapterParityTests
{
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
