// Hosting cost of Inertia.Net next to a plain Minimal API that serves the same data: process RSS, requests per second and latency.
// `serve` runs one server (PROBE_MODE=plain or inertia); `bench` drives published JIT and Native AOT builds of it from a separate
// process over raw keep-alive sockets. eng/hosting-probe.sh publishes both builds and pins server and client to separate CPUs.
using System.Text.Json;
using System.Text.Json.Serialization;
using Inertia.Net.HostingProbe;

if (args is ["bench", var jitDll, var aotBinary, var output, .. var rest])
{
    await Bench.RunAsync(jitDll, aotBinary, output, rest);
    return;
}

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.ClearProviders();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, ProbeJsonContext.Default));
var inertia = Environment.GetEnvironmentVariable("PROBE_MODE") == "inertia";
if (inertia)
{
    builder.Services.AddInertia(o =>
    {
        o.Version = "1";
        o.Share("appName", "Probe");
        o.Share("auth", new Auth("ada@example.com"));
    });
}

var app = builder.Build();
app.MapGet("/healthz", () => "ok");
if (inertia)
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
