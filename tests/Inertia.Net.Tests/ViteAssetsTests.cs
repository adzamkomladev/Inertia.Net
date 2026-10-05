using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Inertia.Net.Tests;

/// <summary>A throw-away content root with helpers to write files into it.</summary>
internal sealed class TempRoot : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "inertia-tests-" + Guid.NewGuid().ToString("N"));

    public TempRoot() => Directory.CreateDirectory(Path);

    public string Write(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

public sealed class ViteAssetsTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly FakeTimeProvider _time = new();

    public void Dispose() => _root.Dispose();

    private ViteAssets Create(Action<ViteOptions>? configure = null, string environment = "Production")
    {
        var options = new InertiaOptions();
        configure?.Invoke(options.Vite);
        return new ViteAssets(Options.Create(options), new TestHostEnvironment(_root.Path, environment), _time);
    }

    private static HttpContext Ctx(string pathBase = "") => new DefaultHttpContext { Request = { PathBase = pathBase } };

    private void Manifest(string json, string path = "wwwroot/build/.vite/manifest.json") => _root.Write(path, json);

    private static string[] Lines(string tags) => tags.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private const string Sample = """
        {
          "src/app.tsx": { "file": "assets/app-1.js", "src": "src/app.tsx", "isEntry": true, "css": ["assets/app-1.css"], "imports": ["_vendor.js", "_ui.js"] },
          "_vendor.js": { "file": "assets/vendor-2.js", "css": ["assets/vendor-2.css"], "imports": ["_ui.js"] },
          "_ui.js": { "file": "assets/ui-3.js", "css": ["assets/app-1.css", "assets/ui-3.css"], "imports": ["_vendor.js"] },
          "src/admin.tsx": { "file": "assets/admin-4.js", "isEntry": true, "imports": ["_vendor.js"] },
          "src/app.css": { "file": "assets/style-5.css", "isEntry": true },
          "src/lazy.tsx": { "file": "assets/lazy-6.js", "isDynamicEntry": true }
        }
        """;

    [Fact]
    public void Production_tags_collect_css_recursively_dedupe_and_survive_import_cycles()
    {
        Manifest(Sample);
        var tags = Create().RenderTags(Ctx(), "src/app.tsx");

        Assert.Equal(
            [
                """<link rel="stylesheet" href="/build/assets/app-1.css">""",
                """<link rel="stylesheet" href="/build/assets/vendor-2.css">""",
                """<link rel="stylesheet" href="/build/assets/ui-3.css">""",
                """<script type="module" src="/build/assets/app-1.js"></script>""",
                """<link rel="modulepreload" href="/build/assets/vendor-2.js">""",
                """<link rel="modulepreload" href="/build/assets/ui-3.js">""",
            ],
            Lines(tags));
    }

    [Fact]
    public void Multiple_entries_share_deduplication_and_css_entries_become_stylesheets()
    {
        Manifest(Sample);
        var lines = Lines(Create().RenderTags(Ctx(), "src/app.tsx", "src/admin.tsx", "src/app.css"));

        Assert.Equal(lines.Length, lines.Distinct().Count());
        Assert.Contains("""<script type="module" src="/build/assets/admin-4.js"></script>""", lines);
        Assert.Contains("""<link rel="stylesheet" href="/build/assets/style-5.css">""", lines);
        Assert.Single(lines, l => l.Contains("vendor-2.js", StringComparison.Ordinal));
    }

    [Fact]
    public void Legacy_manifest_location_is_used_as_a_fallback()
    {
        Manifest(Sample, "wwwroot/build/manifest.json");
        Assert.Contains("assets/app-1.js", Create().RenderTags(Ctx(), "src/app.tsx"), StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_manifest_path_is_relative_to_the_content_root()
    {
        Manifest(Sample, "dist/m.json");
        Assert.Contains("assets/app-1.js", Create(v => v.ManifestPath = "dist/m.json").RenderTags(Ctx(), "src/app.tsx"), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_entry_lists_the_available_entries()
    {
        Manifest(Sample);
        var ex = Assert.Throws<InvalidOperationException>(() => Create().RenderTags(Ctx(), "src/nope.tsx"));

        Assert.Contains("src/nope.tsx", ex.Message, StringComparison.Ordinal);
        Assert.Contains("src/app.tsx, src/admin.tsx, src/app.css", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("src/lazy.tsx", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_manifest_in_production_explains_where_it_looked()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create().RenderTags(Ctx(), "src/app.tsx"));
        Assert.Contains(Path.Combine("build", ".vite", "manifest.json"), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PathBase_and_AssetBaseUrl_shape_the_asset_urls()
    {
        Manifest(Sample);
        Manifest(Sample, "wwwroot/dist/.vite/manifest.json");
        Assert.Contains("""src="/shop/build/assets/app-1.js""", Create().RenderTags(Ctx("/shop"), "src/app.tsx"), StringComparison.Ordinal);
        Assert.Contains("""src="https://cdn.example.com/b/assets/app-1.js""", Create(v => v.AssetBaseUrl = "https://cdn.example.com/b/").RenderTags(Ctx("/shop"), "src/app.tsx"), StringComparison.Ordinal);
        Assert.Contains("""src="/shop/dist/assets/app-1.js""", Create(v => v.BuildDirectory = "dist").RenderTags(Ctx("/shop"), "src/app.tsx"), StringComparison.Ordinal);
    }

    [Fact]
    public void Production_tags_are_cached_per_entries_and_path_base()
    {
        Manifest(Sample);
        var vite = Create();
        Assert.Same(vite.RenderTagsUtf8(Ctx(), ["src/app.tsx"]), vite.RenderTagsUtf8(Ctx(), ["src/app.tsx"]));
        Assert.NotSame(vite.RenderTagsUtf8(Ctx(), ["src/app.tsx"]), vite.RenderTagsUtf8(Ctx("/x"), ["src/app.tsx"]));
    }

    [Fact]
    public void Production_never_rereads_the_manifest()
    {
        var path = _root.Write("wwwroot/build/.vite/manifest.json", Sample);
        var vite = Create();
        var first = vite.RenderTags(Ctx(), "src/app.tsx");

        File.WriteAllText(path, """{ "src/app.tsx": { "file": "assets/other.js", "isEntry": true } }""");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(first, vite.RenderTags(Ctx(), "src/app.tsx"));
    }

    [Fact]
    public void Development_reloads_the_manifest_when_it_changes()
    {
        var path = _root.Write("wwwroot/build/.vite/manifest.json", Sample);
        var vite = Create(environment: "Development");
        Assert.Contains("app-1.js", vite.RenderTags(Ctx(), "src/app.tsx"), StringComparison.Ordinal);

        File.WriteAllText(path, """{ "src/app.tsx": { "file": "assets/app-9.js", "isEntry": true } }""");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.Contains("app-1.js", vite.RenderTags(Ctx(), "src/app.tsx"), StringComparison.Ordinal); // throttled
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Contains("app-9.js", vite.RenderTags(Ctx(), "src/app.tsx"), StringComparison.Ordinal);
    }

    [Fact]
    public void Hot_file_switches_to_dev_server_tags()
    {
        _root.Write("wwwroot/hot", "http://localhost:5173/\n");
        var vite = Create(environment: "Development");

        Assert.True(vite.IsDevServerRunning);
        Assert.Equal(
            [
                """<script type="module" src="http://localhost:5173/@vite/client"></script>""",
                """<script type="module" src="http://localhost:5173/src/app.tsx"></script>""",
                """<link rel="stylesheet" href="http://localhost:5173/src/app.css">""",
            ],
            Lines(vite.RenderTags(Ctx("/shop"), "src/app.tsx", "/src/app.css")));
    }

    [Fact]
    public void Hot_file_appearing_and_disappearing_is_noticed_in_development()
    {
        Manifest(Sample);
        var vite = Create(environment: "Development");
        Assert.False(vite.IsDevServerRunning);

        var hot = _root.Write("wwwroot/hot", "http://localhost:5173");
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(vite.IsDevServerRunning);

        File.Delete(hot);
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(vite.IsDevServerRunning);
        Assert.Contains("/build/assets/app-1.js", vite.RenderTags(Ctx(), "src/app.tsx"), StringComparison.Ordinal);
    }

    [Fact]
    public void Hot_file_is_ignored_outside_development_but_DevServerUrl_always_wins()
    {
        _root.Write("wwwroot/hot", "http://localhost:5173");
        Assert.False(Create().IsDevServerRunning);

        var vite = Create(v => v.DevServerUrl = "https://dev.test:3000");
        Assert.True(vite.IsDevServerRunning);
        Assert.Contains("https://dev.test:3000/@vite/client", vite.RenderTags(Ctx(), "src/app.tsx"), StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_hot_file_path_is_used()
    {
        _root.Write("run/vite.url", "http://localhost:1234");
        Assert.True(Create(v => v.HotFilePath = "run/vite.url", "Development").IsDevServerRunning);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://x/'; alert(1); //")]
    [InlineData("not a url")]
    public void Suspicious_hot_file_content_is_rejected(string content)
    {
        _root.Write("wwwroot/hot", content);
        Assert.Throws<InvalidOperationException>(() => Create(environment: "Development").RenderReactRefresh(Ctx()));
    }

    [Fact]
    public void React_refresh_preamble_is_dev_only()
    {
        Manifest(Sample);
        Assert.Equal("", Create().RenderReactRefresh(Ctx()));
        Assert.Equal("", Create(environment: "Development").RenderReactRefresh(Ctx()));

        _root.Write("wwwroot/hot", "http://localhost:5173");
        Assert.Equal(
            "<script type=\"module\">import RefreshRuntime from 'http://localhost:5173/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$ = () => {};window.$RefreshSig$ = () => (type) => type;window.__vite_plugin_react_preamble_installed__ = true;</script>\n",
            Create(environment: "Development").RenderReactRefresh(Ctx()));
    }

    [Fact]
    public void No_entries_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => Create().RenderTags(Ctx()));
    }
}
