using System.Numerics;
using System.Net;
using System.Text;

namespace Inertia.Net.Testing.Tests;

public class ParsingTests
{
    [Fact]
    public async Task Parses_json_response()
    {
        await using var s = await FakeServer.StartAsync();
        using var r = await s.Client.InertiaGetAsync("/users?page=2", "v1");
        var page = await r.GetInertiaPageAsync();
        Assert.Equal("Users/Index", page.Component);
        Assert.Equal("/users?page=2", page.Url);
        Assert.Equal("v1", page.Version);
        Assert.Equal("Users", (string)page.Prop("title")!);
        Assert.Equal(["plans"], page.DeferredProps["default"]);
        Assert.Equal(["stats"], page.DeferredProps["sidebar"]);
        Assert.True(page.PreserveBigIntegers);
        Assert.False(page.EncryptHistory);
        Assert.Equal("Saved", (string)page.Flash["toast"]!);
        Assert.Empty(page.MergeProps);
        Assert.Empty(page.OnceProps);
    }

    [Theory]
    [InlineData("/users")]
    [InlineData("/html/attr-order")]
    [InlineData("/html/custom-id")]
    [InlineData("/html/after-other-scripts")]
    public async Task Parses_html_response_variants(string url)
    {
        await using var s = await FakeServer.StartAsync();
        using var r = await s.Client.GetAsync(url);
        var page = await r.GetInertiaPageAsync();
        Assert.Equal("Users/Index", page.Component);
        Assert.Equal("Ann", (string)page.Prop("users.0.name")!);
        Assert.Equal("</script><b>&", (string)page.Prop("evil")!);
    }

    [Fact]
    public async Task Parses_script_content_with_escaped_closing_tag()
    {
        var html = "<script data-page=\"app\" type=\"application/json\">{\"component\":\"A\",\"props\":{\"x\":\"\\u003C/script\\u003E\"},\"url\":\"/\",\"version\":\"\"}</script>";
        using var r = new HttpResponseMessage { Content = new StringContent(html, Encoding.UTF8, "text/html") };
        var page = await r.GetInertiaPageAsync();
        Assert.Equal("</script>", (string)page.Prop("x")!);
    }

    [Fact]
    public async Task Missing_version_defaults_to_empty()
    {
        using var r = JsonResponse("{\"component\":\"A\",\"props\":{},\"url\":\"/\"}");
        Assert.Equal("", (await r.GetInertiaPageAsync()).Version);
    }

    [Fact]
    public async Task Plain_html_is_not_an_inertia_response()
    {
        await using var s = await FakeServer.StartAsync();
        using var r = await s.Client.GetAsync("/plain");
        var e = await Assert.ThrowsAsync<InertiaAssertionException>(() => r.GetInertiaPageAsync());
        Assert.Equal("Not a valid Inertia response: no X-Inertia response header and no <script data-page> element found. [HTTP 200 OK, Content-Type: none] Body: hello", e.Message);
    }

    [Fact]
    public async Task Not_found_message_includes_status_and_snippet()
    {
        await using var s = await FakeServer.StartAsync();
        using var r = await s.Client.GetAsync("/missing");
        var e = await Assert.ThrowsAsync<InertiaAssertionException>(() => r.GetInertiaPageAsync());
        Assert.Contains("HTTP 404 NotFound", e.Message);
        Assert.EndsWith("Body: Not here", e.Message);
    }

    [Fact]
    public async Task Long_bodies_are_truncated_in_failure_message()
    {
        using var r = new HttpResponseMessage { Content = new StringContent(new string('x', 500)) };
        var e = await Assert.ThrowsAsync<InertiaAssertionException>(() => r.GetInertiaPageAsync());
        Assert.EndsWith(new string('x', 200) + "...", e.Message);
    }

    [Fact]
    public async Task Json_without_x_inertia_header_is_rejected()
    {
        using var r = new HttpResponseMessage { Content = new StringContent("{\"component\":\"A\",\"props\":{},\"url\":\"/\"}") };
        await Assert.ThrowsAsync<InertiaAssertionException>(() => r.GetInertiaPageAsync());
    }

    [Fact]
    public async Task Invalid_json_and_missing_fields_are_reported()
    {
        await using var s = await FakeServer.StartAsync();
        using var bad = await s.Client.GetAsync("/bad-json");
        var e1 = await Assert.ThrowsAsync<InertiaAssertionException>(() => bad.GetInertiaPageAsync());
        Assert.StartsWith("Not a valid Inertia response: page is not valid JSON", e1.Message);

        using var incomplete = await s.Client.GetAsync("/incomplete");
        var e2 = await Assert.ThrowsAsync<InertiaAssertionException>(() => incomplete.GetInertiaPageAsync());
        Assert.StartsWith("Not a valid Inertia response: page has no string 'component'.", e2.Message);
    }

    [Fact]
    public async Task Prop_supports_dot_paths_indexes_and_reports_missing()
    {
        using var r = JsonResponse("{\"component\":\"A\",\"props\":{\"users\":[{\"name\":\"Ann\"}],\"n\":null},\"url\":\"/\",\"version\":\"\"}");
        var page = await r.GetInertiaPageAsync();
        Assert.Equal("Ann", (string)page.Prop("users.0.name")!);
        Assert.Null(page.Prop("users.5.name"));
        Assert.True(page.HasProp("n"));
        Assert.False(page.HasProp("users.1"));
    }

    [Fact]
    public async Task Decodes_bigint()
    {
        await using var s = await FakeServer.StartAsync();
        using var r = await s.Client.InertiaGetAsync("/users");
        var page = await r.GetInertiaPageAsync();
        Assert.Equal(BigInteger.Parse("9007199254740993"), page.PropBigInteger("big"));
        using var r2 = JsonResponse("{\"component\":\"A\",\"props\":{\"n\":42,\"s\":\"x\"},\"url\":\"/\"}");
        var p2 = await r2.GetInertiaPageAsync();
        Assert.Equal(42, p2.PropBigInteger("n"));
        Assert.Throws<InertiaAssertionException>(() => p2.PropBigInteger("s"));
    }

    [Fact]
    public async Task Exposes_page_metadata()
    {
        using var r = JsonResponse("""
            {"component":"A","props":{},"url":"/","version":"1","sharedProps":["errors"],"mergeProps":["a"],"prependProps":["b"],
             "deepMergeProps":["c"],"matchPropsOn":["a.id"],"rescuedProps":["d"],"scrollProps":{"feed":{"pageName":"page"}},
             "onceProps":{"plans":{"prop":"plans","expiresAt":null}},"encryptHistory":true,"clearHistory":true,"preserveFragment":true}
            """);
        var p = await r.GetInertiaPageAsync();
        Assert.Equal(["errors"], p.SharedProps);
        Assert.Equal(["a"], p.MergeProps);
        Assert.Equal(["b"], p.PrependProps);
        Assert.Equal(["c"], p.DeepMergeProps);
        Assert.Equal(["a.id"], p.MatchPropsOn);
        Assert.Equal(["d"], p.RescuedProps);
        Assert.True(p.ScrollProps.ContainsKey("feed"));
        Assert.True(p.OnceProps.ContainsKey("plans"));
        Assert.True(p.EncryptHistory && p.ClearHistory && p.PreserveFragment);
        Assert.False(p.PreserveBigIntegers);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    internal static HttpResponseMessage JsonResponse(string json)
    {
        var r = new HttpResponseMessage { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        r.Headers.Add("X-Inertia", "true");
        return r;
    }
}
