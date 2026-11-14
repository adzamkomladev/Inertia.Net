using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

/// <summary><see cref="InertiaHead"/>: server-managed head tags in the <c>head</c> prop and in the initial HTML.</summary>
public sealed class HeadTests : IDisposable
{
    private readonly TempRoot _root = new();

    public void Dispose() => _root.Dispose();

    private static string[] Head(System.Text.Json.Nodes.JsonObject page, string prop = "head") =>
        [.. page["props"]![prop]!.AsArray().Select(t => (string)t!)];

    [Fact]
    public async Task Every_builder_encodes_names_and_values()
    {
        var page = await new Harness().PageAsync(null, c => c.Inertia().Head
            .Title("Tom & \"Jerry\" <3 é")
            .Meta("description", "a < b & \"c\"")
            .Property("og:title", "x\"><script>")
            .Link("canonical", "https://e.test/?a=1&b=\"2\""));

        Assert.Equal(
            [
                "<title data-inertia=\"title\">Tom &amp; &quot;Jerry&quot; &lt;3 &#xE9;</title>",
                "<meta name=\"description\" content=\"a &lt; b &amp; &quot;c&quot;\" data-inertia=\"description\">",
                "<meta property=\"og:title\" content=\"x&quot;&gt;&lt;script&gt;\" data-inertia=\"og:title\">",
                "<link rel=\"canonical\" href=\"https://e.test/?a=1&amp;b=&quot;2&quot;\" data-inertia=\"canonical\">",
            ],
            Head(page));
    }

    [Fact]
    public async Task Response_tags_replace_shared_ones_with_the_same_key_and_keep_the_order()
    {
        var harness = new Harness(o => o.Head.TitleTemplate = "{0} - App");
        var context = harness.Context();
        context.Inertia().Head.Title("Shared").Meta("description", "shared").Meta("robots", "index");
        var page = Harness.ParsePage(await Harness.ExecuteAsync(context, Render("TestComponent", null)
            .WithHead(h => h.Meta("description", "page").Meta("description", "page 2").Title("Post").Remove("robots"))));

        Assert.Equal(["<title data-inertia=\"title\">Post - App</title>", "<meta name=\"description\" content=\"page 2\" data-inertia=\"description\">"], Head(page));
    }

    [Fact]
    public async Task Without_tags_there_is_no_prop_and_the_template_needs_a_title()
    {
        var harness = new Harness(o => o.Head.TitleTemplate = "{0} - App");
        Assert.Null((await harness.PageAsync(new InertiaProps { ["a"] = 1 }))["props"]!["head"]);
        Assert.Equal(["<meta name=\"robots\" content=\"none\" data-inertia=\"robots\">"],
            Head(await harness.PageAsync(null, c => c.Inertia().Head.Meta("robots", "none"))));
    }

    [Fact]
    public async Task The_prop_is_sent_on_partial_reloads_and_deferred_loads_under_its_configured_name()
    {
        var harness = new Harness(o => o.Head.PropName = "meta");
        var partial = await harness.PageAsync(new InertiaProps { ["a"] = 1, ["later"] = Defer(() => 2) }, c =>
        {
            c.AsPartial(only: "later");
            c.Inertia().Head.Title("T");
        });

        Assert.Equal(["<title data-inertia=\"title\">T</title>"], Head(partial, "meta"));
        Assert.Null(partial["props"]!["a"]);
        Assert.Equal(2, (int?)partial["props"]!["later"]);
    }

    [Fact]
    public void A_title_template_without_a_placeholder_or_an_empty_prop_name_fails_validation()
    {
        Assert.ThrowsAny<Exception>(() => new Harness(o => o.Head.TitleTemplate = "App").Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<InertiaOptions>>().Value);
        Assert.ThrowsAny<Exception>(() => new Harness(o => o.Head.PropName = "").Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<InertiaOptions>>().Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_initial_html_has_the_tags_unless_ssr_wrote_the_head(bool ssr)
    {
        _root.Write("app.html", "<head>@inertiaHead</head><body>@inertia</body>");
        var harness = new Harness(contentRoot: _root.Path, configureServices: s => s.AddSingleton<IInertiaSsrRenderer>(new FakeSsr(ssr ? new SsrRender("<title data-inertia=\"title\">SSR</title>", "<div>ssr</div>") : null)));
        var html = await Harness.ExecuteAsync(harness.Context(), Render("Home", null).WithHead(h => h.Title("T").Meta("description", "d")));

        if (ssr)
        {
            Assert.StartsWith("<head><title data-inertia=\"title\">SSR</title></head>", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.StartsWith("<head><title data-inertia=\"title\">T</title>\n<meta name=\"description\" content=\"d\" data-inertia=\"description\"></head><body><script data-page", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Generated_types_declare_the_head_prop()
    {
        var options = new InertiaOptions();
        options.Head.PropName = "meta";
        Assert.Contains("  meta?: string[]\n", new TypeScriptGenerator(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web), options).Generate(), StringComparison.Ordinal);
    }

    private sealed class FakeSsr(SsrRender? result) : IInertiaSsrRenderer
    {
        public ValueTask<SsrRender?> RenderAsync(InertiaRootViewContext context) => ValueTask.FromResult(result);
    }
}
