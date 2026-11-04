using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

public sealed class PrecognitionRateLimitingTests
{
    [Fact]
    public void The_request_predicate_and_header_names_are_public()
    {
        Assert.True(typeof(InertiaPrecognition).IsPublic);
        Assert.Equal("Precognition", InertiaPrecognition.Header);
        Assert.Equal("Precognition-Validate-Only", InertiaPrecognition.ValidateOnlyHeader);
        Assert.Equal("Precognition-Success", InertiaPrecognition.SuccessHeader);
        var context = new DefaultHttpContext();
        Assert.False(InertiaPrecognition.IsPrecognitive(context.Request));
        context.Request.Headers[InertiaPrecognition.Header] = "TRUE";
        Assert.True(InertiaPrecognition.IsPrecognitive(context.Request));
    }

    [Fact]
    public async Task Only_a_precognitive_endpoint_gets_the_validation_bucket_and_it_is_bounded()
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/precognitive", true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/precognitive", true)).StatusCode);
        using var rejected = await PostAsync(client, "/precognitive", true);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("true", rejected.Headers.GetValues(InertiaPrecognition.Header).Single());
        Assert.Equal(0, app.App.Services.GetRequiredService<Calls>().Count);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, "/ordinary", true)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync(client, "/ordinary", true)).StatusCode);
        Assert.Equal(1, app.App.Services.GetRequiredService<Calls>().Count);
    }

    [Fact]
    public async Task Real_submits_keep_the_strict_bucket_and_rejections_are_not_precognitive()
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, "/precognitive", false)).StatusCode);
        using var rejected = await PostAsync(client, "/precognitive", false);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.False(rejected.Headers.Contains(InertiaPrecognition.Header));
        Assert.Equal(1, app.App.Services.GetRequiredService<Calls>().Count);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/precognitive", true)).StatusCode);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, bool precognitive)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { name = "Ann", email = "a@b.c" }),
        };
        if (precognitive) request.Headers.Add(InertiaPrecognition.Header, "true");
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public sealed class Calls
    {
        public int Count;
    }

    public sealed class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services)
        {
            MinimalApiHost.ConfigureServices(services);
            services.AddSingleton<Calls>();
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("login", context =>
                {
                    var validation = InertiaPrecognition.IsPrecognitive(context.Request)
                        && context.GetEndpoint()?.Metadata.GetMetadata<InertiaPrecognitionMetadata>() is not null;
                    return RateLimitPartition.GetFixedWindowLimiter($"{context.Request.Path}:{validation}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = validation ? 2 : 1,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                        AutoReplenishment = false,
                    });
                });
                options.OnRejected = (context, _) =>
                {
                    if (InertiaPrecognition.IsPrecognitive(context.HttpContext.Request))
                        context.HttpContext.Response.Headers[InertiaPrecognition.Header] = "true";
                    return ValueTask.CompletedTask;
                };
            });
        }

        public static void MapRoutes(WebApplication app)
        {
            app.UseRouting();
            app.UseRateLimiter();
            app.MapPost("/precognitive", (PrecognitionTests.PrecogInput input, Calls calls) =>
            {
                Interlocked.Increment(ref calls.Count);
                return Results.Ok();
            }).WithInertiaPrecognition().RequireRateLimiting("login");
            app.MapPost("/ordinary", (PrecognitionTests.PrecogInput input, Calls calls) =>
            {
                Interlocked.Increment(ref calls.Count);
                return Results.Ok();
            }).WithInertiaValidation().RequireRateLimiting("login");
        }
    }
}
