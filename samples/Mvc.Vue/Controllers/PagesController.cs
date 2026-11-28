using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mvc.Vue.Controllers;

// Plain MVC controllers (no [ApiController]): they return the core IResult factories (Render, Back), which MVC executes.
public sealed class PagesController : Controller
{
    [HttpGet("/users", Name = "users.index")]
    public IResult Users() => Render("Users/Index", new InertiaProps
    {
        ["users"] = Pagination.Paginate(AppState.Users, PageRequest.From(Request.Query, defaultPerPage: 5, maxPerPage: 5)),
        ["stats"] = Defer(async ct =>
        {
            await Task.Delay(300, ct);
            return new Stats(AppState.Users.Count);
        }),
        ["plans"] = Once(() => new Plans([new("Free", 0), new("Pro", 9)], AppState.NextPlansCount())),
    }).WithHead(h => h.Title("Users").Meta("description", "Paginated users with deferred stats"));

    [HttpDelete("/users/{id:int}", Name = "users.destroy")]
    public IResult DeleteUser(int id)
    {
        AppState.Users.RemoveAll(u => u.Id == id);
        return Back("/users").WithFlash("success", "User deleted");
    }

    [HttpGet("/contacts/create", Name = "contacts.create")]
    public IResult CreateContact() => Render("Contacts/Create");

    // An Inertia request with an invalid ModelState never gets here: the filter redirects back with the errors.
    // The Inertia client posts JSON, so the models need [FromBody] outside [ApiController].
    [HttpPost("/contacts", Name = "contacts.store")]
    public IActionResult StoreContact([FromBody] ContactForm form)
    {
        HttpContext.Inertia().Flash("success", "Contact created");
        return Redirect("/");
    }

    // The same validation for useHttp (an XHR without X-Inertia): the filter answers a 422 with { message, errors } that fills form.errors.
    [HttpPost("/contacts/check", Name = "contacts.check")]
    public IActionResult CheckContact([FromBody] ContactForm form) => NoContent();

    [HttpPost("/newsletter", Name = "newsletter.store")]
    public IActionResult Subscribe([FromBody] NewsletterForm form)
    {
        HttpContext.Inertia().Flash("success", "Subscribed");
        return Redirect("/contacts/create");
    }

    [HttpGet("/feed", Name = "feed.index")]
    public IResult Feed() => Render("Feed/Index", new InertiaProps
    {
        ["posts"] = Scroll(Pagination.Paginate(AppState.Posts,
            PageRequest.From(Request.Query, defaultPerPage: AppState.PageSize, maxPerPage: AppState.PageSize))),
    });

    [HttpGet("/feed/cursor", Name = "feed.cursor")]
    public async Task<IResult> FeedCursor([FromServices] FeedDbContext feed, CancellationToken ct)
    {
        var request = CursorRequest.From(HttpContext, defaultPerPage: AppState.PageSize, maxPerPage: AppState.PageSize);
        var posts = await feed.Posts.AsNoTracking().ToCursorPageAsync(post => post.Id, request, descending: true, ct: ct);
        return Render("Feed/Cursor", new InertiaProps { ["posts"] = Scroll(posts) });
    }
}
