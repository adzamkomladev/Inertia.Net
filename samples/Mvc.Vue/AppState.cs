using System.ComponentModel.DataAnnotations;

namespace Mvc.Vue;

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
