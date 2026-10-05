using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

/// <summary>
/// Minimal API Precognition (<c>WithInertiaPrecognition()</c>), against the protocol of the laravel-precognition client: request
/// <c>Precognition: true</c> + <c>Precognition-Validate-Only</c>; response <c>Precognition: true</c>, <c>Vary: Precognition</c>,
/// 204 + <c>Precognition-Success: true</c> or 422 <c>{ message, errors }</c>; the handler never runs.
/// </summary>
public sealed class PrecognitionTests
{
    private static Task<ConformanceApp> StartAsync() => ConformanceApp.StartAsync<Host>();

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body, string? only = null, bool precognitive = true, bool inertia = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (precognitive)
        {
            request.WithHeader("Precognition", "true");
        }

        if (inertia)
        {
            request.WithHeader(InertiaHeaders.Inertia, "true");
        }

        if (only is not null)
        {
            request.WithHeader("Precognition-Validate-Only", only);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonObject> ErrorsAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["errors"]!.AsObject();

    [Fact]
    public async Task Failure_is_a_422_with_message_and_errors_and_the_handler_does_not_run()
    {
        await using var app = await StartAsync();
        var response = await PostAsync(app.CreateClient(), "/precog", new { name = "", email = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("true", response.Header("Precognition"));
        Assert.Contains("Precognition", response.Header("Vary"), StringComparison.Ordinal);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Contains("(and 1 more error)", (string?)body["message"], StringComparison.Ordinal);
        var errors = body["errors"]!.AsObject();
        Assert.Equal(["email", "name"], errors.Select(e => e.Key).Order());
        Assert.All(errors, e => Assert.IsType<JsonArray>(e.Value));
        Assert.Empty(app.App.Services.GetRequiredService<Calls>().Names);
    }

    [Fact]
    public async Task Success_is_a_204_with_precognition_success()
    {
        await using var app = await StartAsync();
        var response = await PostAsync(app.CreateClient(), "/precog", new { name = "Ann", email = "a@b.c" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("true", response.Header("Precognition"));
        Assert.Equal("true", response.Header("Precognition-Success"));
        Assert.Contains("Precognition", response.Header("Vary"), StringComparison.Ordinal);
        Assert.Empty(app.App.Services.GetRequiredService<Calls>().Names); // neither the handler nor the inner filter ran
    }

    [Fact]
    public async Task Validate_only_limits_the_errors_to_the_named_fields()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var email = await PostAsync(client, "/precog", new { name = "", email = "" }, only: "email");
        Assert.Equal(["email"], (await ErrorsAsync(email)).Select(e => e.Key));

        var both = await PostAsync(client, "/precog", new { name = "", email = "" }, only: "name, email");
        Assert.Equal(["email", "name"], (await ErrorsAsync(both)).Select(e => e.Key).Order());

        // Only valid fields were asked for: success although others are invalid.
        var ok = await PostAsync(client, "/precog", new { name = "", email = "a@b.c" }, only: "email");
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Nested_errors_use_json_names_and_match_validate_only_by_prefix()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        Assert.Equal(["address.street"], (await ErrorsAsync(await PostAsync(client, "/precog-nested", new { address = new { street = "" } }))).Select(e => e.Key));
        Assert.Equal(["address.street"], (await ErrorsAsync(await PostAsync(client, "/precog-nested", new { address = new { street = "" } }, only: "address"))).Select(e => e.Key));
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/precog-nested", new { address = new { street = "" } }, only: "other")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_without_validatable_parameters_succeed()
    {
        await using var app = await StartAsync();
        var response = await PostAsync(app.CreateClient(), "/precog-none", new { });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("true", response.Header("Precognition-Success"));
    }

    [Fact]
    public async Task Other_requests_behave_like_with_inertia_validation()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var calls = app.App.Services.GetRequiredService<Calls>();

        var plain = await PostAsync(client, "/precog", new { name = "", email = "" }, precognitive: false);
        Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
        Assert.Null(plain.Header("Precognition"));
        Assert.Contains("Precognition", plain.Header("Vary"), StringComparison.Ordinal);

        var inertia = await PostAsync(client, "/precog", new { name = "", email = "" }, precognitive: false, inertia: true);
        Assert.Equal(HttpStatusCode.Found, inertia.StatusCode);
        Assert.Empty(calls.Names);

        var valid = await PostAsync(client, "/precog", new { name = "Ann", email = "a@b.c" }, precognitive: false);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(["inner", "handler"], calls.Names);
    }

    public sealed class Calls
    {
        public List<string> Names { get; } = [];
    }

    public sealed class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services)
        {
            MinimalApiHost.ConfigureServices(services); // AddValidation(): the generator allows one call site per project
            services.AddSingleton<Calls>();
        }

        public static void MapRoutes(WebApplication app)
        {
            app.MapGet("/page", () => Render("Page"));
            app.MapPost("/precog", ([FromBody] PrecogInput input, Calls calls) =>
                {
                    calls.Names.Add("handler");
                    return Results.Ok();
                })
                .WithInertiaPrecognition()
                .AddEndpointFilter(async (context, next) =>
                {
                    context.HttpContext.RequestServices.GetRequiredService<Calls>().Names.Add("inner");
                    return await next(context);
                });
            app.MapPost("/precog-nested", ([FromBody] NestedInput input) => Results.Ok()).WithInertiaPrecognition();
            app.MapPost("/precog-none", () => Results.Ok()).WithInertiaPrecognition();
        }
    }

    public sealed class PrecogInput
    {
        [Required]
        public string? Name { get; set; }

        [Required]
        public string? Email { get; set; }
    }

    public sealed class NestedInput
    {
        [Required]
        public NestedAddress? Address { get; set; }
    }

    public sealed class NestedAddress
    {
        [Required]
        public string? Street { get; set; }
    }
}
