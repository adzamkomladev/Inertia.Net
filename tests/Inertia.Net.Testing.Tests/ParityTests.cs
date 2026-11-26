using System.Text.Json.Nodes;

namespace Inertia.Net.Testing.Tests;

public sealed class ParityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Big_integer_assertions_decode_only_when_the_page_opts_in(bool preserve)
    {
        using var response = ParsingTests.JsonResponse("""
            {"component":"Users/Index","props":{"id":{"$bigint":"9007199254740993"},"nested":{"id":{"$bigint":"-9007199254740993"}}},
             "flash":{"id":{"$bigint":"9007199254740993"}},"url":"/","preserveBigIntegers":__PRESERVE__}
            """.Replace("__PRESERVE__", preserve.ToString().ToLowerInvariant(), StringComparison.Ordinal));
        var page = await response.GetInertiaPageAsync();
        var assertions = new AssertableInertia(page);
        if (preserve)
            assertions.Where("id", 9007199254740993L).Has("nested", scope => scope.Where("id", -9007199254740993L))
                .HasFlash("id", 9007199254740993L);
        else
            assertions.Where("id", JsonNode.Parse("""{"$bigint":"9007199254740993"}"""))
                .HasFlash("id", JsonNode.Parse("""{"$bigint":"9007199254740993"}"""));
        Assert.IsType<JsonObject>(page.Props["id"]); // The raw page remains untouched.
    }

    [Fact]
    public async Task Component_checks_respect_directories_extensions_and_explicit_overrides()
    {
        var directory = Path.Combine(Path.GetTempPath(), "inertia-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "Users"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "Users", "Index.vue"), "");
            using var response = ParsingTests.JsonResponse("""{"component":"Users/Index","props":{},"url":"/"}""");
            var page = await response.GetInertiaPageAsync();
            var options = new InertiaTestingOptions { EnsurePagesExist = true };
            options.PageDirectories.Add(directory);
            new AssertableInertia(page, options).Component("Users/Index");
            options.PageExtensions.Clear();
            options.PageExtensions.Add(".tsx");
            Assert.Throws<InertiaAssertionException>(() => new AssertableInertia(page, options).Component("Users/Index"));
            new AssertableInertia(page, options).Component("Users/Index", false);
            options.EnsurePagesExist = false;
            Assert.Throws<InertiaAssertionException>(() => new AssertableInertia(page, options).Component("Users/Index", true));
            new AssertableInertia(page).Component("Users/Index");
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Users", "Index.vue"));
            Directory.Delete(Path.Combine(directory, "Users"));
            Directory.Delete(directory);
        }
    }
}
