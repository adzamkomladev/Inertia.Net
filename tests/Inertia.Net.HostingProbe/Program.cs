// Hosting cost of Inertia.Net next to a plain Minimal API that serves the same data: process RSS, requests per second and latency.
// `serve` runs one server (PROBE_MODE=plain, inertia, or todo: a small todo app with cookie sign-in and SQLite); `bench` drives published JIT and Native AOT builds of it from a separate
// process over raw keep-alive sockets. eng/hosting-probe.sh publishes both builds and pins server and client to separate CPUs.
// `load <url>` drives one URL of any server and prints a JSON line (benchmarks/frameworks/run.sh).
// PROBE_MODE=ssr serves the React pages in ClientApp: PROBE_SSR=off|node|bun picks client rendering or a managed SSR process,
// PROBE_SSR_CACHE=1 caches the anonymous home page's render, and /__metrics reports GC counters and SSR outcomes.
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
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

if (args is ["load", .. var loadOptions])
{
    await Bench.LoadUrlAsync(loadOptions);
    return;
}

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
if (Environment.GetEnvironmentVariable("PROBE_LOG") != "1") builder.Logging.ClearProviders();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, ProbeJsonContext.Default));
var mode = Environment.GetEnvironmentVariable("PROBE_MODE");
var todo = mode == "todo";
var ssrPages = mode == "ssr";
var inertia = mode == "inertia" || todo || ssrPages;
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
        else if (ssrPages)
        {
            o.RootView = "ssr.html";
            var runtime = Environment.GetEnvironmentVariable("PROBE_SSR") ?? "off";
            if (runtime is "node" or "bun")
            {
                o.Ssr.UseNodeProcess(n =>
                {
                    n.Executable = runtime;
                    n.BasePort = int.Parse(Environment.GetEnvironmentVariable("PROBE_SSR_PORT") ?? "13714", System.Globalization.CultureInfo.InvariantCulture);
                });
                // Required, not Preferred: a render that cannot SSR (e.g. a full render queue) fails the request instead of quietly
                // serving a cheaper client-rendered shell that would flatter the numbers.
                o.Ssr.Mode = SsrMode.Required;
            }
            else if (runtime != "off")
            {
                throw new InvalidOperationException($"PROBE_SSR must be off, node or bun, not '{runtime}'.");
            }
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
else if (ssrPages)
{
    app.UseInertia();
    var outcomes = SsrOutcomes.Listen();
    var cache = Environment.GetEnvironmentVariable("PROBE_SSR_CACHE") == "1";
    app.MapGet("/__metrics", () => new ProbeMetrics(GC.GetTotalAllocatedBytes(), GC.GetTotalPauseDuration().TotalMilliseconds,
        GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), new Dictionary<string, long>(outcomes)));
    // The same JSON for every visitor, so it is the one page the SSR cache (keyed by the page JSON) can share between requests.
    app.MapGet("/", () => cache
        ? Render("Home", new InertiaProps { ["features"] = Data.Features() }).WithSsrCache(TimeSpan.FromHours(1))
        : Render("Home", new InertiaProps { ["features"] = Data.Features() }));
    app.MapGet("/todos", (int? perPage) => Render("Todos/Index", new InertiaProps
    {
        ["auth"] = new Auth("ada@example.com"),
        ["todos"] = Data.Todos(Math.Clamp(perPage ?? 25, 1, 100)),
        ["pagination"] = new PageInfo(1, Math.Clamp(perPage ?? 25, 1, 100), 1000),
        ["filters"] = new Filters("all", ""),
    }));
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

    public sealed record TodoRow(int Id, string Title, string Note, bool Done, string[] Tags, string Assignee, DateTime DueAt, int Priority, int Progress);

    /// <summary>One <c>load</c> run: latency in ms, server CPU µs per request and peak memory in MB (0 without --cgroup).</summary>
    public sealed record LoadReport(double Rps, double P50Ms, double P95Ms, double P99Ms, double P999Ms, long Errors, double CpuMicrosPerRequest, double PeakMb);

    public sealed record PageInfo(int Page, int PerPage, int Total);

    public sealed record Filters(string Status, string Search);

    public sealed record Feature(string Title, string Body);

    /// <summary>The /__metrics answer: GC counters since startup, and inertia.ssr.outcomes counts by "outcome/reason".</summary>
    public sealed record ProbeMetrics(long AllocatedBytes, double GcPauseMs, int Gen0, int Gen1, int Gen2, Dictionary<string, long> SsrOutcomes);

    internal static class SsrOutcomes
    {
        // Counts every inertia.ssr.outcomes measurement, so the bench can tell a server-rendered response from a fallback.
        // Held for the life of the process: disposing it would stop the counts.
        private static MeterListener? s_listener;

        public static ConcurrentDictionary<string, long> Listen()
        {
            var counts = new ConcurrentDictionary<string, long>();
            var listener = s_listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == InertiaDiagnostics.MeterName && instrument.Name == "inertia.ssr.outcomes") l.EnableMeasurementEvents(instrument);
                },
            };
            listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                string? outcome = null, reason = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "inertia.ssr.outcome") outcome = tag.Value as string;
                    else if (tag.Key == "inertia.ssr.reason") reason = tag.Value as string;
                }

                counts.AddOrUpdate($"{outcome}/{reason}", value, (_, total) => total + value);
            });
            listener.Start();
            return counts;
        }
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(User[]))]
    [JsonSerializable(typeof(Auth))]
    [JsonSerializable(typeof(UsersPage))]
    [JsonSerializable(typeof(Todo[]))]
    [JsonSerializable(typeof(TodoRow[]))]
    [JsonSerializable(typeof(PageInfo))]
    [JsonSerializable(typeof(Filters))]
    [JsonSerializable(typeof(Feature[]))]
    [JsonSerializable(typeof(ProbeMetrics))]
    [JsonSerializable(typeof(LoadReport))]
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

        private static readonly string[] TagNames = ["home", "work", "errand", "urgent", "later", "team"];
        private static readonly string[] People = ["ada", "grace", "linus", "barbara", "dennis"];

        public static TodoRow[] Todos(int count)
        {
            var todos = new TodoRow[count];
            for (var i = 0; i < todos.Length; i++)
            {
                todos[i] = new TodoRow(i + 1, "Todo " + (i + 1), $"Notes for todo {i + 1}: check the details before the due date.", i % 3 == 0,
                    [TagNames[i % 6], TagNames[(i + 1) % 6], TagNames[(i + 2) % 6]], People[i % 5], Created.AddDays(i), i % 3, i * 7 % 101);
            }

            return todos;
        }

        public static Feature[] Features() =>
        [
            new("Lists", "Group todos by project, tag and due date."),
            new("Reports", "See what got done this week and what slipped."),
            new("Teams", "Share lists and assign todos to people."),
        ];
    }
}
