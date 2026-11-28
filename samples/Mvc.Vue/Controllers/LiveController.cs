using Microsoft.AspNetCore.Mvc;

namespace Mvc.Vue.Controllers;

public sealed class LiveController(IInertiaLive live) : Controller
{
    [HttpGet("/live", Name = "live.index")]
    public IResult Index() => Render("Live/Index", new InertiaProps
    {
        ["counter"] = Live(() => new Counter(AppState.Counter), SampleEvents.CounterChanged),
        // A scroll prop cannot be Live(): the page reloads it with liveReload() when PostCreated arrives.
        ["posts"] = Scroll(Pagination.Paginate(AppState.LivePosts(),
            PageRequest.From(Request.Query, defaultPerPage: AppState.PageSize, maxPerPage: AppState.PageSize))),
    });

    [HttpPost("/live/posts", Name = "live.posts.store")]
    public async Task<IResult> StorePost()
    {
        AppState.AddLivePost();
        // The sender has the new post from this visit's response: leave its connection out.
        await live.PublishAsync(SampleEvents.PostCreated, exceptSocketId: HttpContext.InertiaRequest().SocketId);
        return Back("/live");
    }
}
