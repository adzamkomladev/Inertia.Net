using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Inertia.Net.Testing.Tests;

public class PaginationTests
{
    [Theory]
    [InlineData("{\"pageName\":\"p\",\"currentPage\":1,\"nextPage\":2}", "p=2")]
    [InlineData("{\"pageName\":\"cursor\",\"currentPage\":\"old\",\"nextPage\":\"é+/=\"}", "cursor=%C3%A9%2B%2F%3D")]
    public async Task Next_page_replaces_the_page_parameter_and_preserves_filters(string metadata, string expectedQuery)
    {
        await using var server = await FakeServer.StartAsync();
        using var response = ParsingTests.JsonResponse($$$"""
            {"component":"Users/Index","props":{},"url":"/users?filter=active&p=1&cursor=old","version":"v1","scrollProps":{"users":{{{metadata}}}}}
            """);
        var page = await response.GetInertiaPageAsync();
        var next = await page.LoadNextPageAsync(server.Client, "users");
        var request = Assert.Single(server.Requests);
        Assert.Contains(expectedQuery, request.PathAndQuery);
        Assert.Contains("filter=active", request.PathAndQuery);
        Assert.Equal("users", request.Headers["X-Inertia-Partial-Data"]);
        Assert.Equal("Users/Index", request.Headers["X-Inertia-Partial-Component"]);
        Assert.Equal("v1", request.Headers["X-Inertia-Version"]);
        Assert.False(request.Headers.ContainsKey("X-Inertia-Infinite-Scroll-Merge-Intent"));
        Assert.True(next.HasProp("users"));
    }

    [Fact]
    public async Task Previous_page_requests_prepend_with_the_previous_cursor()
    {
        await using var server = await FakeServer.StartAsync();
        using var response = ParsingTests.JsonResponse("""
            {"component":"Users/Index","props":{},"url":"/users?cursor=current&filter=active","scrollProps":{"users":{"pageName":"cursor","previousPage":"before+/="}}}
            """);
        var page = await response.GetInertiaPageAsync();
        await page.LoadPreviousPageAsync(server.Client, "users");
        var request = Assert.Single(server.Requests);
        Assert.Equal("/users?cursor=before%2B%2F%3D&filter=active", request.PathAndQuery);
        Assert.Equal("prepend", request.Headers["X-Inertia-Infinite-Scroll-Merge-Intent"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"users\":{\"pageName\":\"page\",\"nextPage\":null,\"previousPage\":null}}")]
    public async Task Missing_navigation_throws_an_assertion(string metadata)
    {
        using var client = new HttpClient();
        using var response = ParsingTests.JsonResponse($$$"""
            {"component":"Users/Index","props":{},"url":"/users","scrollProps":{{{metadata}}}}
            """);
        var page = await response.GetInertiaPageAsync();
        await Assert.ThrowsAsync<InertiaAssertionException>(() => page.LoadNextPageAsync(client, "users"));
        await Assert.ThrowsAsync<InertiaAssertionException>(() => page.LoadPreviousPageAsync(client, "users"));
    }

    [Fact]
    public async Task Helpers_follow_real_numbered_scroll_pages_in_both_directions()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddInertia();
        await using var app = builder.Build();
        app.UseInertia();
        app.MapGet("/paged/scroll", (PageRequest request) => Inertia.Render("Posts/Index", new InertiaProps
        {
            ["posts"] = Inertia.Scroll(Pagination.Paginate(Enumerable.Range(1, 25).ToArray(), request)),
        }));
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.InertiaGetAsync("/paged/scroll?perPage=10&filter=active");
        var first = await response.GetInertiaPageAsync();
        new AssertableInertia(first).HasScroll("posts", 1, 2);
        var second = await first.LoadNextPageAsync(client, "posts");
        new AssertableInertia(second).HasScroll("posts", 2, 3).Where("posts.data.0", 11);
        Assert.Equal("/paged/scroll?perPage=10&filter=active&page=2", second.Url);
        var previous = await second.LoadPreviousPageAsync(client, "posts");
        new AssertableInertia(previous).HasScroll("posts", 1, 2).Where("posts.data.0", 1);
        Assert.Equal(new[] { "posts.data" }, previous.PrependProps);
        var last = await second.LoadNextPageAsync(client, "posts");
        new AssertableInertia(last).HasScroll("posts", 3, null).Has("posts.data", 5);
        await Assert.ThrowsAsync<InertiaAssertionException>(() => last.LoadNextPageAsync(client, "posts"));
    }

    [Fact]
    public async Task Scroll_assertions_compare_values_and_use_literal_metadata_paths()
    {
        using var response = ParsingTests.JsonResponse("""
            {"component":"A","props":{},"url":"/","scrollProps":{"feed.posts":{"currentPage":"opaque","nextPage":null}}}
            """);
        var page = await response.GetInertiaPageAsync();
        var assertion = new AssertableInertia(page);
        Assert.Same(assertion, assertion.HasScroll("feed.posts").HasScroll("feed.posts", "opaque", null));
        Assert.Throws<InertiaAssertionException>(() => assertion.HasScroll("missing"));
        Assert.Throws<InertiaAssertionException>(() => assertion.HasScroll("feed.posts", 1, null));
        Assert.Throws<InertiaAssertionException>(() => assertion.HasScroll("feed.posts", "opaque", "next"));
    }
}
