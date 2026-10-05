using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

/// <summary>
/// The Minimal API conformance host. Factories are called unqualified (<c>Render</c>, <c>Location</c>, <c>Prop</c>...) through the
/// global usings that <c>buildTransitive/Inertia.Net.props</c> adds; this project imports that file like a package consumer would.
/// </summary>
public sealed class MinimalApiHost : IConformanceHost
{
    private static readonly string[] Mutations = [HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete];

    public static void ConfigureServices(IServiceCollection services) => services.AddValidation();

    public static void MapRoutes(WebApplication app)
    {
        app.MapGet("/page", (ConformanceCounters counters) =>
        {
            counters.HitPage();
            return Render("Page", new InertiaProps { ["message"] = "hello" });
        });

        app.MapGet("/café", () => Render("Page"));

        app.MapGet("/partial", () => Render("Partial", new InertiaProps
        {
            ["a"] = 1,
            ["b"] = Prop(() => 2),
            ["c"] = Always(3),
            ["d"] = Optional(() => 4),
        }));

        app.MapGet("/deferred", () => Render("Deferred", new InertiaProps
        {
            ["a"] = 1,
            ["b"] = Defer(ct => Task.FromResult("b")),
            ["c"] = Defer(() => "c", "other"),
        }));

        app.MapMethods("/form", Mutations, (HttpContext context, [FromBody] FormInput input) =>
        {
            context.Inertia().Flash("success", $"Saved {input.Name}");
            return Results.Redirect("/page");
        }).WithInertiaValidation();

        app.MapMethods("/redirect", Mutations, () => Results.Redirect("/page"));

        app.MapPost("/flash/{n:int}", (HttpContext context, int n) =>
        {
            context.Inertia().Flash("n", n);
            return Results.Redirect($"/isolated/{n}");
        });

        app.MapGet("/isolated/{n:int}", (HttpContext context, int n) =>
        {
            context.Inertia().Share("shared", n).EncryptHistory(n % 2 == 0);
            return Render("Isolated", new InertiaProps
            {
                ["n"] = Prop(async () =>
                {
                    await Task.Yield();
                    return n;
                }),
            });
        });

        app.MapGet("/fragment-redirect", () => Results.Redirect("/page#section"));
        app.MapGet("/external", () => Location("https://external.example/path"));
        app.MapMethods("/empty", [.. Mutations, HttpMethods.Get], () => Results.Ok());

        app.MapGet("/clear-history", (HttpContext context) =>
        {
            context.Inertia().ClearHistory();
            return Results.Redirect("/page");
        });

        app.MapGet("/preserve-fragment", (HttpContext context) =>
        {
            context.Inertia().PreserveFragment();
            return Results.Redirect("/page");
        });

        app.MapGet("/encrypt", (HttpContext context) =>
        {
            context.Inertia().EncryptHistory();
            return Render("Page");
        });

        app.MapGet("/error-page", () => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404));
    }
}

/// <summary>The <c>/form</c> body: <c>name</c> is required.</summary>
public sealed class FormInput
{
    [Required]
    public string? Name { get; set; }
}
