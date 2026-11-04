using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Inertia.Net.Testing;

namespace Inertia.Net.IntegrationTests;

public abstract class ErrorPagesTests<THost> where THost : IConformanceHost
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_page_renders_status_and_shared_props(bool inertia)
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var response = await SendAsync(client, "/missing", inertia);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(inertia ? "true" : null, response.Header(InertiaHeaders.Inertia));
        var page = await response.PageAsync();
        Assert.Equal("Error", (string?)page["component"]);
        Assert.Equal(404, (int?)page["props"]!["status"]);
        Assert.Equal("shared", (string?)page["props"]!["app"]);
        Assert.Equal("request", (string?)page["props"]!["requestShared"]);
        Assert.Empty(page["props"]!["errors"]!.AsObject());
        Assert.Contains("X-Inertia", response.Header("Vary"));
        await response.AssertInertiaAsync(p => p.Status(404).Component("Error"), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Exceptions_render_override_and_receive_exception(bool inertia)
    {
        Exception? received = null;
        await using var app = await StartAsync(configure: o =>
        {
            o.Components[500] = "Errors/Server";
            o.Props = (_, _, exception) =>
            {
                received = exception;
                return new InertiaProps { ["requestId"] = "id" };
            };
        });
        using var client = app.GetTestClient();
        using var response = await SendAsync(client, "/throw", inertia);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var page = await response.PageAsync();
        Assert.Equal("Errors/Server", (string?)page["component"]);
        Assert.Equal(500, (int?)page["props"]!["status"]);
        Assert.Equal("id", (string?)page["props"]!["requestId"]);
        Assert.IsType<InvalidOperationException>(received);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Required_ssr_failure_renders_configured_error_page_with_a_safe_message(bool connection)
    {
        const string endpoint = "http://internal-renderer.test:1234";
        InertiaSsrException? received = null;
        await using var app = await StartAsync(configure: options =>
        {
            options.Components[500] = "Errors/Server";
            options.Props = (_, _, exception) =>
            {
                received = Assert.IsType<InertiaSsrException>(exception);
                return new InertiaProps { ["message"] = exception.Message };
            };
        }, services: services =>
        {
            services.Configure<InertiaOptions>(options =>
            {
                options.Ssr.Mode = SsrMode.Preferred;
                options.Ssr.Urls.Add(endpoint);
            });
            services.AddHttpClient(InertiaSsr.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FailedRenderer(connection));
        });
        using var client = app.GetTestClient();
        using var response = await SendAsync(client, "/error-pages/ssr-required", false);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var page = await response.GetInertiaPageAsync(TestContext.Current.CancellationToken);
        var assertions = new AssertableInertia(page);
        assertions.Status(500).Component("Errors/Server");
        Assert.NotNull(received);
        Assert.True(received.Message.Length <= 2049);
        Assert.DoesNotContain(endpoint, received.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token=secret", received.Message, StringComparison.Ordinal);
        Assert.Equal(received.Message, (string?)page.Props["message"]);
    }

    private sealed class FailedRenderer(bool connection) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (connection) throw new HttpRequestException("connection refused");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"" + "http://internal-renderer.test:1234 token=secret " + new string('x', 4096) + "\",\"type\":\"render\"}"),
            });
        }
    }

    [Fact]
    public async Task Api_clients_are_untouched()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/missing");
        request.Headers.Add("Accept", "application/json");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/forbidden", 403, true)]
    [InlineData("/unauthorized", 401, false)]
    [InlineData("/conflict", 409, false)]
    [InlineData("/expired", 419, false)]
    [InlineData("/plain-redirect", 302, false)]
    [InlineData("/body", 404, false)]
    [InlineData("/own-error", 404, true)]
    public async Task Only_selected_empty_errors_render(string path, int status, bool isPage)
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var response = await SendAsync(client, path, true);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(isPage ? "true" : null, response.Header(InertiaHeaders.Inertia));
        if (path == "/body") Assert.Equal("already written", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        if (path == "/own-error") Assert.Equal("CustomError", (string?)(await response.PageAsync())["component"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Development_is_opt_in(bool enabled)
    {
        await using var app = await StartAsync(environment: Environments.Development, configure: o => o.EnabledInDevelopment = enabled);
        using var client = app.GetTestClient();
        using var response = await SendAsync(client, "/missing", true);
        Assert.Equal(enabled ? "true" : null, response.Header(InertiaHeaders.Inertia));
    }

    [Fact]
    public async Task Error_page_consumes_loaded_flash()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var redirect = await SendAsync(client, "/flash-error", true);
        var cookie = redirect.Header("Set-Cookie")!.Split(';')[0];
        using var request = new HttpRequestMessage(HttpMethod.Get, "/missing");
        request.Headers.Add(InertiaHeaders.Inertia, "true");
        request.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("hello", (string?)(await response.PageAsync())["flash"]!["message"]);
        Assert.Contains(".Inertia.State=;", response.Header("Set-Cookie"));
    }

    [Fact]
    public async Task Existing_exception_handler_can_answer_api_routes()
    {
        await using var app = await StartAsync(existingHandler: true);
        using var client = app.GetTestClient();
        using var api = await SendAsync(client, "/api/throw", true);
        Assert.Equal(502, (int)api.StatusCode);
        Assert.Equal("api error", await api.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var page = await SendAsync(client, "/throw", true);
        Assert.Equal("true", page.Header(InertiaHeaders.Inertia));
    }

    [Fact]
    public async Task Exception_status_selector_is_respected()
    {
        await using var app = await StartAsync(existingHandler: true, statusSelector: true);
        using var client = app.GetTestClient();
        using var response = await SendAsync(client, "/throw", true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(503, (int?)(await response.PageAsync())["props"]!["status"]);
    }

    [Theory]
    [InlineData("application/vnd.text/html+json")]
    [InlineData("text/html;q=0")]
    [InlineData("\"invalid")]
    public async Task Non_navigation_accept_does_not_render(string accept)
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/missing");
        request.Headers.TryAddWithoutValidation("Accept", accept);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Api_exception_uses_problem_details()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/throw");
        request.Headers.Add("Accept", "application/json");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Null(response.Header(InertiaHeaders.Inertia));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Development_exception_page_is_opt_in(bool enabled)
    {
        await using var app = await StartAsync(environment: Environments.Development, configure: o => o.EnabledInDevelopment = enabled);
        using var client = app.GetTestClient();
        using var response = await SendAsync(client, "/throw", true);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(enabled ? "true" : null, response.Header(InertiaHeaders.Inertia));
        if (!enabled)
        {
            Assert.Contains("InvalidOperationException", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("/conflict", 409)]
    [InlineData("/expired", 419)]
    public async Task Protocol_errors_remain_untouched_when_selected(string path, int status)
    {
        await using var app = await StartAsync(configure: o => o.StatusCodes.Add(status));
        using var client = app.GetTestClient();
        using var response = await SendAsync(client, path, true);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Null(response.Header(InertiaHeaders.Inertia));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, bool inertia)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (inertia) request.Headers.Add(InertiaHeaders.Inertia, "true");
        request.Headers.Add("Accept", "text/html");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<WebApplication> StartAsync(string environment = "Production",
        Action<InertiaErrorPagesOptions>? configure = null, bool existingHandler = false, bool statusSelector = false,
        Action<IServiceCollection>? services = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "inertia-error-pages-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "app.html"), "<html><head>@inertiaHead</head><body>@inertia</body></html>", TestContext.Current.CancellationToken);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, ContentRootPath = root });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ConformanceCounters>();
        builder.Services.AddInertia(o => o.Share("app", "shared"));
        THost.ConfigureServices(builder.Services);
        services?.Invoke(builder.Services);
        if (existingHandler) builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddInertiaErrorPages(configure);
        var app = builder.Build();
        if (existingHandler)
        {
            app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                StatusCodeSelector = statusSelector ? _ => 503 : null,
            });
        }
        app.UseInertiaErrorPages(useExceptionHandler: !existingHandler);
        app.UseInertia();
        app.Use(async (context, next) =>
        {
            context.Inertia().Share("requestShared", "request");
            await next(context);
        });
        THost.MapRoutes(app);
        app.MapGet("/error-pages/ssr-required", () => Render("Page").WithSsr(SsrMode.Required));
        app.MapGet("/plain-redirect", () => Results.Redirect("/page"));
        app.MapGet("/forbidden", () => Results.StatusCode(403));
        app.MapGet("/unauthorized", () => Results.StatusCode(401));
        app.MapGet("/conflict", () => Location("https://example.com"));
        app.MapGet("/expired", () => Results.StatusCode(419));
        app.MapGet("/body", () => Results.Text("already written", statusCode: 404));
        app.MapGet("/own-error", () => Render("CustomError").WithStatusCode(404));
        app.MapGet("/flash-error", () => Back("/missing").WithFlash("message", "hello"));
        app.MapGet("/api/throw", (Func<IResult>)(() => throw new InvalidOperationException("api failed")));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}

public sealed class MinimalApiErrorPagesTests : ErrorPagesTests<MinimalApiHost>;
public sealed class MvcErrorPagesTests : ErrorPagesTests<MvcHost>;
[Collection("FastEndpoints")]
public sealed class FastEndpointsErrorPagesTests : ErrorPagesTests<FastEndpointsHost>;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (!context.Request.Path.StartsWithSegments("/api")) return false;
        context.Response.StatusCode = 502;
        await context.Response.WriteAsync("api error", cancellationToken);
        return true;
    }
}
