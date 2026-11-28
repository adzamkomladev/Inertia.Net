using System.ComponentModel.DataAnnotations;

namespace Mvc.Vue;

public sealed record User(int Id, string Name);

public sealed record Stats(int Total);

public sealed record Plan(string Name, int Price);

public sealed record Plans(List<Plan> Items, int ResolvedCount);

public sealed record Post(int Id, string Title);

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

    public static LiveEvent PostCreated { get; } = new("PostCreated", LiveChannel.Public("posts"));
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

/// <summary>In-memory demo state shared by all requests (a sample, not a database).</summary>
internal static class AppState
{
    public const int PageSize = 10, FeedPages = 5;

    private static int _plansCount, _counter, _unread, _livePosts = 15;

    public static List<User> Users { get; } = Seed();

    public static IReadOnlyList<Post> Posts { get; } = Enumerable.Range(1, PageSize * FeedPages)
        .Select(id => new Post(id, $"Post {id}")).ToArray();

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

    /// <summary>The live feed on the Live page, newest first.</summary>
    public static Post[] LivePosts()
    {
        var count = Volatile.Read(ref _livePosts);
        return [.. Enumerable.Range(1, count).Reverse().Select(id => new Post(id, $"Live post {id}"))];
    }

    public static void AddLivePost() => Interlocked.Increment(ref _livePosts);

    public static IResult Reset()
    {
        LoginDemo.Reset();
        Users.Clear();
        Users.AddRange(Seed());
        Version = null;
        Interlocked.Exchange(ref _plansCount, 0);
        Interlocked.Exchange(ref _counter, 0);
        Interlocked.Exchange(ref _unread, 0);
        Interlocked.Exchange(ref _livePosts, 15);
        return Results.NoContent();
    }

    private static List<User> Seed() => [new(1, "Ada Lovelace"), new(2, "Grace Hopper"), new(3, "Alan Turing")];
}
