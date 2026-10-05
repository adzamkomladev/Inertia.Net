using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Inertia.Net.Mvc;

namespace Inertia.Net.IntegrationTests;

/// <summary>The MVC conformance host: <see cref="ConformanceController"/> maps the route contract. Controllers return the core <c>IResult</c> factories.</summary>
public sealed class MvcHost : IConformanceHost
{
    public static void ConfigureServices(IServiceCollection services) => services.AddControllersWithViews().AddInertiaMvc();

    public static void MapRoutes(WebApplication app) => app.MapControllers();
}

public sealed class ConformanceController(ConformanceCounters counters) : Controller
{
    [HttpGet("/page")]
    public IResult Page()
    {
        counters.HitPage();
        return Render("Page", new InertiaProps { ["message"] = "hello" });
    }

    [HttpGet("/café")]
    public IResult Cafe() => Render("Page");

    [HttpGet("/partial")]
    public IResult Partial() => Render("Partial", new InertiaProps
    {
        ["a"] = 1,
        ["b"] = Prop(() => 2),
        ["c"] = Always(3),
        ["d"] = Optional(() => 4),
    });

    [HttpGet("/deferred")]
    public IResult Deferred() => Render("Deferred", new InertiaProps
    {
        ["a"] = 1,
        ["b"] = Defer(ct => Task.FromResult("b")),
        ["c"] = Defer(() => "c", "other"),
    });

    [AcceptVerbs("POST", "PUT", "PATCH", "DELETE")]
    [Route("/form")]
    public IActionResult Form([FromBody] FormInput input)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState); // non-Inertia requests; Inertia ones never get here
        }

        HttpContext.Inertia().Flash("success", $"Saved {input.Name}");
        return Redirect("/page");
    }

    [AcceptVerbs("POST", "PUT", "PATCH", "DELETE")]
    [Route("/redirect")]
    public IActionResult RedirectToPage() => Redirect("/page");

    [HttpPost("/flash/{n:int}")]
    public IActionResult Flash(int n)
    {
        HttpContext.Inertia().Flash("n", n);
        return Redirect($"/isolated/{n}");
    }

    [HttpGet("/isolated/{n:int}")]
    public IResult Isolated(int n)
    {
        HttpContext.Inertia().Share("shared", n).EncryptHistory(n % 2 == 0);
        return Render("Isolated", new InertiaProps
        {
            ["n"] = Prop(async () =>
            {
                await Task.Yield();
                return n;
            }),
        });
    }

    [HttpGet("/fragment-redirect")]
    public IActionResult FragmentRedirect() => Redirect("/page#section");

    [HttpGet("/external")]
    public IResult External() => Location("https://external.example/path");

    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE")]
    [Route("/empty")]
    public IActionResult EmptyOk() => Ok();

    [HttpGet("/clear-history")]
    public IActionResult ClearHistory()
    {
        HttpContext.Inertia().ClearHistory();
        return Redirect("/page");
    }

    [HttpGet("/preserve-fragment")]
    public IActionResult PreserveFragment()
    {
        HttpContext.Inertia().PreserveFragment();
        return Redirect("/page");
    }

    [HttpGet("/encrypt")]
    public IResult Encrypt()
    {
        HttpContext.Inertia().EncryptHistory();
        return Render("Page");
    }

    [HttpGet("/error-page")]
    public IResult ErrorPage() => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);

    // MVC-specific routes, used by MvcTests.
    [HttpPost("/mvc/nested")]
    public IActionResult Nested([FromBody] MvcNestedInput input) => Ok();

    [HttpPost("/mvc/back")]
    public IResult BackWithErrors() => Back("/fallback").WithErrors(new Dictionary<string, string> { ["name"] = "Nope" }).WithFlash("toast", "Check the form");

    [HttpGet("/mvc/razor")]
    public IResult Razor() => Render("Razor", new InertiaProps { ["message"] = "hello" }).WithViewData("title", "<Razor> & co");

    [HttpGet("/mvc/razor-other")]
    public IResult RazorOther() => Render("Razor").WithRootView("Other");
}

/// <summary>An <c>[ApiController]</c>: its automatic 400 must not win over the Inertia redirect.</summary>
[ApiController]
public sealed class ApiFormController : ControllerBase
{
    [HttpPost("/mvc/api-form")]
    public IActionResult Post(FormInput input) => Redirect("/page");
}

public sealed class MvcNestedInput
{
    [Required]
    public MvcAddress? Address { get; set; }

    [Required]
    public string? FirstName { get; set; }
}

public sealed class MvcAddress
{
    [Required]
    public string? Street { get; set; }
}
