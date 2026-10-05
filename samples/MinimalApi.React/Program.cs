using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MinimalApi.React;

var e2e = Environment.GetEnvironmentVariable("E2E") == "1";
var ssr = Environment.GetEnvironmentVariable("INERTIA_SSR") == "1";

var builder = WebApplication.CreateBuilder(args);

// The app's source-generated JSON context: Inertia.Net serializes prop values through these options (Native AOT friendly).
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddValidation();
builder.Services.AddInertia(o =>
{
    o.Share("appName", "Inertia.Net Sample");
    o.VersionResolver = _ => AppState.Version; // null: fall back to the hash of the Vite manifest
    o.Ssr.Enabled = ssr;
    o.Ssr.BundlePath = "ssr/ssr.js";
});

var app = builder.Build();

app.UseStaticFiles();
app.UseInertia();

app.MapGet("/", () => Render("Home"));

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
    app.MapPost("/__e2e/bump-version", () => AppState.Version = Guid.NewGuid().ToString("N"));
    app.MapPost("/__e2e/reset", AppState.Reset);
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
    internal sealed partial class AppJsonContext : JsonSerializerContext;

    /// <summary>In-memory demo state shared by all requests (a sample, not a database).</summary>
    internal static class AppState
    {
        public const int PageSize = 10, FeedPages = 5;

        private static int _plansCount;

        public static List<User> Users { get; } = Seed();

        /// <summary>The asset version override; null means the hash of the Vite manifest.</summary>
        public static string? Version { get; set; }

        /// <summary>How many times the once prop was actually resolved.</summary>
        public static int NextPlansCount() => Interlocked.Increment(ref _plansCount);

        public static IResult Reset()
        {
            Users.Clear();
            Users.AddRange(Seed());
            Version = null;
            Interlocked.Exchange(ref _plansCount, 0);
            return Results.NoContent();
        }

        private static List<User> Seed() => [new(1, "Ada Lovelace"), new(2, "Grace Hopper"), new(3, "Alan Turing")];
    }
}
