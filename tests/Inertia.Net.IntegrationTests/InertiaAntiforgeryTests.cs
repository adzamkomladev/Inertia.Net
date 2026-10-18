using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

/// <summary>A conformance host with <c>UseInertiaAntiforgery()</c> in front of its routes.</summary>
public sealed class AntiforgeryProtected<THost> : IConformanceHost
    where THost : IConformanceHost
{
    public static void ConfigureServices(IServiceCollection services)
    {
        THost.ConfigureServices(services);
        services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
    }

    public static void MapRoutes(WebApplication app)
    {
        app.UseInertiaAntiforgery();
        THost.MapRoutes(app);
    }

    public static bool UsesSession => THost.UsesSession;
}

/// <summary><c>UseInertiaAntiforgery()</c> protects the mutations of every framework host.</summary>
public abstract class InertiaAntiforgeryConformanceTests<THost>
    where THost : IConformanceHost
{
    private static Task<ConformanceApp> StartAsync() => ConformanceApp.StartAsync<AntiforgeryProtected<THost>>();

    [Fact]
    public async Task A_mutation_without_a_token_is_rejected_before_the_handler()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" });
        Assert.Equal(419, (int)response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task A_mutation_with_the_cookie_token_runs(string method)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var token = await InertiaAntiforgeryTests.TokenAsync(client);
        var response = await client.InertiaAsync(new HttpMethod(method), "/form", new { name = "Ann" }, r => r.WithHeader(InertiaHeaders.XsrfToken, token));
        Assert.Contains((int)response.StatusCode, new[] { 302, 303 });
        Assert.EndsWith("/page", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validation_still_redirects_back_with_errors()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var token = await InertiaAntiforgeryTests.TokenAsync(client);
        var response = await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "" }, r =>
        {
            r.WithHeader(InertiaHeaders.XsrfToken, token);
            r.Headers.Referrer = new Uri("http://localhost/page");
        });
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        Assert.NotNull(page["props"]!["errors"]!["name"]);
    }
}

public sealed class MinimalApiAntiforgeryConformanceTests : InertiaAntiforgeryConformanceTests<MinimalApiHost>;

public sealed class MvcAntiforgeryConformanceTests : InertiaAntiforgeryConformanceTests<MvcHost>;

public sealed class FastEndpointsAntiforgeryConformanceTests : InertiaAntiforgeryConformanceTests<FastEndpointsHost>;

/// <summary><c>UseInertiaAntiforgery()</c> options and its interplay with form binding and cookie consent.</summary>
public sealed class InertiaAntiforgeryTests
{
    internal static async Task<string> TokenAsync(HttpClient client)
    {
        var response = await client.InertiaGetAsync("/page");
        var cookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(cookie["XSRF-TOKEN=".Length..cookie.IndexOf(';')]);
    }

    [Fact]
    public async Task Form_binding_endpoints_work_without_use_antiforgery()
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        var client = app.CreateClient();
        var token = await TokenAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/form-body") { Content = new FormUrlEncodedContent([new("name", "Ann")]) };
        request.WithHeader(InertiaHeaders.XsrfToken, token);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ann", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // A plain HTML form post without the token (classic cross-site CSRF) is rejected too.
        var forged = await app.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/form-body") { Content = new FormUrlEncodedContent([new("name", "Eve")]) }, TestContext.Current.CancellationToken);
        Assert.Equal(419, (int)forged.StatusCode);
    }

    [Fact]
    public async Task Endpoints_can_opt_out_and_safe_methods_are_never_validated()
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.InertiaAsync(HttpMethod.Post, "/webhook")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.InertiaGetAsync("/page")).StatusCode);
    }

    [Fact]
    public async Task Should_validate_and_on_rejected_are_configurable()
    {
        await using var app = await ConformanceApp.StartAsync<CustomHost>();
        var client = app.CreateClient();

        // Exempted by the predicate.
        Assert.Equal(HttpStatusCode.OK, (await client.InertiaAsync(HttpMethod.Post, "/api/ping")).StatusCode);

        // Rejected with a redirect back and a flash message the next page renders.
        var rejected = await client.InertiaAsync(HttpMethod.Put, "/save", configure: r => r.Headers.Referrer = new Uri("http://localhost/page"));
        Assert.Equal(HttpStatusCode.SeeOther, rejected.StatusCode);
        Assert.Equal("http://localhost/page", rejected.Headers.Location!.ToString());
        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        Assert.Equal("The page expired, please try again.", (string?)page["flash"]!["error"]);
    }

    [Fact]
    public async Task The_cookie_follows_the_secure_policy()
    {
        await using var app = await ConformanceApp.StartAsync<CustomHost>();
        var response = await app.CreateClient().InertiaGetAsync("/page");
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("CSRF=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase); // plain http, policy Always
    }

    [Fact]
    public async Task The_cookie_is_essential_under_a_consent_policy()
    {
        await using var app = await ConformanceApp.StartAsync<ConsentHost>();
        var response = await app.CreateClient().InertiaGetAsync("/page");
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
    }

    public sealed class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services) => services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);

        public static void MapRoutes(WebApplication app)
        {
            app.UseInertiaAntiforgery();
            app.MapGet("/page", () => Render("Page"));
            app.MapPost("/form-body", ([FromForm] string name) => Results.Text(name));
            app.MapPost("/webhook", () => Results.Text("ok")).DisableAntiforgery();
        }
    }

    public sealed class CustomHost : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services) => services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);

        public static void MapRoutes(WebApplication app)
        {
            app.UseInertiaAntiforgery(o =>
            {
                o.CookieName = "CSRF";
                o.SecurePolicy = CookieSecurePolicy.Always;
                o.ShouldValidate = context => !context.Request.Path.StartsWithSegments("/api");
                o.OnRejected = (_, _) => Back().WithFlash("error", "The page expired, please try again.");
            });
            app.MapGet("/page", () => Render("Page"));
            app.MapPost("/api/ping", () => Results.Text("pong"));
            app.MapPut("/save", () => Results.Text("saved"));
        }
    }

    public sealed class ConsentHost : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services)
        {
            services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
            services.Configure<CookiePolicyOptions>(o => o.CheckConsentNeeded = _ => true);
        }

        public static void MapRoutes(WebApplication app)
        {
            app.UseCookiePolicy();
            app.UseInertiaAntiforgery();
            app.MapGet("/page", () => Render("Page"));
        }
    }
}
