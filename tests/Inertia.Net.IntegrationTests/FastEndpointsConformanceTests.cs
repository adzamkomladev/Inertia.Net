using System.Net;
using System.Net.Http.Json;
using FastEndpoints;
using FluentValidation;
using Inertia.Net.FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

// FastEndpoints keeps its configuration and service resolver in statics, so FastEndpoints apps must not start concurrently.
[Collection("FastEndpoints")]
public sealed class FastEndpointsConformanceTests : ProtocolConformanceTests<FastEndpointsHost>;

/// <summary>FastEndpoints specifics: Send extensions, IResult from ExecuteAsync, validation redirect, Precognition.</summary>
[Collection("FastEndpoints")]
public sealed class FastEndpointsTests
{
    private static Task<ConformanceApp> StartAsync() => ConformanceApp.StartAsync<FastEndpointsHost>();

    [Fact]
    public async Task Send_extensions_render_redirect_and_go_back()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var page = await client.InertiaGetAsync("/fe/send-page");
        Assert.Equal("true", page.Header(InertiaHeaders.Inertia));
        JsonAssert.Equal("""{"errors":{},"x":1}""", (await page.PageAsync())["props"]);

        var location = await client.InertiaGetAsync("/fe/send-location");
        Assert.Equal(HttpStatusCode.Conflict, location.StatusCode);
        Assert.Equal("https://external.example/", location.Header(InertiaHeaders.Location));

        var back = await client.InertiaAsync(HttpMethod.Delete, "/fe/send-back", configure: r => r.Headers.Referrer = new Uri("http://localhost/page"));
        Assert.Equal(HttpStatusCode.SeeOther, back.StatusCode);
        Assert.Equal("http://localhost/page", back.Headers.Location?.OriginalString);
        Assert.Equal("/fallback", (await client.InertiaAsync(HttpMethod.Delete, "/fe/send-back")).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Execute_async_can_return_an_inertia_result()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var page = await client.InertiaGetAsync("/fe/execute-page");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("Executed", (string?)(await page.PageAsync())["component"]);

        var back = await client.InertiaAsync(HttpMethod.Post, "/fe/execute-back", configure: r => r.Headers.Referrer = new Uri("http://localhost/page"));
        Assert.Equal(HttpStatusCode.Found, back.StatusCode);
        Assert.Equal("http://localhost/page", back.Headers.Location?.OriginalString);
        Assert.Equal("Nope", (string?)(await (await client.InertiaGetAsync("/page")).PageAsync())["props"]!["errors"]!["name"]);
    }

    [Fact]
    public async Task Without_x_inertia_validation_failures_keep_the_normal_400()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().PostAsJsonAsync("/form", new { name = "" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonAssert.Equal("""{"statusCode":400,"message":"One or more errors occurred!","errors":{"name":["'name' must not be empty."]}}""",
            System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Inertia_get_with_failing_validation_is_not_redirected()
    {
        await using var app = await StartAsync();
        var response = await app.CreateClient().InertiaGetAsync("/fe/get-validated");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nested_error_keys_use_json_names_and_the_first_message_wins()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var response = await client.InertiaAsync(HttpMethod.Post, "/fe/nested", new { address = new { street = "" }, items = new[] { new { sku = "" } } });
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var errors = (await (await client.InertiaGetAsync("/page")).PageAsync())["props"]!["errors"]!.AsObject();
        Assert.Equal(["address.street", "items[0].sku"], errors.Select(e => e.Key).Order());
        Assert.False(errors["address.street"] is System.Text.Json.Nodes.JsonArray); // first message only
    }

    [Fact]
    public async Task Precognition_success_failure_and_validate_only()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        void Precog(HttpRequestMessage r, string? only = null)
        {
            r.WithHeader("Precognition", "true");
            if (only is not null)
            {
                r.WithHeader("Precognition-Validate-Only", only);
            }
        }

        // Invalid, no filter: 422 with every error.
        var invalid = await client.SendAsync(Json(HttpMethod.Post, "/fe/precog", new { name = "", email = "" }, r => Precog(r)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal("true", invalid.Header("Precognition"));
        Assert.Contains("Precognition", invalid.Header("Vary"), StringComparison.Ordinal);
        var body = System.Text.Json.Nodes.JsonNode.Parse(await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(["email", "name"], body["errors"]!.AsObject().Select(e => e.Key).Order());
        Assert.False(string.IsNullOrEmpty((string?)body["message"]));

        // Validate-only narrows the errors to the named fields.
        var only = await client.SendAsync(Json(HttpMethod.Post, "/fe/precog", new { name = "", email = "" }, r => Precog(r, "email")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, only.StatusCode);
        Assert.Equal(["email"], System.Text.Json.Nodes.JsonNode.Parse(await only.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["errors"]!.AsObject().Select(e => e.Key));

        // The only field is valid although others are not: success.
        var ok = await client.SendAsync(Json(HttpMethod.Post, "/fe/precog", new { name = "", email = "a@b.c" }, r => Precog(r, "email")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal("true", ok.Header("Precognition"));
        Assert.Equal("true", ok.Header("Precognition-Success"));

        Assert.Equal(0, app.App.Services.GetRequiredService<HandlerCalls>().Count); // the handler never ran

        // A normal request still runs the handler.
        var normal = await client.SendAsync(Json(HttpMethod.Post, "/fe/precog", new { name = "x", email = "a@b.c" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
        Assert.Equal(1, app.App.Services.GetRequiredService<HandlerCalls>().Count);
    }

    private static HttpRequestMessage Json(HttpMethod method, string url, object body, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        configure?.Invoke(request);
        return request;
    }

    public sealed class HandlerCalls
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Hit() => Interlocked.Increment(ref _count);
    }

    public sealed class SendPageEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Get("/fe/send-page");

        public override Task HandleAsync(CancellationToken ct) => Send.InertiaAsync("Send", new InertiaProps { ["x"] = 1 });
    }

    public sealed class SendLocationEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Get("/fe/send-location");

        public override Task HandleAsync(CancellationToken ct) => Send.InertiaLocationAsync("https://external.example/");
    }

    public sealed class SendBackEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Delete("/fe/send-back");

        public override Task HandleAsync(CancellationToken ct) => Send.InertiaBackAsync("/fallback");
    }

    public sealed class ExecutePageEndpoint : EndpointWithoutRequest<InertiaResult>
    {
        public override void Configure() => Get("/fe/execute-page");

        public override Task<InertiaResult> ExecuteAsync(CancellationToken ct) => Task.FromResult(Render("Executed"));
    }

    public sealed class ExecuteBackEndpoint : EndpointWithoutRequest<InertiaBackResult>
    {
        public override void Configure() => Post("/fe/execute-back");

        public override Task<InertiaBackResult> ExecuteAsync(CancellationToken ct) =>
            Task.FromResult(Back().WithErrors(new Dictionary<string, string> { ["name"] = "Nope" }));
    }

    public sealed class GetValidatedRequest
    {
        [QueryParam]
        public string? Q { get; set; }
    }

    public sealed class GetValidatedValidator : Validator<GetValidatedRequest>
    {
        public GetValidatedValidator() => RuleFor(x => x.Q).NotEmpty();
    }

    public sealed class GetValidatedEndpoint : Endpoint<GetValidatedRequest>
    {
        public override void Configure() => Get("/fe/get-validated");

        public override Task HandleAsync(GetValidatedRequest req, CancellationToken ct) => Send.OkAsync(ct);
    }

    public sealed class NestedRequest
    {
        public AddressDto? Address { get; set; }

        public List<ItemDto> Items { get; set; } = [];
    }

    public sealed class AddressDto
    {
        public string? Street { get; set; }
    }

    public sealed class ItemDto
    {
        public string? Sku { get; set; }
    }

    public sealed class NestedValidator : Validator<NestedRequest>
    {
        public NestedValidator()
        {
            RuleFor(x => x.Address!.Street).NotEmpty().MinimumLength(3);
            RuleForEach(x => x.Items).ChildRules(i => i.RuleFor(x => x.Sku).NotEmpty());
        }
    }

    public sealed class NestedEndpoint : Endpoint<NestedRequest>
    {
        public override void Configure() => Post("/fe/nested");

        public override Task HandleAsync(NestedRequest req, CancellationToken ct) => Send.OkAsync(ct);
    }

    public sealed class PrecogRequest
    {
        public string? Name { get; set; }

        public string? Email { get; set; }
    }

    public sealed class PrecogValidator : Validator<PrecogRequest>
    {
        public PrecogValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.Email).NotEmpty();
        }
    }

    public sealed class PrecogEndpoint(HandlerCalls calls) : Endpoint<PrecogRequest>
    {
        public override void Configure() => Post("/fe/precog");

        public override Task HandleAsync(PrecogRequest req, CancellationToken ct)
        {
            calls.Hit();
            return Send.OkAsync(ct);
        }
    }
}
