using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

/// <summary><see cref="InertiaOptions.CspNonce"/> and <see cref="ViteOptions.Prefetch"/>.</summary>
public sealed class CspAndPrefetchTests : IDisposable
{
    private const string Manifest = """
        {
          "src/app.tsx": { "file": "assets/app-1.js", "isEntry": true, "css": ["assets/app-1.css"], "imports": ["_vendor.js"], "dynamicImports": ["src/pages/Home.tsx", "src/pages/Users.tsx"] },
          "_vendor.js": { "file": "assets/vendor-2.js" },
          "src/pages/Home.tsx": { "file": "assets/Home-3.js", "isDynamicEntry": true, "imports": ["_vendor.js", "_chart.js"] },
          "src/pages/Users.tsx": { "file": "assets/Users-4.js", "isDynamicEntry": true, "css": ["assets/Users-4.css"], "dynamicImports": ["src/pages/Deep.tsx"] },
          "src/pages/Deep.tsx": { "file": "assets/Deep-5.js", "isDynamicEntry": true },
          "_chart.js": { "file": "assets/chart-6.js" }
        }
        """;

    private readonly TempRoot _root = new();

    public CspAndPrefetchTests() => _root.Write("wwwroot/build/.vite/manifest.json", Manifest);

    public void Dispose() => _root.Dispose();

    private ViteAssets Create(Action<InertiaOptions>? configure = null, string environment = "Production")
    {
        var options = new InertiaOptions();
        configure?.Invoke(options);
        return new ViteAssets(Options.Create(options), new TestHostEnvironment(_root.Path, environment), new FakeTimeProvider());
    }

    private static HttpContext Context(string? nonce = null)
    {
        var context = new DefaultHttpContext();
        if (nonce is not null)
        {
            context.Items["nonce"] = nonce;
        }

        return context;
    }

    private static void UseNonce(InertiaOptions o) => o.CspNonce = http => http.Items["nonce"] as string;

    [Fact]
    public void Every_production_tag_carries_the_nonce_and_the_cached_tags_stay_plain()
    {
        var vite = Create(UseNonce);
        var tags = vite.RenderTags(Context("abc+/=="), "src/app.tsx");
        Assert.Equal(
            """
            <link rel="stylesheet" nonce="abc+/==" href="/build/assets/app-1.css">
            <script type="module" nonce="abc+/==" src="/build/assets/app-1.js"></script>
            <link rel="modulepreload" nonce="abc+/==" href="/build/assets/vendor-2.js">

            """.ReplaceLineEndings("\n"),
            tags);

        // A request without a nonce (and the next one with another) gets the same cached tags.
        Assert.DoesNotContain("nonce", vite.RenderTags(Context(), "src/app.tsx"), StringComparison.Ordinal);
        Assert.Contains("nonce=\"xyz\"", vite.RenderTags(Context("xyz"), "src/app.tsx"), StringComparison.Ordinal);
    }

    [Fact]
    public void Dev_server_tags_and_the_react_preamble_carry_the_nonce()
    {
        var vite = Create(o =>
        {
            UseNonce(o);
            o.Vite.DevServerUrl = "http://localhost:5173";
        });
        var tags = vite.RenderTags(Context("n1"), "src/app.tsx");
        Assert.Contains("<script type=\"module\" nonce=\"n1\" src=\"http://localhost:5173/@vite/client\"></script>", tags, StringComparison.Ordinal);
        Assert.Contains("<script type=\"module\" nonce=\"n1\" src=\"http://localhost:5173/src/app.tsx\"></script>", tags, StringComparison.Ordinal);
        Assert.StartsWith("<script type=\"module\" nonce=\"n1\">import RefreshRuntime", vite.RenderReactRefresh(Context("n1")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a\"b")]
    [InlineData("<script>")]
    [InlineData("abc===")]
    [InlineData("=")]
    public void Nonces_that_are_not_base64_are_refused(string nonce)
    {
        var vite = Create(UseNonce);
        Assert.Throws<InvalidOperationException>(() => vite.RenderTags(Context(nonce), "src/app.tsx"));
    }

    [Fact]
    public async Task The_page_script_carries_the_nonce()
    {
        var harness = new Harness(UseNonce);
        var context = harness.Context();
        context.Items["nonce"] = "r4nd0m";
        var html = await Harness.ExecuteAsync(context, Render("Home"));
        Assert.Contains("<script data-page=\"app\" nonce=\"r4nd0m\" type=\"application/json\">", html, StringComparison.Ordinal);

        var plain = await Harness.ExecuteAsync(harness.Context(), Render("Home"));
        Assert.Contains("<script data-page=\"app\" type=\"application/json\">", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void No_prefetch_script_by_default()
    {
        Assert.DoesNotContain("prefetch", Create().RenderTags(Context(), "src/app.tsx"), StringComparison.Ordinal);
    }

    [Fact]
    public void Waterfall_prefetches_the_dynamic_chunks_breadth_first_without_the_loaded_ones()
    {
        var tags = Create(o =>
        {
            UseNonce(o);
            o.Vite.Prefetch = VitePrefetch.Waterfall;
            o.Vite.PrefetchConcurrency = 2;
        }).RenderTags(Context("n0"), "src/app.tsx");

        var script = tags[tags.IndexOf("<script nonce=\"n0\">(()=>", StringComparison.Ordinal)..];
        Assert.Contains(
            """q=["/build/assets/Home-3.js","/build/assets/Users-4.js","/build/assets/Users-4.css","/build/assets/chart-6.js","/build/assets/Deep-5.js"]""",
            script,
            StringComparison.Ordinal);
        Assert.Contains("for(let i=0;i<2;i++)next()", script, StringComparison.Ordinal);
        Assert.Contains("l.rel='prefetch'", script, StringComparison.Ordinal);
        Assert.EndsWith("</script>\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggressive_starts_every_download_at_once()
    {
        var tags = Create(o => o.Vite.Prefetch = VitePrefetch.Aggressive).RenderTags(Context(), "src/app.tsx");
        Assert.Contains("<script>(()=>", tags, StringComparison.Ordinal);
        Assert.Contains("for(let i=0;i<5;i++)next()", tags, StringComparison.Ordinal);
    }

    [Fact]
    public void Prefetch_urls_follow_the_asset_base_url_and_cannot_close_the_script()
    {
        _root.Write("wwwroot/build/.vite/manifest.json", """
            {
              "src/app.tsx": { "file": "assets/app.js", "isEntry": true, "dynamicImports": ["src/x.tsx"] },
              "src/x.tsx": { "file": "assets/</script>.js", "isDynamicEntry": true }
            }
            """);
        var tags = Create(o =>
        {
            o.Vite.Prefetch = VitePrefetch.Waterfall;
            o.Vite.AssetBaseUrl = "https://cdn.example.com/build/";
        }).RenderTags(Context(), "src/app.tsx");
        Assert.Contains("\"https://cdn.example.com/build/assets/\\u003C/script\\u003E.js\"", tags, StringComparison.Ordinal);
        Assert.Equal(1, tags.Split("</script>").Length - 2); // the module script and the prefetch script close once each
    }

    [Fact]
    public void Prefetch_concurrency_must_be_positive()
    {
        var options = new InertiaOptions();
        options.Vite.PrefetchConcurrency = 0;
        Assert.Contains("Vite.PrefetchConcurrency must be positive.", new InertiaOptionsValidator().Validate(null, options).Failures!);
    }
}
