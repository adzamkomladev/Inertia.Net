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
        // A prefix matches whole path segments only, and a long list of fields still matches.
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/precog-nested", new { address = new { street = "" } }, only: "addr, address.str")).StatusCode);
        var many = string.Join(',', Enumerable.Range(0, 2000).Select(i => $"f{i}")) + ",address";
        Assert.Equal(["address.street"], (await ErrorsAsync(await PostAsync(client, "/precog-nested", new { address = new { street = "" } }, only: many))).Select(e => e.Key));
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

    // .NET 11 runs endpoint filters after a binding failure (status already 400, argument missing): a malformed body must not
    // pass Precognition as valid or redirect back without errors. .NET 10 never runs the filters here; both answer 400.
    [Fact]
    public async Task A_malformed_body_is_a_400_for_precognitive_and_inertia_requests()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        foreach (var (precognitive, inertia) in new[] { (true, false), (false, true) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/precog") { Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json") };
            if (precognitive) request.WithHeader("Precognition", "true");
            if (inertia) request.WithHeader(InertiaHeaders.Inertia, "true");
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Header("Precognition-Success"));
        }

        Assert.Empty(app.App.Services.GetRequiredService<Calls>().Names);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public async Task Async_validation_attributes_report_through_precognition_and_inertia_validation()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var precognitive = await PostAsync(client, "/precog-async", new { name = "taken" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, precognitive.StatusCode);
        Assert.Equal(["name"], (await ErrorsAsync(precognitive)).Select(e => e.Key));
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/precog-async", new { name = "free" })).StatusCode);

        var inertia = await PostAsync(client, "/validate-async", new { name = "taken" }, precognitive: false, inertia: true);
        Assert.Equal(HttpStatusCode.Found, inertia.StatusCode);
        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        Assert.Equal("The name is taken.", (string?)page["props"]!["errors"]!["name"]);
    }
#endif

    public sealed class Calls
    {
        public List<string> Names { get; } = [];
    }

    [Fact]
    public async Task Collection_validate_only_keeps_the_invalid_indexed_field()
    {
        await using var app = await StartAsync();
        var response = await PostAsync(app.CreateClient(), "/precog-collection", new { items = new[] { new { email = "" } } }, "items.0.email");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["items.0.email"], (await ErrorsAsync(response)).Select(e => e.Key));
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
            app.MapPost("/precog-collection", ([FromBody] CollectionInput input) => Results.Ok()).WithInertiaPrecognition();
#if NET11_0_OR_GREATER
            app.MapPost("/precog-async", ([FromBody] AsyncInput input) => Results.Ok()).WithInertiaPrecognition();
            app.MapPost("/validate-async", ([FromBody] AsyncInput input) => Results.Ok()).WithInertiaValidation();
#endif
        }
    }

    public sealed class PrecogInput
    {
        [Required]
        public string? Name { get; set; }

        [Required]
        public string? Email { get; set; }
    }

#if NET11_0_OR_GREATER
    public sealed class AsyncInput
    {
        [NotTakenAsync]
        public string? Name { get; set; }
    }

    // Async only: the synchronous path accepts everything, so an error proves the async validator ran.
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class NotTakenAsyncAttribute : AsyncValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) => ValidationResult.Success;

        protected override async Task<ValidationResult?> IsValidAsync(object? value, ValidationContext validationContext, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return value as string == "taken" ? new ValidationResult("The name is taken.") : ValidationResult.Success;
        }
    }
#endif

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

    public sealed class CollectionInput
    {
        public List<CollectionItem> Items { get; set; } = [];
    }
    public sealed class CollectionItem
    {
        [Required] public string? Email { get; set; }
    }
}
