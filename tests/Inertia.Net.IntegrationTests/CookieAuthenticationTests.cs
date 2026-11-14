using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

public sealed class MinimalApiCookieAuthenticationTests : CookieAuthenticationTests<MinimalApiHost>;

public sealed class MvcCookieAuthenticationTests : CookieAuthenticationTests<MvcHost>;

[Collection("FastEndpoints")] // FastEndpoints keeps its configuration in statics
public sealed class FastEndpointsCookieAuthenticationTests : CookieAuthenticationTests<FastEndpointsHost>;

/// <summary>
/// <see cref="InertiaCookieAuthenticationExtensions.UseInertiaRedirects"/>: an Inertia visit sends <c>X-Requested-With</c>, which the
/// cookie handler would answer with a 401 the client cannot follow.
/// </summary>
public abstract class CookieAuthenticationTests<THost>
    where THost : IConformanceHost
{
    private sealed class AppHandlerCalls
    {
        public int Login;
    }

    private static Task<ConformanceApp> StartAsync(bool errorPages = false) => ConformanceApp.StartAsync<THost>(new ConformanceSetup
    {
        UseErrorPages = errorPages,
        Services = services =>
        {
            var calls = new AppHandlerCalls();
            services.AddSingleton(calls);
            services.AddAuthorization();
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
            {
                o.LoginPath = "/login";
                o.AccessDeniedPath = "/denied";
                var inner = o.Events.OnRedirectToLogin;
                o.Events.OnRedirectToLogin = context =>
                {
                    Interlocked.Increment(ref calls.Login); // an app's own handler: still runs for other requests
                    return inner(context);
                };
                o.UseInertiaRedirects();
            });
            if (errorPages)
            {
                services.AddInertiaErrorPages();
            }
        },
    });

    [Theory]
    [InlineData("GET", 302)]
    [InlineData("POST", 303)]
    [InlineData("PUT", 303)]
    [InlineData("DELETE", 303)]
    public async Task An_unauthenticated_inertia_visit_is_redirected_to_the_login_page(string method, int status)
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().InertiaAsync(new HttpMethod(method), "/secret", configure: r => r.WithHeader("X-Requested-With", "XMLHttpRequest"));
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("/login?ReturnUrl=%2Fsecret", response.Headers.Location?.PathAndQuery);
        Assert.Equal(0, app.App.Services.GetRequiredService<AppHandlerCalls>().Login);
    }

    [Fact]
    public async Task Prefetch_and_partial_reloads_are_redirected_too()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var prefetch = await client.InertiaGetAsync("/secret", r => r.WithHeader("Purpose", "prefetch"));
        Assert.Equal(HttpStatusCode.Found, prefetch.StatusCode);
        var partial = await client.InertiaGetAsync("/secret", r =>
        {
            r.WithHeader(InertiaHeaders.PartialComponent, "Secret");
            r.WithHeader(InertiaHeaders.PartialData, "a");
        });
        Assert.Equal(HttpStatusCode.Found, partial.StatusCode);
        Assert.Equal("/login?ReturnUrl=%2Fsecret", partial.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task Other_requests_keep_the_framework_behavior_and_the_app_handler()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        // fetch() and Precognition validation: the 401 a script can read.
        using var fetch = new HttpRequestMessage(HttpMethod.Get, "/secret");
        fetch.WithHeader("X-Requested-With", "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(fetch, TestContext.Current.CancellationToken)).StatusCode);
        var precognition = await client.InertiaAsync(HttpMethod.Post, "/secret", configure: r =>
        {
            r.WithHeader(InertiaPrecognition.Header, "true");
            r.WithHeader("X-Requested-With", "XMLHttpRequest");
        });
        Assert.Equal(HttpStatusCode.Unauthorized, precognition.StatusCode);

        // A browser navigation is redirected by the framework itself.
        var browser = await client.GetAsync(new Uri("/secret", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, browser.StatusCode);
        Assert.Equal(3, app.App.Services.GetRequiredService<AppHandlerCalls>().Login);
    }

    [Fact]
    public async Task Access_denied_redirects_without_error_pages_and_renders_403_with_them()
    {
        await using (var app = await StartAsync())
        {
            var client = app.CreateClient();
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(new Uri("/sign-in", UriKind.Relative), null, TestContext.Current.CancellationToken)).StatusCode);
            var denied = await client.InertiaGetAsync("/secret/admin");
            Assert.Equal(HttpStatusCode.Found, denied.StatusCode);
            Assert.Equal("/denied?ReturnUrl=%2Fsecret%2Fadmin", denied.Headers.Location?.PathAndQuery);
        }

        await using (var app = await StartAsync(errorPages: true))
        {
            var client = app.CreateClient();
            await client.PostAsync(new Uri("/sign-in", UriKind.Relative), null, TestContext.Current.CancellationToken);
            var denied = await client.InertiaGetAsync("/secret/admin");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            var page = await denied.PageAsync();
            Assert.Equal("Error", (string?)page["component"]);
            Assert.Equal(403, (int?)page["props"]!["status"]);
        }
    }

}
