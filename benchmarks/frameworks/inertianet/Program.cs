// The benchmark app on Inertia.Net (benchmarks/frameworks/README.md): the same routes, data and React pages as the other apps, written
// the way the Inertia.Net template writes an app. BENCH_SSR=1 renders first visits on the managed Node process; otherwise the client renders them.
// BENCH_TUNED=1 also turns on what Inertia.Net offers for speed: SSR render caching, two Node renderers, the in-memory cache store and
// stale-while-revalidate prop caches, including a 10 s cache of the todo list.
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using InertiaNetBench;

var tuned = Environment.GetEnvironmentVariable("BENCH_TUNED") == "1";
var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, BenchJsonContext.Default));
builder.Services.AddInertia(o =>
{
    o.RootView = "app.html";
    o.Ssr.BundlePath = "ssr/ssr.js";
    if (builder.Environment.IsDevelopment()) o.Vite.DevServerUrl = "http://localhost:5173"; // `npm run dev` beside `dotnet watch`
    if (Environment.GetEnvironmentVariable("BENCH_SSR") == "1")
    {
        o.Ssr.UseNodeProcess(n => n.Instances = tuned ? 2 : 1); // one renderer per CPU of the container
        if (tuned)
        {
            o.Ssr.Cache.Duration = TimeSpan.FromSeconds(60);
            o.Ssr.Cache.StaleFor = TimeSpan.FromMinutes(5);
        }
        // Required, not Preferred: a render that cannot SSR fails the request instead of quietly serving the cheaper client-rendered page.
        o.Ssr.Mode = SsrMode.Required;
    }
});
if (!tuned) // tuned keeps the default in-memory store: one instance needs no shared cache
{
    builder.Services.AddInertiaRedisCache(o =>
    {
        o.Configuration = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";
        o.KeyPrefix = "bench:";
    });
}
builder.Services.AddValidation();
// CSRF: the Inertia client sends the XSRF-TOKEN cookie back as X-XSRF-TOKEN; UseInertiaAntiforgery() issues and validates it.
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
builder.Services.AddBenchData(Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Username=bench;Password=bench;Database=bench;Maximum Pool Size=10");

var app = builder.Build();
app.UseStaticFiles();
app.UseInertia();
app.UseInertiaAntiforgery();

app.MapGet("/", () => Render("Home", new InertiaProps { ["features"] = Data.Features }));

app.MapGet("/todos", async (int? perPage, int? page, BenchData data, CancellationToken ct) =>
{
    var request = new PageRequest(Math.Clamp(page ?? 1, 1, 400), Math.Clamp(perPage ?? 25, 1, 100));
    if (tuned)
    {
        var rows = Prop(async Task<IReadOnlyList<TodoItem>> (BenchData d, CancellationToken c) => (await d.TodosAsync(request, c)).Data)
            .Cache($"todos:{request.Page}:{request.PerPage}", TimeSpan.FromSeconds(10), staleFor: TimeSpan.FromSeconds(60));
        return Render("Todos", new InertiaProps { ["todos"] = rows, ["page"] = request.Page, ["perPage"] = request.PerPage });
    }

    var todos = await data.TodosAsync(request, ct);
    return Render("Todos", new InertiaProps { ["todos"] = todos.Data, ["page"] = todos.CurrentPage, ["perPage"] = todos.PerPage });
});

app.MapGet("/dashboard", () => Render("Dashboard", new InertiaProps
{
    ["stats"] = Prop<BenchData, Stats>((data, ct) => data.StatsAsync(ct)).Cache("dashboard", TimeSpan.FromSeconds(60), staleFor: tuned ? TimeSpan.FromMinutes(5) : null),
}));

app.MapGet("/compute", (int? n) =>
{
    var count = Math.Clamp(n ?? 20000, 16, 1_000_000);
    return Render("Compute", new InertiaProps { ["n"] = count, ["checksum"] = Compute.Run(count, parallel: false), ["parallel"] = false });
});

app.MapGet("/compute/parallel", (int? n) =>
{
    var count = Math.Clamp(n ?? 20000, 16, 1_000_000);
    return Render("Compute", new InertiaProps { ["n"] = count, ["checksum"] = Compute.Run(count, parallel: true), ["parallel"] = true });
});

app.MapGet("/todos/new", () => Render("Todos/New"));

app.MapPost("/todos", async (NewTodoInput input, BenchData data, CancellationToken ct) =>
{
    await data.AddSubmissionAsync(input.Title!.Trim(), input.Priority!.Value, input.Assignee!, input.DueDate, ct);
    return Results.Redirect("/todos/new");
}).WithInertiaValidation();

app.Run();

namespace InertiaNetBench
{
    public sealed record Feature(string Title, string Body);

    public sealed record TodoItem(int Id, string Title, bool Done, int Priority, string Assignee, DateOnly Due);

    public sealed record AssigneeStats(string Assignee, int Total, int Done, int PrioritySum);

    public sealed record Stats(int Total, int Done, List<AssigneeStats> ByAssignee);

    /// <summary>The body of POST /todos. Must be public: the AddValidation() source generator skips internal types.</summary>
    public sealed record NewTodoInput(string? Title, int? Priority, string? Assignee, string? Due) : IValidatableObject
    {
        private static readonly string[] Assignees = ["ada", "barbara", "dennis", "grace", "linus"];

        internal DateOnly DueDate => DateOnly.ParseExact(Due!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // One rule method rather than attributes: attribute failures would stop IValidatableObject, and every failing field must be reported.
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Title?.Trim().Length is not (>= 3 and <= 120)) yield return new("Enter a title between 3 and 120 characters.", [nameof(Title)]);
            if (Priority is not (>= 0 and <= 4)) yield return new("Choose a priority from 0 to 4.", [nameof(Priority)]);
            if (Assignee is null || Array.IndexOf(Assignees, Assignee) < 0) yield return new("Choose someone from the team.", [nameof(Assignee)]);
            if (!DateOnly.TryParseExact(Due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due) || due.Year != 2026)
                yield return new("Enter a due date in 2026.", [nameof(Due)]);
        }
    }

    internal static class Data
    {
        public static readonly Feature[] Features =
        [
            new("Lists", "Group todos by project, tag and due date."),
            new("Reports", "See what got done this week and what slipped."),
            new("Teams", "Share lists and assign todos to people."),
        ];
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(Feature[]))]
    [JsonSerializable(typeof(TodoItem[]))]
    [JsonSerializable(typeof(List<TodoItem>))]
    [JsonSerializable(typeof(IReadOnlyList<TodoItem>))]
    [JsonSerializable(typeof(Stats))]
    [JsonSerializable(typeof(NewTodoInput))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(string))]
    internal sealed partial class BenchJsonContext : JsonSerializerContext;
}
