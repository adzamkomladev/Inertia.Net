using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Inertia.Net.Tests;

public class VersionProviderTests
{
    [Fact]
    public void No_manifest_and_no_version_is_empty()
    {
        var provider = Provider(new Harness(contentRoot: NewRoot()));
        Assert.Equal("", provider.GetVersion(new Harness().Context()));
    }

    [Fact]
    public void Default_is_a_stable_hash_of_the_vite_manifest()
    {
        var root = NewRoot();
        Write(root, "wwwroot/build/.vite/manifest.json", """{"a":1}""");
        var harness = new Harness(contentRoot: root);

        var version = Provider(harness).GetVersion(harness.Context());

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData("""{"a":1}"""u8)[..16]), version);
        Assert.Equal(version, Provider(new Harness(contentRoot: root)).GetVersion(harness.Context()));
    }

    [Fact]
    public void Legacy_manifest_location_and_custom_path_are_used()
    {
        var legacy = NewRoot();
        Write(legacy, "wwwroot/build/manifest.json", "{}");
        Assert.NotEqual("", Provider(new Harness(contentRoot: legacy)).GetVersion(new Harness().Context()));

        var custom = NewRoot();
        Write(custom, "dist/m.json", "{}");
        Assert.NotEqual("", Provider(new Harness(o => o.Vite.ManifestPath = "dist/m.json", contentRoot: custom)).GetVersion(new Harness().Context()));
    }

    [Fact]
    public void Explicit_version_wins_over_the_manifest_and_the_resolver_wins_over_both()
    {
        var root = NewRoot();
        Write(root, "wwwroot/build/.vite/manifest.json", "{}");
        Assert.Equal("v1", Provider(new Harness(o => o.Version = "v1", contentRoot: root)).GetVersion(new Harness().Context()));

        var harness = new Harness(o => { o.Version = "v1"; o.VersionResolver = _ => "v2"; }, contentRoot: root);
        Assert.Equal("v2", Provider(harness).GetVersion(harness.Context()));

        var fallsThrough = new Harness(o => { o.Version = "v1"; o.VersionResolver = _ => null; }, contentRoot: root);
        Assert.Equal("v1", Provider(fallsThrough).GetVersion(fallsThrough.Context()));
    }

    [Fact]
    public void Production_hashes_once_development_rehashes_on_change()
    {
        foreach (var environment in new[] { "Production", "Development" })
        {
            var root = NewRoot();
            var path = Write(root, "wwwroot/build/.vite/manifest.json", "{}");
            var time = new FakeTimeProvider();
            var harness = new Harness(contentRoot: root, environment: environment, timeProvider: time);
            var provider = Provider(harness);
            var before = provider.GetVersion(harness.Context());

            File.WriteAllText(path, """{"changed":true}""");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            time.Advance(TimeSpan.FromSeconds(2));

            var after = provider.GetVersion(harness.Context());
            Assert.Equal(environment == "Development", before != after);
        }
    }

    [Theory]
    [InlineData("v1", null, false)]
    [InlineData("v1", "v1", true)]
    [InlineData("v1", "v2", false)]
    [InlineData("v1", "V1", false)]
    [InlineData(null, null, true)]
    [InlineData(null, "", true)]
    [InlineData(null, "v1", false)]
    public void Matches_compares_the_header_ordinally_and_absent_means_empty(string? version, string? header, bool expected)
    {
        var harness = new Harness(o => o.Version = version, contentRoot: NewRoot());
        var context = harness.Context();
        if (header is not null)
        {
            context.Request.Headers[InertiaHeaders.Version] = header;
        }

        Assert.Equal(expected, Provider(harness).Matches(context));
    }

    private static VersionProvider Provider(Harness harness) => harness.Services.GetRequiredService<VersionProvider>();

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "inertia-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
