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

public sealed class FeedEndpoint : Endpoint<FeedRequest>
{
    public override void Configure() => Get("/feed");

    public override Task HandleAsync(FeedRequest req, CancellationToken ct)
    {
        var page = Math.Clamp(req.Page, 1, AppState.FeedPages);
        var posts = Enumerable.Range((page - 1) * AppState.PageSize + 1, AppState.PageSize).Select(i => new Post(i, $"Post {i}")).ToList();
        return Send.InertiaAsync("Feed/Index", new InertiaProps
        {
            ["posts"] = Scroll(new PostPage(posts), metadata: ScrollMetadata.FromPage(page, page < AppState.FeedPages)),
        });
    }
}
