using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

public sealed class MinimalApiRateLimitingTests : RateLimitingTests<MinimalApiHost>;

public sealed class MvcRateLimitingTests : RateLimitingTests<MvcHost>;

[Collection("FastEndpoints")] // FastEndpoints keeps its configuration in statics
public sealed class FastEndpointsRateLimitingTests : RateLimitingTests<FastEndpointsHost>;

/// <summary><see cref="InertiaRateLimiting.OnRejected"/>: one permit per hour for POST, none limited for GET.</summary>
public abstract class RateLimitingTests<THost>
    where THost : IConformanceHost
{
    private static Task<ConformanceApp> StartAsync(bool errorPages = false) => ConformanceApp.StartAsync<THost>(new ConformanceSetup
    {
        UseErrorPages = errorPages,
        Middleware = app => app.UseRateLimiter(),
        Services = services =>
        {
            services.AddRateLimiter(o =>
            {
                o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => HttpMethods.IsGet(http.Request.Method)
                    ? RateLimitPartition.GetNoLimiter("get")
                    : RateLimitPartition.GetFixedWindowLimiter("post", _ => new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromHours(1) }));
                o.OnRejected = InertiaRateLimiting.OnRejected(message: "Slow down");
            });
            if (errorPages)
            {
                services.AddInertiaErrorPages(e => e.StatusCodes.Add(StatusCodes.Status429TooManyRequests));
            }
        },
    });

    private static async Task<HttpResponseMessage> SubmitTwiceAsync(HttpClient client, Action<HttpRequestMessage>? configure = null)
    {
        Assert.Equal(HttpStatusCode.Found, (await client.InertiaAsync(HttpMethod.Post, "/typed", new { id = 1 })).StatusCode); // the one permit
        return await client.InertiaAsync(HttpMethod.Post, "/typed", new { id = 1 }, configure);
    }

    [Theory]
    [InlineData("POST", HttpStatusCode.Found)]
    [InlineData("QUERY", HttpStatusCode.SeeOther)]
    public async Task A_rejected_inertia_submission_goes_back_with_a_flash(string method, HttpStatusCode expected)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var rejected = await SubmitTwiceAsync(client, r =>
        {
            r.Method = new HttpMethod(method);
            r.Headers.Referrer = new Uri("http://localhost/typed/7");
        });
        Assert.Equal(expected, rejected.StatusCode);
        Assert.Equal("/typed/7", rejected.Headers.Location?.PathAndQuery);
        Assert.Equal("3600", rejected.Header("Retry-After"));

        var page = await (await client.InertiaGetAsync("/typed/7")).PageAsync();
        Assert.Equal("Slow down", (string?)page["flash"]!["error"]);
    }

    [Fact]
    public async Task Without_a_referer_the_rejection_goes_back_to_the_root()
    {
        await using var app = await StartAsync();
        var rejected = await SubmitTwiceAsync(app.CreateClient());
        Assert.Equal(HttpStatusCode.Found, rejected.StatusCode);
        Assert.Equal("/", rejected.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Error_pages_that_render_429_take_over_the_submission()
    {
        await using var app = await StartAsync(errorPages: true);
        var rejected = await SubmitTwiceAsync(app.CreateClient(), r => r.Headers.Referrer = new Uri("http://localhost/typed/7"));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(429, (int?)(await rejected.PageAsync())["props"]!["status"]);
    }

    [Fact]
    public async Task Precognition_and_plain_requests_get_a_bare_429()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var precognition = await SubmitTwiceAsync(client, r => r.WithHeader(InertiaPrecognition.Header, "true"));
        Assert.Equal(HttpStatusCode.TooManyRequests, precognition.StatusCode);
        Assert.Equal("true", precognition.Header(InertiaPrecognition.Header));
        Assert.Contains(InertiaPrecognition.Header, precognition.Headers.Vary);

        using var plain = new HttpRequestMessage(HttpMethod.Post, "/typed") { Content = JsonContent.Create(new { id = 1 }) };
        var response = await client.SendAsync(plain, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Null(response.Header(InertiaPrecognition.Header));
    }
}
