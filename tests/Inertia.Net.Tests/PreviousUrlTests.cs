using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Inertia.Net.Tests;

public class PreviousUrlTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_page_remembers_the_encoded_relative_url(bool inertia)
    {
        var harness = new Harness(o => o.State.RememberPreviousUrl = true);
        var context = harness.Context("/users?name=a%2Fb&x=1");
        context.Request.PathBase = "/app";
        context.Request.Scheme = "https";
        if (inertia) context.AsInertia();
        await Harness.ExecuteAsync(context, Inertia.Render("Users"));
        var cookie = SetCookieHeaderValue.Parse(Assert.Single(context.Response.Headers.SetCookie)!);
        Assert.Equal(".Inertia.Previous", cookie.Name.Value);
        Assert.Equal("/app/users?name=a%2Fb&x=1", Uri.UnescapeDataString(cookie.Value.Value!));
        Assert.Equal("/", cookie.Path.Value);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
        Assert.Null(cookie.Expires);
        Assert.Null(cookie.MaxAge);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("partial")]
    [InlineData("prefetch")]
    [InlineData("precognition")]
    [InlineData("post")]
    [InlineData("error")]
    [InlineData("unchanged")]
    public async Task Ineligible_renders_do_not_write_a_cookie(string reason)
    {
        var harness = new Harness(o => o.State.RememberPreviousUrl = reason != "off");
        var context = harness.Context("/users", reason == "post" ? "POST" : "GET");
        context.AsInertia();
        if (reason == "partial") context.WithHeader(InertiaHeaders.PartialComponent, "Users");
        if (reason == "prefetch") context.WithHeader(InertiaHeaders.Purpose, "prefetch");
        if (reason == "precognition") context.WithHeader("Precognition", "true");
        if (reason == "unchanged") context.Request.Headers.Cookie = ".Inertia.Previous=%2Fusers";
        await Harness.ExecuteAsync(context, Inertia.Render("Users").WithStatusCode(reason == "error" ? 404 : 200));
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }

    [Theory]
    [InlineData("/users?x=1", null, true, "/users?x=1")]
    [InlineData("/users", "https://example.com/form", true, "https://example.com/form")]
    [InlineData("/users", "https://evil.example/form", true, "/users")]
    [InlineData("//evil.example", null, true, "/fallback")]
    [InlineData("/\t/evil.example", null, true, "/fallback")]
    [InlineData("/\t\\evil.example", null, true, "/fallback")]
    [InlineData("/\\evil.example", null, true, "/fallback")]
    [InlineData("/users\nother", null, true, "/fallback")]
    [InlineData("/users\rother", null, true, "/fallback")]
    [InlineData("/users\0other", null, true, "/fallback")]
    [InlineData("https://evil.example", null, true, "/fallback")]
    [InlineData("", null, true, "/fallback")]
    [InlineData("/users", null, false, "/fallback")]
    public async Task Back_and_feature_validate_the_cookie(string value, string? referer, bool enabled, string expected)
    {
        var harness = new Harness(o => o.State.RememberPreviousUrl = enabled);
        var context = harness.Context("/submit", "POST");
        context.Request.Host = new HostString("example.com");
        context.Request.Headers.Cookie = ".Inertia.Previous=" + Uri.EscapeDataString(value);
        if (referer is not null) context.Request.Headers.Referer = referer;
        await Harness.ExecuteAsync(context, Inertia.Back("/fallback"));
        Assert.Equal(expected, context.Response.Headers.Location);
        Assert.Equal(expected == "/fallback" ? null : value, context.Inertia().PreviousUrl);
    }

    [Fact]
    public async Task Empty_response_uses_the_same_cookie_fallback()
    {
        var harness = new Harness(o => o.State.RememberPreviousUrl = true);
        var app = new ApplicationBuilder(harness.Services);
        app.UseInertia();
        app.Run(_ => Task.CompletedTask);
        var context = harness.Context("/submit", "PUT");
        context.AsInertia();
        context.Request.Headers.Cookie = ".Inertia.Previous=%2Fusers%3Fx%3D1";
        await app.Build()(context);
        Assert.Equal(303, context.Response.StatusCode);
        Assert.Equal("/users?x=1", context.Response.Headers.Location);
    }

    [Fact]
    public void Missing_cookie_returns_null()
    {
        var harness = new Harness(o => o.State.RememberPreviousUrl = true);
        Assert.Null(harness.Context().Inertia().PreviousUrl);
    }

    [Theory]
    [InlineData(CookieSecurePolicy.Always, "http", true)]
    [InlineData(CookieSecurePolicy.None, "https", false)]
    [InlineData(CookieSecurePolicy.SameAsRequest, "http", false)]
    public async Task Custom_name_and_secure_policy_are_honored(CookieSecurePolicy policy, string scheme, bool secure)
    {
        var harness = new Harness(o =>
        {
            o.State.RememberPreviousUrl = true;
            o.State.PreviousUrlCookieName = ".Previous";
            o.State.CookieSecurePolicy = policy;
        });
        var context = harness.Context("/users");
        context.Request.Scheme = scheme;
        context.WithHeader(InertiaHeaders.PartialComponent, "AnotherComponent");
        await Harness.ExecuteAsync(context, Inertia.Render("Users"));
        var cookie = SetCookieHeaderValue.Parse(Assert.Single(context.Response.Headers.SetCookie)!);
        Assert.Equal(".Previous", cookie.Name.Value);
        Assert.Equal(secure, cookie.Secure);
        var next = harness.Context();
        next.Request.Headers.Cookie = ".Previous=%2Fusers";
        Assert.Equal("/users", next.Inertia().PreviousUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".Inertia.State")]
    public void Invalid_cookie_name_is_rejected(string name)
    {
        var options = new InertiaOptions();
        options.State.PreviousUrlCookieName = name;
        Assert.True(new InertiaOptionsValidator().Validate(null, options).Failed);
    }
}
