using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

/// <summary><c>UseInertiaAntiforgeryCookie()</c>: the XSRF-TOKEN cookie and the X-XSRF-TOKEN header the client sends back.</summary>
public sealed class AntiforgeryTests
{
    private static Task<ConformanceApp> StartAsync() => ConformanceApp.StartAsync<Host>();

    private static string? Xsrf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.FirstOrDefault(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)) : null;

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var cookie = Xsrf(await client.InertiaGetAsync("/page"))!;
        return Uri.UnescapeDataString(cookie["XSRF-TOKEN=".Length..cookie.IndexOf(';')]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Page_loads_set_a_script_readable_cookie(bool inertia)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = inertia ? await client.InertiaGetAsync("/page") : await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/page") { Headers = { { "Accept", "text/html" } } }, TestContext.Current.CancellationToken);

        var cookie = Xsrf(response);
        Assert.NotNull(cookie);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase); // plain http
    }

    [Fact]
    public async Task Api_requests_and_posts_do_not_get_the_cookie()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        Assert.Null(Xsrf(await client.GetAsync("/page", TestContext.Current.CancellationToken))); // no HTML accepted
        Assert.Null(Xsrf(await client.InertiaAsync(HttpMethod.Post, "/json")));
    }

    [Fact]
    public async Task The_cookie_name_is_configurable_and_https_is_secure()
    {
        await using var app = await ConformanceApp.StartAsync<CustomHost>();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/page");
        request.Headers.Add(InertiaHeaders.Inertia, "true");
        var response = await app.Server.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("CSRF=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_post_with_the_header_passes_and_without_or_with_a_wrong_one_fails()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var token = await TokenAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await client.InertiaAsync(HttpMethod.Post, "/json", configure: r => r.WithHeader(InertiaHeaders.XsrfToken, token))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.InertiaAsync(HttpMethod.Post, "/json")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.InertiaAsync(HttpMethod.Post, "/json", configure: r => r.WithHeader(InertiaHeaders.XsrfToken, "nope"))).StatusCode);
    }

    [Fact]
    public async Task Form_endpoints_are_validated_by_use_antiforgery()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var token = await TokenAsync(client);
        static FormUrlEncodedContent Form() => new([new("name", "Ann")]);

        var ok = new HttpRequestMessage(HttpMethod.Post, "/form") { Content = Form() };
        ok.WithHeader(InertiaHeaders.XsrfToken, token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ok, TestContext.Current.CancellationToken)).StatusCode);

        var missing = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/form") { Content = Form() }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    public class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services) => services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");

        public static void MapRoutes(WebApplication app)
        {
            app.UseInertiaAntiforgeryCookie();
            app.UseAntiforgery();
            app.MapGet("/page", () => Render("Page"));
            app.MapPost("/json", async (HttpContext context, IAntiforgery antiforgery) =>
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                    return Results.Text("valid"); // not an empty 200, which Inertia would redirect back
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest();
                }
            });
            app.MapPost("/form", ([FromForm] string name) => Results.Ok(name));
        }
    }

    public sealed class CustomHost : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services) => services.AddAntiforgery();

        public static void MapRoutes(WebApplication app)
        {
            app.UseInertiaAntiforgeryCookie("CSRF");
            app.MapGet("/page", () => Render("Page"));
        }
    }
}
