// A Native AOT Minimal API app exercising the Inertia.Net surface; tests/Inertia.Net.AotSmoke/smoke.sh drives the published binary.
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FastEndpoints;
using Inertia.Net.AotSmoke; // FastEndpoints.Generator output (DiscoveredTypes, ReflectionCache.AddFromInertiaNetAotSmoke)
using SmokeEndpoints;
using Inertia.Net.FastEndpoints;

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddValidation();
builder.Services.AddFastEndpoints(DiscoveredTypes.All);
// Registers the ProblemDetails JSON metadata: without it (reflection off) the 400 for non-Inertia clients cannot be serialized.
builder.Services.AddProblemDetails();
builder.Services.AddInertia(o =>
{
    o.PreserveBigIntegers = true;
    o.Share("appName", "AotSmoke");
    o.Share("auth", ctx => new AuthInfo(ctx.Request.Headers["X-User"].ToString() is { Length: > 0 } user ? user : null));
});

var app = builder.Build();
app.UseInertia();
app.UseFastEndpoints(c =>
{
    c.UseInertia();
    c.Serializer.Options.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    c.Binding.ReflectionCache.AddFromInertiaNetAotSmoke();
});

app.MapGet("/healthz", () => "ok");

// Typed page props: plain members are serialized through AppJsonContext; InertiaProp members are resolved by Inertia.Net.
app.MapGet("/", () => Render("Home", new HomePage
{
    Title = "Home",
    Big = 9_007_199_254_740_993,
    Stats = Defer(() => new[] { new Stat("users", 42), new Stat("orders", 7) }),
    Plans = Once(ct => Task.FromResult(new[] { "free", "pro" })),
    Feed = Merge(new[] { new Stat("item", 1) }),
}).WithViewData("title", "Smoke & Mirrors"));

app.MapGet("/dashboard", (HttpContext context) =>
{
    context.Inertia().Share("requestShared", true);
    var page = int.TryParse(context.Request.Query["page"], out var p) ? p : 1;
    return Render("Dashboard", new InertiaProps
    {
        ["title"] = "Dashboard",
        ["count"] = 3,
        ["tags"] = new[] { "a", "b" },
        ["user"] = new InertiaProps { ["name"] = "Ada", ["roles"] = Always(new[] { "admin" }) },
        ["stats"] = Defer(ct => ValueTask.FromResult(new Stat("visits", 1234))),
        ["chart"] = Defer(() => new[] { 1, 2, 3 }, "charts"),
        ["countries"] = Once(() => new[] { "BE", "FR" }).Until(TimeSpan.FromHours(1)),
        ["feed"] = Scroll(() => new FeedPage([new Stat($"post-{page}", page)], page, page < 3)),
        ["filters"] = Always(() => "all"),
        ["lazy"] = Optional(() => "loaded"),
    });
});

app.MapGet("/users/create", () => Render("Users/Create"));

app.MapPost("/users", (HttpContext context, [Required] UserInput input) =>
{
    context.Inertia().Flash("toast", $"Created {input.Name}");
    return Results.Redirect("/dashboard");
}).WithInertiaValidation();

app.MapPut("/users/{id:int}", (int id) => Results.Redirect("/dashboard"));

app.MapPost("/back", () => Back("/users/create")
    .WithErrors(new Dictionary<string, string> { ["email"] = "Taken" }, "createUser")
    .WithFlash("toast", "Rejected"));

app.MapPost("/flash", (HttpContext context) =>
{
    context.Inertia().Flash("toast", "Hello");
    return Results.Redirect("/dashboard");
});

app.MapGet("/external", () => Location("https://example.com/landing"));

app.Run();

internal sealed class HomePage
{
    public required string Title { get; init; }

    public long Big { get; init; }

    public required InertiaProp Stats { get; init; }

    public required InertiaProp Plans { get; init; }

    public required InertiaProp Feed { get; init; }
}

internal sealed record Stat(string Name, int Value);

internal sealed record AuthInfo(string? User);

internal sealed record FeedPage(Stat[] Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}

// Must be public: the AddValidation() source generator skips internal types (and validation then silently does nothing).
public sealed class UserInput
{
    [Required]
    [StringLength(50)]
    public string? Name { get; set; }

    [Required]
    [EmailAddress]
    public string? Email { get; set; }
}

[JsonSerializable(typeof(HomePage))]
[JsonSerializable(typeof(Stat))]
[JsonSerializable(typeof(Stat[]))]
[JsonSerializable(typeof(AuthInfo))]
[JsonSerializable(typeof(FeedPage))]
[JsonSerializable(typeof(UserInput))]
[JsonSerializable(typeof(ContactRequest))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
