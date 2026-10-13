using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Inertia.Net.IntegrationTests;

/// <summary><c>UseInertia()</c> combined with other middleware and with a path base.</summary>
public sealed class MiddlewareCompositionTests
{
    private static async Task<WebApplication> StartAsync(Action<WebApplication> configure, Action<IServiceCollection>? services = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "inertia-composition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "app.html"), "<html><body>@inertia</body></html>");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddInertia();
        services?.Invoke(builder.Services);
        var app = builder.Build();
        configure(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    // Compression buffers the body and drops Content-Length, so "nothing was written" must not be inferred from those alone.
    [Fact]
    public async Task A_compressed_page_is_not_mistaken_for_an_empty_response()
    {
        await using var app = await StartAsync(
            a =>
            {
                a.UseResponseCompression();
                a.UseInertia();
                a.MapGet("/page", () => Render("Page", new InertiaProps { ["message"] = "hello" }));
            },
            s => s.AddResponseCompression());

        var request = new HttpRequestMessage(HttpMethod.Get, "/page");
        request.Headers.Add(InertiaHeaders.Inertia, "true");
        request.Headers.Add("Accept-Encoding", "gzip");
        var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", response.Header(InertiaHeaders.Inertia));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Buffered_responses_redirect_only_when_no_bytes_were_written(bool pipe, bool empty)
    {
        await using var app = await StartAsync(a =>
        {
            a.Use(async (context, next) =>
            {
                var original = context.Response.Body;
                await using var buffer = new MemoryStream();
                context.Response.Body = buffer;
                var feature = context.Features.Get<IHttpResponseBodyFeature>();
                await next(context);
                Assert.Same(feature, context.Features.Get<IHttpResponseBodyFeature>());
                context.Response.Body = original;
                buffer.Position = 0;
                await buffer.CopyToAsync(original, context.RequestAborted);
            });
            a.UseInertia();
            a.MapGet("/buffered", async (HttpContext context) =>
            {
                var bytes = empty ? ReadOnlyMemory<byte>.Empty : "nonempty"u8.ToArray();
                if (pipe) await context.Response.BodyWriter.WriteAsync(bytes, context.RequestAborted);
                else await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
            });
        });
        var response = await app.GetTestClient().InertiaGetAsync("/buffered");
        Assert.Equal(empty ? HttpStatusCode.Found : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(empty ? "" : "nonempty", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Redirect_fallbacks_stay_inside_the_path_base()
    {
        await using var app = await StartAsync(a =>
        {
            a.UsePathBase("/app");
            a.UseInertia();
            a.MapGet("/empty", () => Results.Ok());
            a.MapGet("/back", () => Back());
        });

        var client = app.GetTestClient();
        var empty = await client.InertiaGetAsync("/app/empty");
        Assert.Equal(HttpStatusCode.Found, empty.StatusCode);
        Assert.Equal("/app/", empty.Headers.Location?.OriginalString);

        var back = await client.GetAsync("/app/back", TestContext.Current.CancellationToken);
        Assert.Equal("/app/", back.Headers.Location?.OriginalString);
    }
}
