using System.Text;
using Microsoft.Extensions.DependencyInjection;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

public sealed class RootTemplateTests : IDisposable
{
    private readonly TempRoot _root = new();

    public void Dispose() => _root.Dispose();

    private Harness Create(string template, Action<InertiaOptions>? configure = null, string environment = "Production", Action<IServiceCollection>? services = null)
    {
        _root.Write("app.html", template);
        return new Harness(configure, contentRoot: _root.Path, environment: environment, configureServices: services);
    }

    private static async Task<string> RenderAsync(Harness harness, InertiaResult? result = null, string url = "/")
    {
        var context = harness.Context(url);
        return await Harness.ExecuteAsync(context, result ?? Render("Home", new InertiaProps { ["a"] = 1 }));
    }

    private void Manifest() => _root.Write("wwwroot/build/.vite/manifest.json", """
        { "src/app.tsx": { "file": "assets/app-1.js", "isEntry": true, "css": ["assets/app-1.css"] }, "src/b.ts": { "file": "assets/b.js", "isEntry": true } }
        """);

    [Fact]
    public async Task Full_html_has_exactly_one_page_script_followed_by_the_root_div()
    {
        var html = await RenderAsync(Create("<!DOCTYPE html>\n<html><head><title>T</title>@inertiaHead</head><body>@inertia</body></html>"));

        Assert.Equal("<!DOCTYPE html>\n<html><head><title>T</title></head><body><script data-page=\"app\" type=\"application/json\">", html[..html.IndexOf("{\"", StringComparison.Ordinal)]);
        Assert.Equal(1, Count(html, "<script data-page=\"app\" type=\"application/json\">"));
        Assert.Equal(1, Count(html, "<script"));
        Assert.EndsWith("</script><div id=\"app\"></div></body></html>", html, StringComparison.Ordinal);
        Assert.Equal("Home", (string?)Harness.ParsePage(html)["component"]);
    }

    [Fact]
    public async Task Custom_root_element_id_is_used()
    {
        var html = await RenderAsync(Create("@inertia", o => o.RootElementId = "root"));
        Assert.StartsWith("<script data-page=\"root\" type=\"application/json\">", html, StringComparison.Ordinal);
        Assert.EndsWith("</script><div id=\"root\"></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_cannot_break_out_of_the_script_element_in_the_final_html()
    {
        var evil = new[] { "</script><script>alert(1)</script>", "<!--", "<![CDATA[", "</", "&lt;" };
        var html = await RenderAsync(Create("<body>@inertia</body>"), Render("</script>", new InertiaProps { ["x"] = evil }), "/</script>");

        Assert.Equal(1, Count(html, "<script"));
        Assert.Equal(1, Count(html, "</script>"));
        Assert.Equal(evil, Harness.ParsePage(html)["props"]!["x"]!.AsArray().Select(n => (string)n!));
    }

    [Fact]
    public async Task Tokens_are_replaced_and_at_signs_in_ordinary_text_are_kept()
    {
        var html = await RenderAsync(Create("@@inertia @@@@ a@b.com <style>@media print{}</style> @inertiaFoo @@vite(\"x\")|@inertia"));

        Assert.StartsWith("@inertia @@ a@b.com <style>@media print{}</style> @inertiaFoo @vite(\"x\")|<script data-page", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, "<script"));
    }

    [Fact]
    public async Task Crlf_and_bom_are_handled()
    {
        var path = Path.Combine(_root.Path, "app.html");
        File.WriteAllBytes(path, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("<html>\r\n<head>@inertiaHead</head>\r\n<body>\r\n  @inertia\r\n</body>\r\n</html>é")]);
        var harness = new Harness(contentRoot: _root.Path);
        var html = await RenderAsync(harness);

        Assert.StartsWith("<html>\r\n<head></head>\r\n<body>\r\n  <script", html, StringComparison.Ordinal);
        Assert.EndsWith("</div>\r\n</body>\r\n</html>é", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_inertia_token_is_an_error_naming_the_file()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RenderAsync(Create("<html>@inertiaHead</html>")));
        Assert.Contains("@inertia", ex.Message, StringComparison.Ordinal);
        Assert.Contains("app.html", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_template_is_an_error_naming_the_expected_path()
    {
        var harness = new Harness(contentRoot: _root.Path);
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => RenderAsync(harness));
        Assert.Contains(Path.Combine(_root.Path, "app.html"), ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@vite(", "@vite")]
    [InlineData("@vite()", "@vite")]
    [InlineData("@vite(src/app.tsx)", "@vite")]
    [InlineData("@vite(\"a\" \"b\")", "@vite")]
    [InlineData("@vite", "@vite")]
    [InlineData("@viewData(\"a\", \"b\")", "@viewData")]
    public async Task Malformed_tokens_are_errors_with_a_line_number(string token, string name)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RenderAsync(Create("<html>\n" + token + "@inertia")));
        Assert.Contains(name, ex.Message, StringComparison.Ordinal);
        Assert.Contains("line 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vite_tokens_accept_single_and_double_quotes_and_several_entries()
    {
        Manifest();
        var html = await RenderAsync(Create("<head>@vite(\"src/app.tsx\", 'src/b.ts' )@viteReactRefresh</head>@inertia"));

        Assert.StartsWith(
            "<head><link rel=\"stylesheet\" href=\"/build/assets/app-1.css\">\n<script type=\"module\" src=\"/build/assets/app-1.js\"></script>\n<script type=\"module\" src=\"/build/assets/b.js\"></script>\n</head><script data-page",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dev_server_renders_client_entries_and_react_preamble()
    {
        _root.Write("wwwroot/hot", "http://localhost:5173");
        var html = await RenderAsync(Create("@viteReactRefresh@vite(\"src/app.tsx\")@inertia", environment: "Development"));

        Assert.StartsWith("<script type=\"module\">import RefreshRuntime from 'http://localhost:5173/@react-refresh'", html, StringComparison.Ordinal);
        Assert.Contains("<script type=\"module\" src=\"http://localhost:5173/@vite/client\"></script>\n<script type=\"module\" src=\"http://localhost:5173/src/app.tsx\"></script>\n<script data-page", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_vite_entry_fails_the_render()
    {
        Manifest();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RenderAsync(Create("@vite(\"nope.ts\")@inertia")));
        Assert.Contains("src/app.tsx", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ViewData_is_html_encoded_and_missing_keys_render_empty()
    {
        var harness = Create("<title>@viewData(\"title\")</title><meta content='@viewData('other')'>@viewData(\"n\")|@viewData(\"none\")@inertia");
        var html = await RenderAsync(harness, Render("Home", null).WithViewData("title", "<b>\"Fish\" & chips</b>").WithViewData("other", "x'y").WithViewData("n", 42));

        Assert.StartsWith("<title>&lt;b&gt;&quot;Fish&quot; &amp; chips&lt;/b&gt;</title><meta content='x&#x27;y'>42|<script", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithRootView_picks_another_template()
    {
        _root.Write("Views/admin.html", "ADMIN @inertia");
        var html = await RenderAsync(Create("DEFAULT @inertia"), Render("Home", null).WithRootView("Views/admin.html"));
        Assert.StartsWith("ADMIN <script", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Template_reloads_in_development_only()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var path = _root.Write("app.html", "ONE @inertia");
        var dev = new Harness(contentRoot: _root.Path, environment: "Development", timeProvider: clock);
        var prod = new Harness(contentRoot: _root.Path, timeProvider: clock);
        Assert.StartsWith("ONE ", await RenderAsync(dev), StringComparison.Ordinal);
        Assert.StartsWith("ONE ", await RenderAsync(prod), StringComparison.Ordinal);

        File.WriteAllText(path, "TWO @inertia");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.StartsWith("TWO ", await RenderAsync(dev), StringComparison.Ordinal);
        Assert.StartsWith("ONE ", await RenderAsync(prod), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ssr_renderer_supplies_head_and_body()
    {
        var harness = Create("<head>@inertiaHead</head><body>@inertia</body>", services: s => s.AddSingleton<IInertiaSsrRenderer>(new FakeSsr(new SsrRender("<title>SSR</title>", "<div>ssr é</div>"))));
        Assert.Equal("<head><title>SSR</title></head><body><div>ssr é</div></body>", await RenderAsync(harness));
    }

    [Fact]
    public async Task Ssr_fallback_renders_the_client_body_and_an_empty_head()
    {
        var harness = Create("<head>@inertiaHead</head><body>@inertia</body>", services: s => s.AddSingleton<IInertiaSsrRenderer>(new FakeSsr(null)));
        var html = await RenderAsync(harness);
        Assert.StartsWith("<head></head><body><script data-page=\"app\"", html, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private sealed class FakeSsr(SsrRender? result) : IInertiaSsrRenderer
    {
        public ValueTask<SsrRender?> RenderAsync(InertiaRootViewContext context) => ValueTask.FromResult(result);
    }
}
