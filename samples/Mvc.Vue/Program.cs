using System.Globalization;
using Inertia.Net.Live.SignalR;
using Inertia.Net.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mvc.Vue;

var e2e = Environment.GetEnvironmentVariable("E2E") == "1";
var ssr = Environment.GetEnvironmentVariable("INERTIA_SSR") == "1";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation(); // for the Minimal API login demo (LoginDemo.cs); MVC validates with ModelState
LoginDemo.AddServices(builder.Services);
// Keep a named in-memory database alive; request-scoped contexts use their own connections.
const string feedConnectionString = "Data Source=InertiaVueFeed;Mode=Memory;Cache=Shared";
builder.Services.AddSingleton(_ =>
{
    var connection = new SqliteConnection(feedConnectionString);
    connection.Open();
    return connection;
});
builder.Services.AddDbContext<FeedDbContext>(options => options.UseSqlite(feedConnectionString));
builder.Services.AddInertia(o =>
{
    o.State.RememberPreviousUrl = true;
    o.Share("appName", "Inertia.Net Sample");
    o.Head.TitleTemplate = "{0} - Inertia.Net Sample"; // Render(...).WithHead(h => h.Title("Users"))
    o.VersionResolver = _ => AppState.Version; // null: fall back to the hash of the Vite manifest
    o.Vite.PageModule = component => $"ClientApp/pages/{component}.vue";
    o.Vite.Prefetch = VitePrefetch.Waterfall; // fetch the other pages' chunks after load, three at a time
    // Development only: rendering a component without a page file fails fast, and the TypeScript declarations are rewritten at startup.
    o.Pages.Directory = "ClientApp/pages";
    o.TypeScript.OutputPath = "ClientApp/types/inertia.d.ts";
    o.TypeScript.FlashData<Flash>().Page<LoginPage>();
    o.TypeScript.RoutesOutputPath = "ClientApp/types/routes.ts"; // one typed function per named endpoint
    o.TypeScript.LiveEvents(SampleEvents.CounterChanged, SampleEvents.InboxChanged, SampleEvents.PostCreated); // exported from routes.ts
    o.Ssr.BundlePath = "ssr/ssr.js";
    // A live prop on every page: the demo user's private channel (the sample has no sign-in, so the demo user is always id 1).
    o.Share("inbox", Prop(() => new Inbox(AppState.Unread)).Live(SampleEvents.InboxChanged));
    if (ssr)
    {
        // Sets Ssr.Mode to Preferred. The port must match the one baked into the SSR bundle (vite.config.ts).
        o.Ssr.UseNodeProcess(n => n.BasePort = int.Parse(Environment.GetEnvironmentVariable("INERTIA_SSR_PORT") ?? "13714", CultureInfo.InvariantCulture));
    }
});

// Live props over SignalR: the hub is mapped below; the client enables it with createInertiaApp({ live: signalr() }).
// LIVE_TRANSPORT=sse uses Server-Sent Events instead (build the client with VITE_LIVE_TRANSPORT=sse for its sse() transport).
var sse = Environment.GetEnvironmentVariable("LIVE_TRANSPORT") == "sse";
var live = sse
    ? builder.Services.AddInertiaLive().AddSse(o =>
        o.Authorize((_, channel, _) => ValueTask.FromResult(channel == SampleEvents.InboxChanged.Channel)))
    : builder.Services.AddInertiaLive().AddSignalR(o =>
        o.Authorize((_, channel, _) => ValueTask.FromResult(channel == SampleEvents.InboxChanged.Channel)));
// LIVE_BACKPLANE=redis|postgres runs several instances (or a worker) on one backplane: start the app twice on two ports and a publish
// on either reaches the pages of both. REDIS_URL / POSTGRES_URL point at the store.
var backplane = Environment.GetEnvironmentVariable("LIVE_BACKPLANE");
if (backplane == "redis")
{
    live.AddRedisBackplane(o => o.Configuration = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379");
}
else if (backplane == "postgres")
{
    live.AddPostgresBackplane(o => o.ConnectionString = Environment.GetEnvironmentVariable("POSTGRES_URL")
        ?? "Host=localhost;Username=postgres;Password=postgres");
}

builder.Services.AddResponseCompression(); // gzip and Brotli for the page JSON and HTML (see the README on HTTPS)
// Keep development diagnostics unless the browser tests explicitly enable error pages.
builder.Services.AddInertiaErrorPages(o =>
{
    o.EnabledInDevelopment = e2e;
    o.StatusCodes.Add(StatusCodes.Status429TooManyRequests);
});
builder.Services.AddInertiaDevTools(); // the Inertia DevTools browser extension, in Development only
// The first-visit HTML is Views/Shared/App.cshtml (tag helpers) instead of an app.html template; the ModelState filter turns
// validation failures of Inertia requests into a redirect back with the errors.
builder.Services.AddControllersWithViews().AddInertiaMvc(o => o.UseRazorRootView("App"));
// CSRF: the Inertia client sends the XSRF-TOKEN cookie back as X-XSRF-TOKEN; UseInertiaAntiforgery() issues and validates it.
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);

var app = builder.Build();
_ = app.Services.GetRequiredService<SqliteConnection>();
using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<FeedDbContext>();
    database.Database.EnsureCreated();
    database.Posts.AddRange(AppState.Posts);
    database.SaveChanges();
}

app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    // Vite puts a content hash in every file name under build/assets, so browsers can keep them for good.
    OnPrepareResponse = c =>
    {
        if (c.Context.Request.Path.StartsWithSegments("/build/assets"))
        {
            c.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    },
});
app.UseInertiaErrorPages(); // before UseInertia(): handle errors from the whole page pipeline, rate-limit rejections included
app.UseRouting();
app.UseInertia();
app.UseRateLimiter(); // after UseInertia(), which saves the flash data of a rejection
app.UseInertiaAntiforgery(); // after UseAuthentication() in an app with sign-in
app.MapControllers();
app.MapInertiaDevTools();
if (sse) app.MapInertiaLiveSse(); // /inertia-live
else app.MapInertiaLiveHub(); // /inertia-live
// A page without a handler; its generated route (routes.home()) names the component, so <Link instant> can show it before the response.
app.MapInertia("/", "Home").WithName("home");
LoginDemo.MapRoutes(app);

if (e2e)
{
    app.MapGet("/__e2e/boom", (Func<IResult>)(() => throw new InvalidOperationException("E2E error page test")));
    // Test hooks called by the E2E runner, not by a browser page: exempt from CSRF validation.
    app.MapPost("/__e2e/bump-version", () => AppState.Version = Guid.NewGuid().ToString("N")).DisableAntiforgery();
    app.MapPost("/__e2e/reset", AppState.Reset).DisableAntiforgery();
    app.MapPost("/__e2e/pagination/seed-users", () =>
    {
        AppState.Users.AddRange(Enumerable.Range(4, 9).Select(id => new User(id, $"User {id}")));
        return Results.NoContent();
    }).DisableAntiforgery();
    // Live props: publish as another user would (no socket to leave out).
    app.MapPost("/__e2e/live/increment", async (IInertiaLive live) =>
    {
        AppState.IncrementCounter();
        await live.PublishAsync(SampleEvents.CounterChanged);
    }).DisableAntiforgery();
    app.MapPost("/__e2e/live/increment-with-value", async (IInertiaLive live) =>
    {
        var value = AppState.IncrementCounter();
        await live.PublishAsync(SampleEvents.CounterChanged, new CounterUpdate(new Counter(value)));
    }).DisableAntiforgery();
    // Cuts this backplane's subscription connections, as a store restart would: every instance then resyncs its live clients.
    app.MapPost("/__e2e/live/backplane-gap", async () =>
    {
        if (backplane == "redis")
        {
            await using var redis = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(
                (Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379") + ",allowAdmin=true");
            await redis.GetServer(redis.GetEndPoints()[0]).ExecuteAsync("CLIENT", "KILL", "TYPE", "pubsub");
        }
        else if (backplane == "postgres")
        {
            await using var connection = new Npgsql.NpgsqlConnection(Environment.GetEnvironmentVariable("POSTGRES_URL")
                ?? "Host=localhost;Username=postgres;Password=postgres");
            await connection.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE query ILIKE 'LISTEN%' AND pid <> pg_backend_pid()", connection);
            await command.ExecuteNonQueryAsync();
        }
    }).DisableAntiforgery();
    app.MapPost("/__e2e/live/posts", async (IInertiaLive live, int? count) =>
    {
        for (var i = 0; i < (count ?? 1); i++)
        {
            AppState.AddLivePost();
            await live.PublishAsync(SampleEvents.PostCreated);
        }
    }).DisableAntiforgery();
    app.MapPost("/__e2e/live/inbox", async (IInertiaLive live) =>
    {
        AppState.IncrementUnread();
        await live.PublishAsync(SampleEvents.InboxChanged);
    }).DisableAntiforgery();
    app.MapGet("/__e2e/array", () => Render("ArrayRegression", new InertiaProps
    {
        ["rows"] = new List<DeferredRow>
        {
            new("first", Defer(() => 10, "a")),
            new("second", Defer(() => 20, "b")),
        },
    }));
}

app.Run();
