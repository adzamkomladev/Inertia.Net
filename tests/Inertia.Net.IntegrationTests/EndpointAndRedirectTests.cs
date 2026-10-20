using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

/// <summary><c>MapInertia</c>, external redirects, <see cref="InertiaOptions.UrlResolver"/> and shared once props.</summary>
public sealed class EndpointAndRedirectTests
{
    [Fact]
    public async Task Map_inertia_renders_the_component_for_get_and_head()
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        var client = app.CreateClient();

        var page = await (await client.InertiaGetAsync("/about")).PageAsync();
        Assert.Equal("About", (string?)page["component"]);
        Assert.Equal("Inertia.Net", (string?)page["props"]!["name"]);

        // The shared props object serves every request unchanged.
        var again = await (await client.InertiaGetAsync("/about")).PageAsync();
        Assert.Equal("Inertia.Net", (string?)again["props"]!["name"]);

        var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/about"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.InertiaAsync(HttpMethod.Post, "/about")).StatusCode);
    }

    [Theory]
    [InlineData("https://accounts.example.com/login")]
    [InlineData("//accounts.example.com/login")]
    [InlineData("https://localhost/same-host-other-scheme")]
    [InlineData("http://localhost:8080/other-port")]
    public async Task Redirects_to_another_origin_become_a_full_visit(string target)
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        var response = await app.CreateClient().InertiaGetAsync("/go?to=" + Uri.EscapeDataString(target));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(target, response.Header(InertiaHeaders.Location));
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("http://localhost/dashboard")]
    public async Task Same_origin_redirects_are_followed_as_usual(string target)
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        var response = await app.CreateClient().InertiaGetAsync("/go?to=" + Uri.EscapeDataString(target));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(target, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task External_redirects_are_kept_for_prefetches_plain_requests_and_when_turned_off()
    {
        const string target = "https://accounts.example.com/login";
        await using var app = await ConformanceApp.StartAsync<Host>();
        var client = app.CreateClient();
        var prefetch = await client.InertiaGetAsync("/go?to=" + Uri.EscapeDataString(target), r => r.WithHeader(InertiaHeaders.Purpose, "prefetch"));
        Assert.Equal(HttpStatusCode.Found, prefetch.StatusCode);
        var plain = await client.GetAsync("/go?to=" + Uri.EscapeDataString(target), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, plain.StatusCode);

        await using var off = await ConformanceApp.StartAsync<Host>(new ConformanceSetup { Inertia = o => o.ConvertExternalRedirects = false });
        Assert.Equal(HttpStatusCode.Found, (await off.CreateClient().InertiaGetAsync("/go?to=" + Uri.EscapeDataString(target))).StatusCode);
    }

    [Fact]
    public async Task An_external_redirect_carries_flash_to_the_next_page()
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        var client = app.CreateClient();
        var response = await client.InertiaGetAsync("/go?to=" + Uri.EscapeDataString("https://elsewhere.example/") + "&flash=bye");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var page = await (await client.InertiaGetAsync("/about")).PageAsync();
        Assert.Equal("bye", (string?)page["flash"]!["message"]);
    }

    [Fact]
    public async Task The_url_resolver_sets_the_page_url()
    {
        await using var app = await ConformanceApp.StartAsync<Host>(new ConformanceSetup { Inertia = o => o.UrlResolver = http => "/public" + http.Request.Path });
        var page = await (await app.CreateClient().InertiaGetAsync("/about")).PageAsync();
        Assert.Equal("/public/about", (string?)page["url"]);
    }

    [Fact]
    public async Task A_shared_once_prop_is_skipped_when_the_client_has_it()
    {
        var loads = 0;
        await using var app = await ConformanceApp.StartAsync<Host>(new ConformanceSetup { Inertia = o => o.Share("plans", Once(() => ++loads)) });
        var client = app.CreateClient();

        var first = await (await client.InertiaGetAsync("/about")).PageAsync();
        Assert.Equal(1, (int?)first["props"]!["plans"]);
        Assert.NotNull(first["onceProps"]!["plans"]);

        var second = await (await client.InertiaGetAsync("/about", r => r.WithHeader(InertiaHeaders.ExceptOnceProps, "plans"))).PageAsync();
        Assert.Null(second["props"]!["plans"]);
        Assert.Equal(1, loads);
    }

    public sealed class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services)
        {
        }

        public static void MapRoutes(WebApplication app)
        {
            app.MapInertia("/about", "About", new InertiaProps { ["name"] = "Inertia.Net" });
            app.MapGet("/go", (HttpContext http, string to, string? flash) =>
            {
                if (flash is not null)
                {
                    http.Inertia().Flash("message", flash);
                }

                return Results.Redirect(to);
            });
        }
    }
}
