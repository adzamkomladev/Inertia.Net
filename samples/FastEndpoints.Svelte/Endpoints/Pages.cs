using FastEndpoints;
using Inertia.Net.FastEndpoints;

namespace FastEndpointsSvelte.Endpoints;

// Style 1: return the Inertia result from ExecuteAsync (FastEndpoints sends any IResult).
public sealed class HomeEndpoint : EndpointWithoutRequest<InertiaResult>
{
    public override void Configure() => Get("/");

    public override Task<InertiaResult> ExecuteAsync(CancellationToken ct) => Task.FromResult(Render("Home"));
}

// Style 2: Send.InertiaAsync from HandleAsync.
public sealed class UsersEndpoint : EndpointWithoutRequest
{
    public override void Configure() => Get("/users");

    public override Task HandleAsync(CancellationToken ct) => Send.InertiaAsync("Users/Index", new InertiaProps
    {
        ["users"] = AppState.Users.ToList(),
        ["stats"] = Defer(async ct =>
        {
            await Task.Delay(300, ct);
            return new Stats(AppState.Users.Count);
        }),
        ["plans"] = Once(() => new Plans([new("Free", 0), new("Pro", 9)], AppState.NextPlansCount())),
    });
}

public sealed class CreateContactPageEndpoint : EndpointWithoutRequest<InertiaResult>
{
    public override void Configure() => Get("/contacts/create");

    public override Task<InertiaResult> ExecuteAsync(CancellationToken ct) => Task.FromResult(Render("Contacts/Create"));
}

public sealed class FeedEndpoint : EndpointWithoutRequest
{
    public override void Configure() => Get("/feed");

    public override Task HandleAsync(CancellationToken ct) => Send.InertiaAsync("Feed/Index", new InertiaProps
    {
        ["posts"] = Scroll(Pagination.Paginate(AppState.Posts,
            PageRequest.From(HttpContext.Request.Query, defaultPerPage: AppState.PageSize, maxPerPage: AppState.PageSize))),
    });
}
