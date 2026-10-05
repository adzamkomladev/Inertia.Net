using Microsoft.AspNetCore.Http;

namespace Inertia.Net.Tests;

public class BackResultTests
{
    private readonly Harness _harness = new();

    [Theory]
    [InlineData("example.com", "https://example.com/users?page=2", "https://example.com/users?page=2")]
    [InlineData("example.com", "http://example.com/users", "http://example.com/users")] // scheme is not compared (TLS-terminating proxies)
    [InlineData("EXAMPLE.com", "https://example.com/a", "https://example.com/a")]
    [InlineData("example.com:8080", "http://example.com:8080/a", "http://example.com:8080/a")]
    [InlineData("example.com", "https://example.com//evil.com/x", "https://example.com//evil.com/x")] // stays absolute: never protocol-relative
    [InlineData("example.com", "https://example.com/café", "https://example.com/caf%C3%A9")]
    [InlineData("example.com", "https://evil.com/a", "/fallback")]
    [InlineData("example.com", "https://example.com.evil.com/a", "/fallback")]
    [InlineData("example.com:8080", "http://example.com:9090/a", "/fallback")]
    [InlineData("example.com:8080", "http://example.com/a", "/fallback")]
    [InlineData("example.com", "javascript:alert(1)", "/fallback")]
    [InlineData("example.com", "ftp://example.com/a", "/fallback")]
    [InlineData("example.com", "/relative", "/fallback")]
    [InlineData("example.com", null, "/fallback")]
    public async Task Only_a_same_host_referer_is_followed(string host, string? referer, string expected)
    {
        var context = _harness.Context("/form", "POST");
        context.Request.Host = new HostString(host);
        if (referer is not null)
        {
            context.Request.Headers.Referer = referer;
        }

        await Harness.ExecuteAsync(context, Inertia.Back("/fallback"));

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal(expected, context.Response.Headers.Location);
    }

    [Fact]
    public async Task Errors_and_flash_go_to_the_feature()
    {
        var context = _harness.Context("/form", "POST");
        await Harness.ExecuteAsync(context, Inertia.Back()
            .WithErrors(new Dictionary<string, string> { ["name"] = "Required" })
            .WithErrors(new Dictionary<string, string[]> { ["email"] = ["Taken"] }, "login")
            .WithFlash("toast", "Nope"));

        var feature = context.Inertia();
        Assert.Equal("/", context.Response.Headers.Location);
        Assert.Equal(["Required"], feature.Errors!["default"]["name"]);
        Assert.Equal(["Taken"], feature.Errors!["login"]["email"]);
        Assert.Equal("Nope", feature.FlashData!["toast"]);
    }

    [Fact]
    public async Task A_plain_back_does_not_create_the_feature()
    {
        var context = _harness.Context();
        await Harness.ExecuteAsync(context, Inertia.Back());
        Assert.Null(context.Features.Get<InertiaFeature>());
    }
}
