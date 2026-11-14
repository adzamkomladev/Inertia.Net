// Hosting cost of Inertia.Net next to a plain Minimal API that serves the same data: process RSS, requests per second and latency.
// `serve` runs one server (PROBE_MODE=plain, inertia, or todo: a small todo app with cookie sign-in and SQLite); `bench` drives published JIT and Native AOT builds of it from a separate
// process over raw keep-alive sockets. eng/hosting-probe.sh publishes both builds and pins server and client to separate CPUs.
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inertia.Net.HostingProbe;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

if (args is ["bench", var jitDll, var aotBinary, var output, .. var rest])
{
    await Bench.RunAsync(jitDll, aotBinary, output, rest);
    return;
}

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
if (Environment.GetEnvironmentVariable("PROBE_LOG") != "1") builder.Logging.ClearProviders();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, ProbeJsonContext.Default));
var mode = Environment.GetEnvironmentVariable("PROBE_MODE");
var todo = mode == "todo";
var inertia = mode == "inertia" || todo;
if (inertia)
{
    builder.Services.AddInertia(o =>
    {
        o.Version = "1";
        o.Share("appName", "Probe");
        if (todo)
        {
            o.Share("auth", ctx => new Auth(ctx.User.Identity?.Name ?? ""));
        }
        else
        {
            o.Share("auth", new Auth("ada@example.com"));
        }
    });
}

if (todo)
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => o.UseInertiaRedirects());
    builder.Services.AddAuthorization();
    TodoStore.Add(builder.Services);
}

var app = builder.Build();
app.MapGet("/healthz", () => "ok");
if (todo)
{
    app.UseInertia();
    app.UseAuthentication();
    app.UseAuthorization();
    // Signs in user 1 without a form, so the load client can pick up the auth cookie with one request.
    app.MapGet("/probe-login", async (HttpContext http) =>
    {
        var user = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, "ada@example.com")], CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(new ClaimsPrincipal(user));
        http.Response.StatusCode = StatusCodes.Status204NoContent;
    });
    // ClaimsPrincipal as well as HttpContext: a lambda taking only HttpContext and returning a Task binds to the RequestDelegate
    // overload, which discards the returned result.
    app.MapGet("/todos", async (ClaimsPrincipal user, HttpContext http) =>
        Render("Todos/Index", new InertiaProps { ["todos"] = await TodoStore.ForUserAsync(http.RequestServices, int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture)) }))
        .RequireAuthorization();
}
else if (inertia)
{
    app.UseInertia();
    // One route answers both: the first visit gets HTML, an Inertia visit (X-Inertia: true) gets the page JSON.
    app.MapGet("/users", () => Render("Users/Index", new InertiaProps { ["users"] = Data.Users() }));
}
else
{
    // What an app without Inertia serves for the same screen: a JSON API, and an HTML shell with the data embedded in it.
    app.MapGet("/api/users", () => Results.Json(Data.Page(), ProbeJsonContext.Default.UsersPage));
    app.MapGet("/users", () => Results.Text(
        string.Concat(Data.HtmlStart, JsonSerializer.Serialize(Data.Page(), ProbeJsonContext.Default.UsersPage), Data.HtmlEnd),
        "text/html; charset=utf-8"));
}

app.Run();

namespace Inertia.Net.HostingProbe
{
    public sealed record User(int Id, string Name, string Email, DateTime CreatedAt, bool Admin);

    public sealed record Auth(string Email);

    public sealed record UsersPage(string AppName, Auth Auth, User[] Users);

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(User[]))]
    [JsonSerializable(typeof(Auth))]
    [JsonSerializable(typeof(UsersPage))]
    [JsonSerializable(typeof(Todo[]))]
    internal sealed partial class ProbeJsonContext : JsonSerializerContext;

    internal static class Data
    {
        public const string HtmlStart = "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n  <meta charset=\"utf-8\">\n  <title>Users</title>\n</head>\n<body>\n  <script data-page=\"app\" type=\"application/json\">";
        public const string HtmlEnd = "</script><div id=\"app\"></div>\n</body>\n</html>\n";

        private static readonly DateTime Created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Built per request, as a handler that queries its data would.
        public static User[] Users()
        {
            var users = new User[25];
            for (var i = 0; i < users.Length; i++)
            {
                users[i] = new User(i + 1, "User " + (i + 1), $"user{i + 1}@example.com", Created.AddDays(i), i % 5 == 0);
            }

            return users;
        }

        public static UsersPage Page() => new("Probe", new Auth("ada@example.com"), Users());
    }
}
