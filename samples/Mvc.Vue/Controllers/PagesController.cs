using Microsoft.AspNetCore.Mvc;

namespace Mvc.Vue.Controllers;

// Plain MVC controllers (no [ApiController]): they return the core IResult factories (Render, Back), which MVC executes.
public sealed class PagesController : Controller
{
    [HttpGet("/")]
    public IResult Home() => Render("Home");

    [HttpGet("/users")]
    public IResult Users() => Render("Users/Index", new InertiaProps
    {
        ["users"] = AppState.Users.ToList(),
        ["stats"] = Defer(async ct =>
        {
            await Task.Delay(300, ct);
            return new Stats(AppState.Users.Count);
        }),
        ["plans"] = Once(() => new Plans([new("Free", 0), new("Pro", 9)], AppState.NextPlansCount())),
    });

    [HttpDelete("/users/{id:int}")]
    public IResult DeleteUser(int id)
    {
        AppState.Users.RemoveAll(u => u.Id == id);
        return Back("/users").WithFlash("success", "User deleted");
    }

    [HttpGet("/contacts/create")]
    public IResult CreateContact() => Render("Contacts/Create");

    // An Inertia request with an invalid ModelState never gets here: the filter redirects back with the errors.
    // The Inertia client posts JSON, so the models need [FromBody] outside [ApiController].
    [HttpPost("/contacts")]
    public IActionResult StoreContact([FromBody] ContactForm form)
    {
        HttpContext.Inertia().Flash("success", "Contact created");
        return Redirect("/");
    }

    [HttpPost("/newsletter")]
    public IActionResult Subscribe([FromBody] NewsletterForm form)
    {
        HttpContext.Inertia().Flash("success", "Subscribed");
        return Redirect("/contacts/create");
    }

    [HttpGet("/feed")]
    public IResult Feed() => Render("Feed/Index", new InertiaProps
    {
        ["posts"] = Scroll(Pagination.Paginate(AppState.Posts,
            PageRequest.From(Request.Query, defaultPerPage: AppState.PageSize, maxPerPage: AppState.PageSize))),
    });
}
