using Microsoft.AspNetCore.Http;

namespace Inertia.Net.Testing.Tests;

public sealed class XsrfAndErrorTests
{
    [Fact]
    public async Task Xsrf_helpers_decode_the_cookie_and_preserve_the_cookie_header()
    {
        await using var server = await FakeServer.StartAsync(context =>
        {
            context.Response.Headers.SetCookie = ["other=one; Expires=Wed, 09 Jun 2027 10:18:14 GMT; Path=/", "XSRF-TOKEN=a%2Bb%2Fc%3D; Path=/"];
            return Task.CompletedTask;
        });
        using var response = await server.Client.GetAsync("/");
        Assert.Equal("a+b/c=", response.GetXsrfToken());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/");
        request.Headers.Add("Cookie", "antiforgery=paired; other=one");
        request.Headers.Add("X-XSRF-TOKEN", "old");
        Assert.Same(request, request.WithXsrfToken(response));
        using var submitted = await server.Client.SendAsync(request);
        Assert.Equal("a+b/c=", server.Requests[^1].Headers["X-XSRF-TOKEN"]);
        Assert.Equal("antiforgery=paired; other=one", server.Requests[^1].Headers["Cookie"]);
    }

    [Fact]
    public async Task Custom_cookie_and_header_names_are_supported()
    {
        await using var server = await FakeServer.StartAsync(context =>
        {
            context.Response.Headers.SetCookie = ["XSRF-TOKEN=default; Path=/", "CSRF=custom%3D; Path=/"];
            return Task.CompletedTask;
        });
        using var response = await server.Client.GetAsync("/");
        Assert.Equal("custom=", response.GetXsrfToken("CSRF"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/");
        request.WithXsrfToken(response, cookieName: "CSRF", headerName: "X-CSRF");
        using var submitted = await server.Client.SendAsync(request);
        Assert.Equal("custom=", server.Requests[^1].Headers["X-CSRF"]);
        Assert.DoesNotContain("X-XSRF-TOKEN", server.Requests[^1].Headers.Keys);
    }

    [Fact]
    public async Task A_missing_cookie_has_an_actionable_failure()
    {
        await using var server = await FakeServer.StartAsync(_ => Task.CompletedTask);
        using var response = await server.Client.GetAsync("/");
        var error = Assert.Throws<InvalidOperationException>(() => response.GetXsrfToken("CSRF"));
        Assert.Contains("CSRF", error.Message, StringComparison.Ordinal);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/");
        Assert.ThrowsAny<ArgumentException>(() => request.WithXsrfToken(""));
        Assert.ThrowsAny<ArgumentException>(() => request.WithXsrfToken("token", headerName: ""));
    }

    [Fact]
    public async Task Error_assertions_support_flat_dotted_fields_and_all_errors_arrays()
    {
        await using var server = await ErrorsAsync("""{"email":"Required","items.0.email":["Required","Invalid email"]}""");
        using var response = await server.Client.InertiaGetAsync("/");
        var page = await response.GetInertiaPageAsync();
        var assert = new AssertableInertia(page);
        Assert.Same(assert, assert.HasErrors().HasError("email").HasError("email", "Required")
            .HasError("items.0.email").HasError("items.0.email", "Invalid email"));
        Assert.Throws<InertiaAssertionException>(() => assert.HasError("missing"));
        Assert.Throws<InertiaAssertionException>(() => assert.HasError("email", "Other"));
        Assert.Throws<InertiaAssertionException>(() => assert.HasError("items.0.email", "Other"));
    }

    [Fact]
    public async Task Bag_assertions_keep_bag_names_and_field_names_separate()
    {
        await using var server = await ErrorsAsync("""{"newsletter.v2":{"items.0.email":["Required","Invalid email"]}}""");
        using var response = await server.Client.InertiaGetAsync("/");
        var assert = new AssertableInertia(await response.GetInertiaPageAsync());
        assert.HasErrors().HasErrors("newsletter.v2").HasErrorBag("newsletter.v2")
            .HasError("items.0.email", bag: "newsletter.v2")
            .HasError("items.0.email", "Invalid email", bag: "newsletter.v2");
        Assert.Throws<InertiaAssertionException>(() => assert.HasError("items.0.email"));
        Assert.Throws<InertiaAssertionException>(() => assert.HasErrorBag("missing"));
        Assert.Throws<InertiaAssertionException>(() => assert.HasErrors("missing"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"newsletter\":{}}")]
    [InlineData("{\"email\":null}")]
    [InlineData("{\"email\":[]}")]
    public async Task Empty_errors_do_not_pass_error_assertions(string errors)
    {
        await using var server = await ErrorsAsync(errors);
        using var response = await server.Client.InertiaGetAsync("/");
        var assert = new AssertableInertia(await response.GetInertiaPageAsync());
        Assert.Throws<InertiaAssertionException>(() => assert.HasErrors());
        Assert.Throws<InertiaAssertionException>(() => assert.HasError("email"));
        Assert.Throws<InertiaAssertionException>(() => assert.HasErrorBag("newsletter"));
    }

    private static Task<FakeServer> ErrorsAsync(string errors) => FakeServer.StartAsync(context =>
    {
        context.Response.Headers["X-Inertia"] = "true";
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync($$"""{"component":"Form","props":{"errors":{{errors}}},"url":"/"}""");
    });
}
