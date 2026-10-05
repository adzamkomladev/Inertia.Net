using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Inertia.Net.Tests;

/// <summary>The page envelope: key order, omission rules, shared props, errors, flash, $bigint and HTML-safe escaping.</summary>
public class PageWriterTests
{
    [Fact]
    public async Task Minimal_page_has_only_the_required_keys()
    {
        var page = await new Harness().PageAsync(null);
        JsonAssert.Equal("""{"component":"TestComponent","props":{"errors":{}},"url":"/","version":"","sharedProps":["errors"]}""", page);

        var bare = await new Harness(o => o.ExposeSharedPropKeys = false).PageAsync(null);
        Assert.Equal(["component", "props", "url", "version"], bare.Select(p => p.Key));
    }

    [Fact]
    public async Task Keys_follow_the_protocol_order()
    {
        var harness = new Harness(o =>
        {
            o.EncryptHistory = true;
            o.PreserveBigIntegers = true;
        });

        var page = await harness.PageAsync(
            new InertiaProps
            {
                ["merge"] = Inertia.Merge(new[] { 1 }).Append(matchOn: "id"),
                ["prepend"] = Inertia.Merge(new[] { 1 }).Prepend(),
                ["deep"] = Inertia.DeepMerge(new InertiaProps { ["a"] = 1 }),
                ["deferred"] = Inertia.Defer(() => 1),
                ["rescued"] = Inertia.Prop<int>(() => throw new InvalidOperationException()).Rescue(),
                ["scroll"] = Inertia.Scroll(new PagedUsers([], 1, false)),
                ["once"] = Inertia.Once(() => 1),
            },
            c => c.Inertia().ClearHistory().PreserveFragment().Flash("toast", "Saved"));

        Assert.Equal(
            [
                "component", "props", "url", "version", "sharedProps", "mergeProps", "prependProps", "deepMergeProps", "matchPropsOn",
                "deferredProps", "rescuedProps", "scrollProps", "onceProps", "preserveBigIntegers", "clearHistory", "encryptHistory", "flash", "preserveFragment",
            ],
            page.Select(p => p.Key));
        Assert.True((bool)page["clearHistory"]!);
        Assert.True((bool)page["encryptHistory"]!);
        Assert.True((bool)page["preserveFragment"]!);
        Assert.True((bool)page["preserveBigIntegers"]!);
    }

    [Fact]
    public async Task Per_request_encrypt_history_overrides_the_option()
    {
        var on = await new Harness().PageAsync(null, c => c.Inertia().EncryptHistory());
        Assert.True((bool)on["encryptHistory"]!);

        var off = await new Harness(o => o.EncryptHistory = true).PageAsync(null, c => c.Inertia().EncryptHistory(false));
        JsonAssert.Missing(off, "encryptHistory");
    }

    [Fact]
    public async Task Version_comes_from_the_resolver_then_the_option()
    {
        Assert.Equal("", (string?)(await new Harness().PageAsync(null))["version"]);
        Assert.Equal("v1", (string?)(await new Harness(o => o.Version = "v1").PageAsync(null))["version"]);
        var resolved = await new Harness(o =>
        {
            o.Version = "v1";
            o.VersionResolver = ctx => "v-" + ctx.Request.Path.Value!.Trim('/');
        }).PageAsync(null, url: "/x");
        Assert.Equal("v-x", (string?)resolved["version"]);
    }

    [Fact]
    public async Task Flash_is_emitted_only_when_present()
    {
        JsonAssert.Missing(await new Harness().PageAsync(null), "flash");

        using var raw = JsonDocument.Parse("""{"a":[1,"</script>"]}""");
        var page = await new Harness().PageAsync(null, c => c.Inertia().Flash("toast", "Saved").Flash("raw", raw.RootElement.Clone()).Flash("none", null));
        JsonAssert.Equal("""{"toast":"Saved","raw":{"a":[1,"</script>"]},"none":null}""", page["flash"]);
    }

    [Fact]
    public async Task Shared_props_are_merged_under_page_props_and_listed()
    {
        var harness = new Harness(o => o
            .Share("appName", "Demo")
            .Share("auth.user", ctx => "user-for-" + ctx.Request.Path)
            .Share("static", new InertiaProps { ["x"] = 1 }));

        var page = await harness.PageAsync(
            new InertiaProps { ["appName"] = "Page wins", ["title"] = "Home" },
            c => c.Inertia().Share("requestShared", 42).Share("static", "request wins"),
            url: "/home");

        JsonAssert.Equal(
            """{"errors":{},"appName":"Page wins","auth":{"user":"user-for-/home"},"static":"request wins","requestShared":42,"title":"Home"}""",
            page["props"]);
        JsonAssert.Equal("""["errors","appName","auth","static","requestShared"]""", page["sharedProps"]);

        var hidden = await new Harness(o =>
        {
            o.ExposeSharedPropKeys = false;
            o.Share("appName", "Demo");
        }).PageAsync(null);
        Assert.Equal("Demo", (string?)hidden["props"]!["appName"]);
        JsonAssert.Missing(hidden, "sharedProps");
    }

    [Fact]
    public async Task Shared_props_are_filtered_like_page_props()
    {
        var page = await new Harness(o => o.Share("appName", "Demo").Share("always", Inertia.Always("yes")))
            .PageAsync(new InertiaProps { ["a"] = 1 }, c => c.AsPartial("a"));
        JsonAssert.Equal("""{"errors":{},"always":"yes","a":1}""", page["props"]);
    }

    [Fact]
    public async Task Errors_are_always_present_and_survive_partial_reloads()
    {
        var page = await new Harness().PageAsync(
            new InertiaProps { ["a"] = 1, ["b"] = 2 },
            c => c.AsPartial("a", "errors").Inertia().WithErrors(new Dictionary<string, string> { ["name"] = "Required" }));
        JsonAssert.Equal("""{"errors":{"name":"Required"},"a":1}""", page["props"]);
    }

    [Fact]
    public async Task Errors_use_the_first_message_unless_with_all_errors()
    {
        var errors = new Dictionary<string, string[]> { ["name"] = ["Required", "Too short"], ["email"] = ["Invalid"] };

        var first = await new Harness().PageAsync(null, c => c.Inertia().WithErrors(errors));
        JsonAssert.Equal("""{"name":"Required","email":"Invalid"}""", first["props"]!["errors"]);

        var all = await new Harness(o => o.WithAllErrors = true).PageAsync(null, c => c.Inertia().WithErrors(errors));
        JsonAssert.Equal("""{"name":["Required","Too short"],"email":["Invalid"]}""", all["props"]!["errors"]);
    }

    [Fact]
    public async Task Error_bag_header_nests_the_default_bag()
    {
        var page = await new Harness().PageAsync(null, c => c
            .WithHeader(InertiaHeaders.ErrorBag, "createUser")
            .Inertia().WithErrors(new Dictionary<string, string> { ["name"] = "Required" }));
        JsonAssert.Equal("""{"createUser":{"name":"Required"}}""", page["props"]!["errors"]);
    }

    [Fact]
    public void Errors_resolver_bags()
    {
        Dictionary<string, Dictionary<string, string[]>> Bags(params string[] names) =>
            names.ToDictionary(n => n, n => new Dictionary<string, string[]> { ["field"] = [$"{n} 1", $"{n} 2"], ["empty"] = [] });

        static string Json(InertiaProps props) => JsonSerializer.Serialize(props, InertiaJsonContext.Default.InertiaProps);

        Assert.Equal("{}", Json(ErrorsResolver.Resolve(null, false, null)));
        Assert.Equal("""{"field":"default 1"}""", Json(ErrorsResolver.Resolve(Bags("default"), false, null)));
        Assert.Equal("""{"field":["default 1","default 2"]}""", Json(ErrorsResolver.Resolve(Bags("default"), true, null)));
        Assert.Equal("""{"bag":{"field":"default 1"}}""", Json(ErrorsResolver.Resolve(Bags("default", "login"), false, "bag")));
        Assert.Equal("""{"field":"default 1"}""", Json(ErrorsResolver.Resolve(Bags("login", "default"), false, null)));
        Assert.Equal("""{"login":{"field":"login 1"},"register":{"field":"register 1"}}""", Json(ErrorsResolver.Resolve(Bags("login", "register"), false, "ignored")));
    }

    [Fact]
    public async Task Feature_errors_merge_per_bag()
    {
        var page = await new Harness().PageAsync(null, c => c.Inertia()
            .WithErrors(new Dictionary<string, string> { ["a"] = "1" }, "login")
            .WithErrors(new Dictionary<string, string[]> { ["b"] = ["2"] }, "login")
            .WithErrors(new Dictionary<string, string> { ["c"] = "3" }, "register"));
        JsonAssert.Equal("""{"login":{"a":"1","b":"2"},"register":{"c":"3"}}""", page["props"]!["errors"]);
    }

    [Fact]
    public async Task Big_integers_are_wrapped_at_the_boundaries_of_every_integer_type()
    {
        var harness = new Harness(o => o.PreserveBigIntegers = true);
        var page = await harness.PageAsync(
            new InertiaProps
            {
                ["safe"] = 42L,
                ["max"] = 9007199254740991L,
                ["min"] = -9007199254740991L,
                ["over"] = 9007199254740992L,
                ["under"] = -9007199254740992L,
                ["longMin"] = long.MinValue,
                ["ulongSafe"] = 9007199254740991UL,
                ["ulongMax"] = ulong.MaxValue,
                ["int128"] = (Int128)5,
                ["int128Big"] = Int128.MinValue,
                ["uint128Big"] = UInt128.MaxValue,
                ["bigInteger"] = BigInteger.Pow(10, 30),
                ["bigIntegerSmall"] = new BigInteger(-7),
                ["int"] = int.MaxValue,
                ["typed"] = new BigNumbers(9007199254740992L, 1UL, 2, 3, 4, null, [1L, 9007199254740993L]),
                ["lazy"] = Inertia.Prop(() => (long?)9007199254740992L),
            },
            c => c.AsInertia().Inertia().Flash("id", 9007199254740992L));

        JsonAssert.Equal(
            """
            {
              "errors": {},
              "safe": 42, "max": 9007199254740991, "min": -9007199254740991,
              "over": {"$bigint":"9007199254740992"}, "under": {"$bigint":"-9007199254740992"},
              "longMin": {"$bigint":"-9223372036854775808"},
              "ulongSafe": 9007199254740991, "ulongMax": {"$bigint":"18446744073709551615"},
              "int128": 5, "int128Big": {"$bigint":"-170141183460469231731687303715884105728"},
              "uint128Big": {"$bigint":"340282366920938463463374607431768211455"},
              "bigInteger": {"$bigint":"1000000000000000000000000000000"}, "bigIntegerSmall": -7,
              "int": 2147483647,
              "typed": {"long":{"$bigint":"9007199254740992"},"uLong":1,"int128":2,"uInt128":3,"big":4,"nullable":null,"list":[1,{"$bigint":"9007199254740993"}]},
              "lazy": {"$bigint":"9007199254740992"}
            }
            """,
            page["props"]);
        JsonAssert.Equal("""{"id":{"$bigint":"9007199254740992"}}""", page["flash"]);
        Assert.True((bool)page["preserveBigIntegers"]!);
    }

    [Fact]
    public async Task Big_integers_are_plain_numbers_when_disabled()
    {
        var page = await new Harness().PageAsync(new InertiaProps { ["big"] = 900719925474099988L });
        Assert.Equal(900719925474099988L, (long)page["props"]!["big"]!);
        JsonAssert.Missing(page, "preserveBigIntegers");
    }

    [Fact]
    public async Task Html_embedded_json_cannot_break_out_of_the_script_element()
    {
        var corpus = new[]
        {
            "</script><script>alert(1)</script>", "<!--", "-->", "<![CDATA[", "&amp;", "'single'", "\"double\"", "a/b", "</", "<>&",
            ((char)0x2028).ToString(), ((char)0x2029).ToString(), "café", "\U0001F600", "+ADw-", "`tick`",
        };
        var harness = new Harness(o => o.RootElementId = "app");
        var context = harness.Context("/</script>?q=</script>");
        context.Inertia().Flash("</script>", "</script>").Share("<!--key", "x");
        var html = await Harness.ExecuteAsync(context, Inertia.Render("</script>", new InertiaProps { ["corpus"] = corpus, ["</script>"] = 1 }));

        var json = Harness.ExtractPageJson(html);
        Assert.DoesNotContain('<', json);
        Assert.DoesNotContain('>', json);
        Assert.DoesNotContain('&', json);
        Assert.DoesNotContain('\'', json);
        Assert.DoesNotContain((char)0x2028, json);
        Assert.DoesNotContain((char)0x2029, json);
        Assert.Equal(1, CountOf(html, "</script>"));

        var page = JsonNode.Parse(json)!;
        Assert.Equal(corpus, page["props"]!["corpus"]!.AsArray().Select(n => (string)n!));
        Assert.Equal("</script>", (string?)page["component"]);
        Assert.Equal("</script>", (string?)page["flash"]!["</script>"]);
        Assert.Equal(1, (int)page["props"]!["</script>"]!);
        Assert.StartsWith("/%3C/script%3E", (string?)page["url"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_responses_keep_the_app_encoder()
    {
        var page = await new Harness().PageAsync(new InertiaProps { ["path"] = "a/b" }, c => c.AsInertia());
        Assert.Equal("a/b", (string?)page["props"]!["path"]);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
