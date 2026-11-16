// The benchmark app on Inertia.Net (benchmarks/frameworks/README.md): the same routes, queries and React pages as the Next.js,
// TanStack Start and Laravel apps. BENCH_SSR=1 renders first visits on the managed Node process; otherwise the client renders them.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using InertiaNetBench;
using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, BenchJsonContext.Default));
builder.Services.AddInertia(o =>
{
    o.RootView = "app.html";
    o.Ssr.BundlePath = "ssr/ssr.js";
    if (builder.Environment.IsDevelopment()) o.Vite.DevServerUrl = "http://localhost:5173"; // `npm run dev` beside `dotnet watch`
    if (Environment.GetEnvironmentVariable("BENCH_SSR") == "1")
    {
        o.Ssr.UseNodeProcess();
        // Required, not Preferred: a render that cannot SSR fails the request instead of quietly serving the cheaper client-rendered page.
        o.Ssr.Mode = SsrMode.Required;
    }
});
builder.Services.AddSingleton(new NpgsqlSlimDataSourceBuilder(Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Username=bench;Password=bench;Database=bench;Maximum Pool Size=10").Build());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379"));

var app = builder.Build();
app.UseStaticFiles();
app.UseInertia();

app.MapGet("/", () => Render("Home", new InertiaProps { ["features"] = Data.Features }));

app.MapGet("/todos", async (int? perPage, int? page, NpgsqlDataSource db) =>
{
    var size = Math.Clamp(perPage ?? 25, 1, 100);
    var number = Math.Clamp(page ?? 1, 1, 400);
    await using var command = db.CreateCommand(
        "SELECT id, title, done, priority, assignee, to_char(due_at, 'YYYY-MM-DD') AS due FROM todos ORDER BY created_at DESC, id DESC LIMIT $1 OFFSET $2");
    command.Parameters.Add(new NpgsqlParameter { Value = size });
    command.Parameters.Add(new NpgsqlParameter { Value = (number - 1) * size });
    var todos = new List<Todo>(size);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        todos.Add(new Todo(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5)));
    }

    return Render("Todos", new InertiaProps { ["todos"] = todos, ["page"] = number, ["perPage"] = size });
});

app.MapGet("/dashboard", async (NpgsqlDataSource db, IConnectionMultiplexer redis) =>
{
    var cache = redis.GetDatabase();
    if (await cache.StringGetAsync("bench:dashboard") is { HasValue: true } cached)
    {
        return Render("Dashboard", new InertiaProps { ["stats"] = JsonSerializer.Deserialize((string)cached!, BenchJsonContext.Default.Stats)! });
    }

    await using var command = db.CreateCommand(
        "SELECT assignee, count(*)::int AS total, (count(*) FILTER (WHERE done))::int AS done, sum(priority)::int AS priority_sum FROM todos GROUP BY assignee ORDER BY assignee");
    var rows = new List<AssigneeStats>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            rows.Add(new AssigneeStats(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
        }
    }

    var stats = new Stats(rows.Sum(r => r.Total), rows.Sum(r => r.Done), rows);
    await cache.StringSetAsync("bench:dashboard", JsonSerializer.Serialize(stats, BenchJsonContext.Default.Stats), TimeSpan.FromSeconds(60));
    return Render("Dashboard", new InertiaProps { ["stats"] = stats });
});

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

app.Run();

namespace InertiaNetBench
{
    public sealed record Feature(string Title, string Body);

    public sealed record Todo(int Id, string Title, bool Done, int Priority, string Assignee, string Due);

    public sealed record AssigneeStats(string Assignee, int Total, int Done, int PrioritySum);

    public sealed record Stats(int Total, int Done, List<AssigneeStats> ByAssignee);

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
    [JsonSerializable(typeof(List<Todo>))]
    [JsonSerializable(typeof(Stats))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(string))]
    internal sealed partial class BenchJsonContext : JsonSerializerContext;
}
