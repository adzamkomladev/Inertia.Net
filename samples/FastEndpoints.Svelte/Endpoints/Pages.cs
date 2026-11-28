using FastEndpoints;
using Inertia.Net.FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace FastEndpointsSvelte.Endpoints;

// Style 1: return the Inertia result from ExecuteAsync (FastEndpoints sends any IResult).
public sealed class HomeEndpoint : EndpointWithoutRequest<InertiaResult>
{
    public override void Configure()
    {
        Get("/");
        Description(b => b.WithName("home").WithInertiaComponent("Home")); // routes.home() can open the page instantly
    }

    public override Task<InertiaResult> ExecuteAsync(CancellationToken ct) => Task.FromResult(Render("Home"));
}

// Style 2: Send.InertiaAsync from HandleAsync.
public sealed class UsersEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/users");
        Description(b => b.WithName("users.index"));
    }

    public override Task HandleAsync(CancellationToken ct) => Send.ResultAsync(Render("Users/Index", new InertiaProps
    {
        ["users"] = Pagination.Paginate(AppState.Users, PageRequest.From(HttpContext.Request.Query, defaultPerPage: 5, maxPerPage: 5)),
        ["stats"] = Defer(async ct =>
        {
            await Task.Delay(300, ct);
            return new Stats(AppState.Users.Count);
        }),
        ["plans"] = Once(() => new Plans([new("Free", 0), new("Pro", 9)], AppState.NextPlansCount())),
    }).WithHead(h => h.Title("Users").Meta("description", "Paginated users with deferred stats")));
}

public sealed class CreateContactPageEndpoint : EndpointWithoutRequest<InertiaResult>
{
    public override void Configure()
    {
        Get("/contacts/create");
        Description(b => b.WithName("contacts.create"));
    }

    public override Task<InertiaResult> ExecuteAsync(CancellationToken ct) => Task.FromResult(Render("Contacts/Create"));
}

public sealed class FeedEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/feed");
        Description(b => b.WithName("feed.index"));
    }

    public override Task HandleAsync(CancellationToken ct) => Send.InertiaAsync("Feed/Index", new InertiaProps
    {
        ["posts"] = Scroll(Pagination.Paginate(AppState.Posts,
            PageRequest.From(HttpContext.Request.Query, defaultPerPage: AppState.PageSize, maxPerPage: AppState.PageSize))),
    });
}

public sealed class FeedCursorEndpoint(FeedDbContext database) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/feed/cursor");
        Description(b => b.WithName("feed.cursor"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var request = CursorRequest.From(HttpContext, defaultPerPage: AppState.PageSize, maxPerPage: AppState.PageSize);
        var posts = await database.Posts.AsNoTracking().ToCursorPageAsync(post => post.Id, request, descending: true, ct: ct);
        await Send.InertiaAsync("Feed/Cursor", new InertiaProps { ["posts"] = Scroll(posts) });
    }
}

public sealed class LiveEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/live");
        Description(b => b.WithName("live.index"));
    }

    public override Task HandleAsync(CancellationToken ct) => Send.InertiaAsync("Live/Index", new InertiaProps
    {
        ["counter"] = Live(() => new Counter(AppState.Counter), SampleEvents.CounterChanged),
        // A scroll prop cannot be Live(): the page reloads it with liveReload() when PostCreated arrives.
        ["posts"] = Scroll(Pagination.Paginate(AppState.LivePosts(),
            PageRequest.From(HttpContext.Request.Query, defaultPerPage: AppState.PageSize, maxPerPage: AppState.PageSize))),
    });
}
