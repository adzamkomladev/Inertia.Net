using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Inertia.Net.Live.SignalR;
using MinimalApi.React;

var e2e = Environment.GetEnvironmentVariable("E2E") == "1";
var ssr = Environment.GetEnvironmentVariable("INERTIA_SSR") == "1";

var builder = WebApplication.CreateBuilder(args);

// The app's source-generated JSON context: Inertia.Net serializes prop values through these options (Native AOT friendly).
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddValidation();
// CSRF: the Inertia client sends the XSRF-TOKEN cookie back as X-XSRF-TOKEN; UseInertiaAntiforgery() issues and validates it.
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
builder.Services.AddResponseCompression(); // gzip and Brotli for the page JSON and HTML (see the README on HTTPS)
builder.Services.AddInertia(o =>
{
    o.Share("appName", "Inertia.Net Sample");
    o.VersionResolver = _ => AppState.Version; // null: fall back to the hash of the Vite manifest
    o.Vite.Prefetch = VitePrefetch.Waterfall; // fetch the other pages' chunks after load, three at a time
    // Development only: Render("Users/Indx") fails fast, and the TypeScript declarations are rewritten at startup.
    o.Pages.Directory = "ClientApp/pages";
    o.TypeScript.OutputPath = "ClientApp/types/inertia.d.ts";
    o.TypeScript.FlashData<Flash>();
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
builder.Services.AddInertiaLive().AddSignalR(o =>
    o.Authorize((_, channel, _) => ValueTask.FromResult(channel == SampleEvents.InboxChanged.Channel)));

builder.Services.AddInertiaDevTools(); // the Inertia DevTools browser extension, in Development only

var app = builder.Build();

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
app.UseInertia();
app.UseInertiaAntiforgery(); // after UseAuthentication() in an app with sign-in

app.MapInertiaDevTools();
app.MapInertiaLiveHub(); // /inertia-live
app.MapInertia("/", "Home");

app.MapGet("/users", () => Render("Users/Index", new InertiaProps
{
    ["users"] = AppState.Users.ToList(),
    ["stats"] = Defer(async ct =>
    {
        await Task.Delay(300, ct);
        return new Stats(AppState.Users.Count);
    }),
    ["plans"] = Once(() => new Plans([new("Free", 0), new("Pro", 9)], AppState.NextPlansCount())),
}));

app.MapDelete("/users/{id:int}", (int id) =>
{
    AppState.Users.RemoveAll(u => u.Id == id);
    return Back("/users").WithFlash("success", "User deleted");
});

app.MapGet("/live", () => Render("Live/Index", new InertiaProps
{
    ["counter"] = Live(() => new Counter(AppState.Counter), SampleEvents.CounterChanged),
}));

app.MapGet("/contacts/create", () => Render("Contacts/Create"));

app.MapPost("/contacts", (HttpContext http, [FromBody] ContactForm form) =>
{
    http.Inertia().Flash("success", "Contact created");
    return Results.Redirect("/");
}).WithInertiaValidation();

app.MapPost("/newsletter", (HttpContext http, [FromBody] NewsletterForm form) =>
{
    http.Inertia().Flash("success", "Subscribed");
    return Results.Redirect("/contacts/create");
}).WithInertiaValidation();

app.MapGet("/feed", (int page = 1) =>
{
    page = Math.Clamp(page, 1, AppState.FeedPages);
    var posts = Enumerable.Range((page - 1) * AppState.PageSize + 1, AppState.PageSize).Select(i => new Post(i, $"Post {i}")).ToList();
    return Render("Feed/Index", new InertiaProps
    {
        ["posts"] = Scroll(new PostPage(posts), metadata: ScrollMetadata.FromPage(page, page < AppState.FeedPages)),
    });
});

if (e2e)
{
    // Test hooks called by the E2E runner, not by a browser page: exempt from CSRF validation.
    app.MapPost("/__e2e/bump-version", () => AppState.Version = Guid.NewGuid().ToString("N")).DisableAntiforgery();
    app.MapPost("/__e2e/reset", AppState.Reset).DisableAntiforgery();
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

namespace MinimalApi.React
{
    public sealed record User(int Id, string Name);

    public sealed record Stats(int Total);

    public sealed record Plan(string Name, int Price);

    public sealed record Plans(List<Plan> Items, int ResolvedCount);

    public sealed record Post(int Id, string Title);

    public sealed record PostPage(List<Post> Data);

    public sealed record DeferredRow(string Name, InertiaProp Extra);

    public sealed record Counter(int Value);

    /// <summary>Live values published with <c>CounterChanged</c>, keyed by prop path.</summary>
    public sealed record CounterUpdate(Counter Counter);

    public sealed record Inbox(int Unread);

    /// <summary>The live events, shared by the props that listen and the code that publishes.</summary>
    public static class SampleEvents
    {
        public static LiveEvent CounterChanged { get; } = new("CounterChanged", LiveChannel.Public("counter"));

        public static LiveEvent InboxChanged { get; } = new("InboxChanged", LiveChannel.Private("users.1"));
    }

    /// <summary>The flash data the pages read (<c>page.flash</c>), for the generated TypeScript declarations.</summary>
    public sealed record Flash(string? Success);

    public sealed class ContactForm
    {
        [Required]
        public string? Name { get; set; }

        [Required, EmailAddress]
        public string? Email { get; set; }
    }

    public sealed class NewsletterForm
    {
        [Required, EmailAddress]
        public string? Email { get; set; }
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(List<User>))]
    [JsonSerializable(typeof(Stats))]
    [JsonSerializable(typeof(Plans))]
    [JsonSerializable(typeof(PostPage))]
    [JsonSerializable(typeof(ContactForm))]
    [JsonSerializable(typeof(NewsletterForm))]
    [JsonSerializable(typeof(List<DeferredRow>))]
    [JsonSerializable(typeof(Flash))]
    [JsonSerializable(typeof(Counter))]
    [JsonSerializable(typeof(CounterUpdate))]
    [JsonSerializable(typeof(Inbox))]
    internal sealed partial class AppJsonContext : JsonSerializerContext;

    /// <summary>In-memory demo state shared by all requests (a sample, not a database).</summary>
    internal static class AppState
    {
        public const int PageSize = 10, FeedPages = 5;

        private static int _plansCount, _counter, _unread;

        public static List<User> Users { get; } = Seed();

        /// <summary>The asset version override; null means the hash of the Vite manifest.</summary>
        public static string? Version { get; set; }

        /// <summary>How many times the once prop was actually resolved.</summary>
        public static int NextPlansCount() => Interlocked.Increment(ref _plansCount);

        /// <summary>The live counter on the Live page.</summary>
        public static int Counter => Volatile.Read(ref _counter);

        public static int IncrementCounter() => Interlocked.Increment(ref _counter);

        /// <summary>The demo user's unread messages, a shared live prop.</summary>
        public static int Unread => Volatile.Read(ref _unread);

        public static int IncrementUnread() => Interlocked.Increment(ref _unread);

        public static IResult Reset()
        {
            Users.Clear();
            Users.AddRange(Seed());
            Version = null;
            Interlocked.Exchange(ref _plansCount, 0);
            Interlocked.Exchange(ref _counter, 0);
            Interlocked.Exchange(ref _unread, 0);
            return Results.NoContent();
        }

        private static List<User> Seed() => [new(1, "Ada Lovelace"), new(2, "Grace Hopper"), new(3, "Alan Turing")];
    }
}
