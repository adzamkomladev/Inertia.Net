using Inertia.Net.Testing;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Inertia.Net.IntegrationTests;

/// <summary>
/// <c>UseInertiaAntiforgery()</c> on a full <c>WebApplication.CreateBuilder</c> host, which on .NET 11 also gets the framework's
/// automatic CSRF middleware (a <c>Sec-Fetch-Site</c>/<c>Origin</c> verdict on unsafe requests). The token check stays authoritative.
/// </summary>
public sealed class AutomaticCsrfTests
{
    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(bool disableCsrfProtection = false)
    {
        var root = Path.Join(Path.GetTempPath(), "inertia-csrf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Join(root, "app.html"), "<html><body>@inertia</body></html>");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        if (disableCsrfProtection)
        {
            builder.Configuration["DisableCsrfProtection"] = "true";
        }

        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddInertia();
        builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
        var app = builder.Build();
        app.UseInertia();
        app.UseInertiaAntiforgery();
        app.MapGet("/page", () => Render("Page"));
        app.MapPost("/form", ([FromForm] string name) => Results.Ok(name));
        app.MapPost("/json", () => Results.Ok());
        await app.StartAsync(TestContext.Current.CancellationToken);
        var server = app.GetTestServer();
        return (app, new HttpClient(new CookieContainerHandler { InnerHandler = server.CreateHandler() }) { BaseAddress = server.BaseAddress });
    }

    private static HttpRequestMessage CrossSite(string url, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        request.Headers.Add("Origin", "https://evil.example");
        return request.WithXsrfToken(token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_valid_token_wins_over_a_cross_site_verdict_on_form_and_json_endpoints(bool disableCsrfProtection)
    {
        var (app, client) = await StartAsync(disableCsrfProtection);
        await using var _ = app;
        using var __ = client; // also disposes its CookieContainerHandler
        using var page = new HttpRequestMessage(HttpMethod.Get, "/page") { Headers = { { InertiaHeaders.Inertia, "true" } } };
        var token = (await client.SendAsync(page, TestContext.Current.CancellationToken)).GetXsrfToken();

        using var formRequest = CrossSite("/form", token, new FormUrlEncodedContent([new("name", "Ann")]));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(formRequest, TestContext.Current.CancellationToken)).StatusCode);
        using var jsonRequest = CrossSite("/json", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(jsonRequest, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task A_missing_token_is_still_a_419_not_a_400()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        using var __ = client; // also disposes its CookieContainerHandler
        using var request = new HttpRequestMessage(HttpMethod.Post, "/form") { Content = new FormUrlEncodedContent([new("name", "Ann")]) };
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(419, (int)(await client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }
}
