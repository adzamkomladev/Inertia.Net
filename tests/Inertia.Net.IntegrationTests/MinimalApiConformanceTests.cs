using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

public sealed class MinimalApiConformanceTests : ProtocolConformanceTests<MinimalApiHost>;

/// <summary>Minimal API validation glue: .NET 10 <c>AddValidation()</c>, returned validation problems, filter ordering.</summary>
public sealed class MinimalApiValidationTests
{
    private static Task<ConformanceApp> StartAsync() => ConformanceApp.StartAsync<Host>();

    [Fact]
    public async Task Built_in_validation_still_answers_400_problem_details_without_x_inertia()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().PostAsJsonAsync("/form", new { name = "" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Without_the_glue_add_validation_answers_400_even_for_inertia_requests()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().InertiaAsync(HttpMethod.Post, "/plain-validation", new { name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Validation_runs_before_other_endpoint_filters_and_the_handler()
    {
        await using var app = await StartAsync();
        var calls = app.App.Services.GetRequiredService<FilterCalls>();
        var response = await app.CreateClient().InertiaAsync(HttpMethod.Post, "/ordered", new { name = "" });

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(["outer"], calls.Names); // the inner filter and the handler never ran

        await app.CreateClient().InertiaAsync(HttpMethod.Post, "/ordered", new { name = "Ann" });
        Assert.Equal(["outer", "outer", "inner", "handler"], calls.Names);
    }

    [Theory]
    [InlineData("/typed-problem")]
    [InlineData("/untyped-problem")]
    [InlineData("/problem-details")]
    [InlineData("/union-problem")]
    public async Task Returned_validation_problems_become_a_redirect_back_with_errors(string url)
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var response = await client.InertiaAsync(HttpMethod.Post, url, configure: r => r.WithHeader(InertiaHeaders.ErrorBag, "login"));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);

        var page = await (await client.InertiaGetAsync("/page", r => r.WithHeader(InertiaHeaders.ErrorBag, "login"))).PageAsync();
        JsonAssert.Equal("""{"login":{"email":"Taken"}}""", page["props"]!["errors"]);

        var plain = await client.PostAsync(url, null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
    }

    [Fact]
    public async Task Inertia_gets_keep_their_validation_problem()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().InertiaGetAsync("/typed-problem");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Manual_back_with_errors_and_flash()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = await client.InertiaAsync(HttpMethod.Delete, "/manual", configure: r => r.Headers.Referrer = new Uri("http://localhost/page"));
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("http://localhost/page", response.Headers.Location?.OriginalString);

        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        JsonAssert.Equal("""{"name":"Nope"}""", page["props"]!["errors"]);
        JsonAssert.Equal("""{"toast":{"text":"Check the form","level":2}}""", page["flash"]);
    }

    [Fact]
    public async Task Validation_glue_applies_to_route_groups()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().InertiaAsync(HttpMethod.Post, "/group/form", new { name = "" });
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact]
    public async Task Nested_error_keys_use_json_names()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        await client.InertiaAsync(HttpMethod.Post, "/nested", new { address = new { street = "" } });
        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        Assert.Equal(["address.street"], page["props"]!["errors"]!.AsObject().Select(p => p.Key));
    }

    public sealed class FilterCalls
    {
        public List<string> Names { get; } = [];
    }

    public sealed record Toast(string Text, int Level);

    /// <summary>The Minimal API host plus extra validation routes.</summary>
    public sealed class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services)
        {
            MinimalApiHost.ConfigureServices(services);
            services.AddSingleton<FilterCalls>();
        }

        public static void MapRoutes(WebApplication app)
        {
            MinimalApiHost.MapRoutes(app);
            var errors = new Dictionary<string, string[]> { ["email"] = ["Taken"] };

            app.MapPost("/plain-validation", ([FromBody] FormInput input) => Results.Ok());
            app.MapPost("/nested", ([FromBody] NestedInput input) => Results.Ok()).WithInertiaValidation();

            app.MapPost("/ordered", ([FromBody] FormInput input, FilterCalls calls) =>
                {
                    calls.Names.Add("handler");
                    return Results.Ok();
                })
                .AddEndpointFilter(async (context, next) =>
                {
                    context.HttpContext.RequestServices.GetRequiredService<FilterCalls>().Names.Add("outer");
                    return await next(context);
                })
                .WithInertiaValidation()
                .AddEndpointFilter(async (context, next) =>
                {
                    context.HttpContext.RequestServices.GetRequiredService<FilterCalls>().Names.Add("inner");
                    return await next(context);
                });

            app.MapMethods("/typed-problem", [HttpMethods.Get, HttpMethods.Post], () => TypedResults.ValidationProblem(errors)).WithInertiaValidation();
            app.MapPost("/untyped-problem", () => Results.ValidationProblem(errors)).WithInertiaValidation();
            app.MapPost("/problem-details", (HttpContext context) =>
            {
                context.Response.StatusCode = 400;
                return new HttpValidationProblemDetails(errors);
            }).WithInertiaValidation();
            app.MapPost("/union-problem", Results<Ok, ValidationProblem> () => TypedResults.ValidationProblem(errors)).WithInertiaValidation();

            app.MapDelete("/manual", () => Back("/fallback")
                .WithErrors(new Dictionary<string, string> { ["name"] = "Nope" })
                .WithFlash("toast", new Toast("Check the form", 2)));

            app.MapGroup("/group").WithInertiaValidation().MapPost("/form", ([FromBody] GroupInput input) => Results.Redirect("/page"));
        }
    }

    public sealed class NestedInput
    {
        [Required]
        public Address? Address { get; set; }
    }

    public sealed class Address
    {
        [Required]
        public string? Street { get; set; }
    }

    public sealed class GroupInput
    {
        [Required]
        [StringLength(10)]
        public string? Name { get; set; }
    }
}
