using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastEndpointsSvelte;

public sealed record User(int Id, string Name);

public sealed record Stats(int Total);

public sealed record Plan(string Name, int Price);

public sealed record Plans(List<Plan> Items, int ResolvedCount);

public sealed record Post(int Id, string Title);

public sealed record PostPage(List<Post> Data);

public sealed class ContactRequest
{
    public string? Name { get; set; }

    public string? Email { get; set; }
}

public sealed class NewsletterRequest
{
    public string? Email { get; set; }
}

public sealed class DeleteUserRequest
{
    public int Id { get; set; }
}

public sealed class FeedRequest
{
    public int Page { get; set; } = 1;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<User>))]
[JsonSerializable(typeof(Stats))]
[JsonSerializable(typeof(Plans))]
[JsonSerializable(typeof(PostPage))]
[JsonSerializable(typeof(ContactRequest))]
[JsonSerializable(typeof(NewsletterRequest))]
[JsonSerializable(typeof(DeleteUserRequest))]
[JsonSerializable(typeof(FeedRequest))]
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
