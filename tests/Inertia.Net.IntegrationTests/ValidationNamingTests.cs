using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Inertia.Net.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

[Collection("FastEndpoints")]
public sealed class ValidationNamingTests
{
    [Theory]
    [InlineData("minimal")]
    [InlineData("mvc")]
    [InlineData("fast")]
    public async Task All_adapters_use_the_page_serializer_and_member_aliases(string adapter)
    {
        await using var app = adapter switch
        {
            "mvc" => await ConformanceApp.StartAsync<MvcHost>(Setup()),
            "fast" => await ConformanceApp.StartAsync<FastEndpointsHost>(Setup()),
            _ => await ConformanceApp.StartAsync<Host>(Setup()),
        };
        var client = app.CreateClient();
        await client.InertiaAsync(HttpMethod.Post, "/naming/" + adapter, new { mail = "", displayName = "", items = new[] { new { mail = "" } } });
        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        var keys = page["props"]!["errors"]!.AsObject().Select(entry => entry.Key).Order().ToArray();
        Assert.Equal(["displayName", "items.0.mail", "mail"], keys);
    }

    [Fact]
    public async Task Mvc_uses_http_page_naming_even_when_mvc_binding_uses_another_policy()
    {
        await using var app = await ConformanceApp.StartAsync<MvcHost>(Setup(snakeCase: true));
        var client = app.CreateClient();
        await client.InertiaAsync(HttpMethod.Post, "/naming/mvc", new { mail = "", displayName = "", items = new[] { new { mail = "" } } });
        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        Assert.Equal(["display_name", "items.0.mail", "mail"], page["props"]!["errors"]!.AsObject().Select(entry => entry.Key).Order().ToArray());
    }

    [Fact]
    public async Task Handler_returned_errors_and_precognition_use_the_body_schema()
    {
        await using var app = await ConformanceApp.StartAsync<Host>(Setup());
        var client = app.CreateClient();
        await client.InertiaAsync(HttpMethod.Post, "/naming/returned", new { mail = "valid", displayName = "valid", items = new[] { new { mail = "valid" } } });
        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        Assert.Equal(["items.0.mail", "mail"], page["props"]!["errors"]!.AsObject().Select(entry => entry.Key).Order().ToArray());
        var response = await client.InertiaAsync(HttpMethod.Post, "/naming/precognition", new { mail = "", displayName = "", items = new[] { new { mail = "" } } },
            configure: request => { request.WithHeader("Precognition", "true"); request.WithHeader("Precognition-Validate-Only", "items.0.mail"); });
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var errors = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["errors"]!.AsObject();
        Assert.Equal(["items.0.mail"], errors.Select(entry => entry.Key));
    }

    private static ConformanceSetup Setup(bool snakeCase = false) => new()
    {
        Services = services => services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = snakeCase ? JsonNamingPolicy.SnakeCaseLower : JsonNamingPolicy.CamelCase;
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, NamingJsonContext.Default);
        }),
    };

    public sealed class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services) => MinimalApiHost.ConfigureServices(services);
        public static void MapRoutes(WebApplication app)
        {
            app.MapGet("/page", () => Render("Page"));
            app.MapPost("/naming/minimal", ([FromBody] NamingInput input) => Results.Ok()).WithInertiaValidation();
            app.MapPost("/naming/precognition", ([FromBody] NamingInput input) => Results.Ok()).WithInertiaPrecognition();
            app.MapPost("/naming/returned", ([FromBody] NamingInput input) => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Email"] = ["Invalid"], ["Items[0].Email"] = ["Invalid"],
            })).WithInertiaValidation();
        }
    }
}

public sealed class NamingInput
{
    [Required, JsonPropertyName("mail")] public string? Email { get; set; }
    [Required] public string? DisplayName { get; set; }
    public List<NamingItem> Items { get; set; } = [];
}
public sealed class NamingItem
{
    [Required, JsonPropertyName("mail")] public string? Email { get; set; }
}
[JsonSerializable(typeof(NamingInput))]
internal sealed partial class NamingJsonContext : JsonSerializerContext;

public sealed class NamingController : Controller
{
    [HttpPost("/naming/mvc")]
    public IResult Submit([FromBody] NamingInput input) => Results.Ok();
}

public sealed class NamingEndpoint : global::FastEndpoints.Endpoint<NamingInput>
{
    public override void Configure() => Post("/naming/fast");
    public override Task HandleAsync(NamingInput input, CancellationToken ct) => Send.OkAsync(ct);
}
public sealed class NamingValidator : global::FastEndpoints.Validator<NamingInput>
{
    public NamingValidator()
    {
        RuleFor(input => input.Email).NotEmpty();
        RuleFor(input => input.DisplayName).NotEmpty();
        RuleForEach(input => input.Items).ChildRules(item => item.RuleFor(value => value.Email).NotEmpty());
    }
}
