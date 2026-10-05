using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inertia.Net.Benchmarks;

/// <summary>The services and pages shared by the benchmarks: a production-like setup built once per benchmark class.</summary>
internal static class BenchApp
{
    public const string Component = "Users/Index";

    public static IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new BenchHostEnvironment(CreateContentRoot()));
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, BenchJsonContext.Default));
        services.AddInertia(o =>
        {
            o.Share("appName", "Bench");
            o.Share("auth", ctx => new AuthInfo(ctx.Request.Headers["X-User"].ToString() is { Length: > 0 } user ? user : null));
        });
        return services.BuildServiceProvider();
    }

    /// <summary>A reusable context (like Kestrel's pooled ones): the body goes to <see cref="Stream.Null"/>.</summary>
    public static DefaultHttpContext CreateContext(IServiceProvider services, string path = "/users", params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");
        context.Request.Path = path;
        context.Response.Body = Stream.Null;
        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }

        return context;
    }

    public static string Version(IServiceProvider services) =>
        services.GetRequiredService<VersionProvider>().GetVersion(new DefaultHttpContext());

    private static readonly User[] Users = [.. Enumerable.Range(1, 10).Select(i => new User(i, $"User {i}", $"user{i}@example.com", i % 3 == 0))];
    private static readonly string[] Tags = ["admin", "editor", "viewer", "billing", "support"];
    private static readonly int[] Ids = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private static readonly Stat[] Stats = [new("users", 1200), new("orders", 87), new("revenue", 45210)];

    /// <summary>20 mixed props: literals, nested dictionaries, typed arrays, lazy, always, optional, deferred (2 groups), once, merge.</summary>
    public static InertiaProps Props20() => new()
    {
        ["title"] = "Users",
        ["count"] = 1234,
        ["enabled"] = true,
        ["price"] = 19.99m,
        ["createdAt"] = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        ["tags"] = Tags,
        ["ids"] = Ids,
        ["user"] = new InertiaProps { ["name"] = "Ada", ["email"] = "ada@example.com", ["roles"] = Tags },
        ["users"] = Users,
        ["filters"] = Always(new InertiaProps { ["search"] = "ad", ["sort"] = "name" }),
        ["stats"] = Defer(() => Stats),
        ["activity"] = Defer(ct => ValueTask.FromResult(Ids)),
        ["chart"] = Defer(() => Ids, "charts"),
        ["plans"] = Once(() => Tags),
        ["feed"] = Merge(Users),
        ["lazy"] = Prop(() => "lazy"),
        ["lazyAsync"] = Prop(ct => ValueTask.FromResult(42)),
        ["optional"] = Optional(() => "optional"),
        ["settings"] = new InertiaProps { ["theme"] = "dark", ["locale"] = "en", ["pageSize"] = 25 },
        ["notifications"] = Prop(() => Task.FromResult(3)),
    };

    /// <summary>The page <see cref="Props20"/> renders on a full Inertia visit, as plain values: the cost floor of writing that JSON.</summary>
    public static Dictionary<string, object?> PlainPage() => new()
    {
        ["component"] = Component,
        ["props"] = new Dictionary<string, object?>
        {
            ["errors"] = new Dictionary<string, object?>(),
            ["appName"] = "Bench",
            ["auth"] = new AuthInfo(null),
            ["title"] = "Users",
            ["count"] = 1234,
            ["enabled"] = true,
            ["price"] = 19.99m,
            ["createdAt"] = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            ["tags"] = Tags,
            ["ids"] = Ids,
            ["user"] = new Dictionary<string, object?> { ["name"] = "Ada", ["email"] = "ada@example.com", ["roles"] = Tags },
            ["users"] = Users,
            ["filters"] = new Dictionary<string, object?> { ["search"] = "ad", ["sort"] = "name" },
            ["plans"] = Tags,
            ["feed"] = Users,
            ["lazy"] = "lazy",
            ["lazyAsync"] = 42,
            ["settings"] = new Dictionary<string, object?> { ["theme"] = "dark", ["locale"] = "en", ["pageSize"] = 25 },
            ["notifications"] = 3,
        },
        ["url"] = "/users",
        ["version"] = "56cb9c5afcdb812699a9f832fac3c53f",
        ["sharedProps"] = new[] { "errors", "appName", "auth" },
        ["mergeProps"] = new[] { "feed" },
        ["deferredProps"] = new Dictionary<string, object?> { ["default"] = new[] { "stats", "activity" }, ["charts"] = new[] { "chart" } },
        ["onceProps"] = new Dictionary<string, object?> { ["plans"] = new Dictionary<string, object?> { ["prop"] = "plans", ["expiresAt"] = null } },
    };

    public static UsersPage Poco() => new()
    {
        Title = "Users",
        Count = 1234,
        Users = Users,
        Filters = new Filters("ad", "name"),
        Stats = Defer(() => Stats),
        Plans = Once(() => Tags),
        Feed = Merge(Users),
    };

    private static string CreateContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "inertia-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "wwwroot", "build", ".vite"));
        File.WriteAllText(Path.Combine(root, "app.html"), """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Bench</title>
              @viteReactRefresh
              @vite("src/app.tsx")
              @inertiaHead
            </head>
            <body>
              @inertia
            </body>
            </html>
            """);
        File.WriteAllText(Path.Combine(root, "wwwroot", "build", ".vite", "manifest.json"), """
            {
              "src/app.tsx": { "file": "assets/app-4f2a91c3.js", "src": "src/app.tsx", "isEntry": true, "imports": ["_vendor.js"], "css": ["assets/app-1c9b6e2d.css"] },
              "_vendor.js": { "file": "assets/vendor-8d1e0b7a.js", "css": ["assets/vendor-55aa01ff.css"] }
            }
            """);
        return root;
    }
}

public sealed record User(int Id, string Name, string Email, bool Admin);

public sealed record Stat(string Name, int Value);

public sealed record Filters(string Search, string Sort);

public sealed record AuthInfo(string? User);

public sealed class UsersPage
{
    public required string Title { get; init; }

    public int Count { get; init; }

    public required User[] Users { get; init; }

    public required Filters Filters { get; init; }

    public required InertiaProp Stats { get; init; }

    public required InertiaProp Plans { get; init; }

    public required InertiaProp Feed { get; init; }
}

[JsonSerializable(typeof(User[]))]
[JsonSerializable(typeof(Stat[]))]
[JsonSerializable(typeof(Filters))]
[JsonSerializable(typeof(AuthInfo))]
[JsonSerializable(typeof(UsersPage))]
internal sealed partial class BenchJsonContext : JsonSerializerContext;

internal sealed class BenchHostEnvironment(string contentRoot) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;

    public string ApplicationName { get; set; } = "Bench";

    public string ContentRootPath { get; set; } = contentRoot;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
