using System.Text.Json.Nodes;

namespace Inertia.Net.Testing.Tests;

public class AssertionTests
{
    private const string PageJson = """
        {"component":"Users/Index","url":"/users?page=1","version":"v1",
         "props":{"errors":{},"users":[{"id":1,"name":"Ann","email":"a@x"},{"id":2,"name":"Bob","email":"b@x"}],
                  "title":"Hi","n":5,"ratio":1.5,"ok":true,"nothing":null,"filters":{"search":"a"},"tags":["x","y"]},
         "flash":{"toast":"Saved","nested":{"a":1}},
         "deferredProps":{"sidebar":["stats"],"default":["plans"]}}
        """;

    private static AssertableInertia Make() =>
        new(ParsingTests.JsonResponse(PageJson).GetInertiaPageAsync().GetAwaiter().GetResult());

    private static void Passes(Action<AssertableInertia> a) => a(Make());

    private static void Fails(string message, Action<AssertableInertia> a) =>
        Assert.Equal(message, Assert.Throws<InertiaAssertionException>(() => a(Make())).Message);

    [Fact]
    public void Component_url_version()
    {
        Passes(p => p.Component("Users/Index").Url("/users?page=1").Version("v1"));
        Fails("Unexpected Inertia page component: expected [X] but was [Users/Index].", p => p.Component("X"));
        Fails("Unexpected Inertia page url: expected [/] but was [/users?page=1].", p => p.Url("/"));
        Fails("Unexpected Inertia asset version: expected [v2] but was [v1].", p => p.Version("v2"));
    }

    [Fact]
    public void Has_variants()
    {
        Passes(p => p.Has("users").Has("users.0.name").Has("nothing").Has("users", 2).HasAll("title", "n").HasAny("zzz", "n"));
        Fails("Property [users.0.nope] does not exist.", p => p.Has("users.0.nope"));
        Fails("Property [users.5.name] does not exist.", p => p.Has("users.5.name"));
        Fails("Property [users] does not have the expected size: expected 3 but was 2.", p => p.Has("users", 3));
        Fails("Property [zzz] does not exist.", p => p.HasAll("title", "zzz"));
        Fails("None of properties [a, b] exist.", p => p.HasAny("a", "b"));
    }

    [Fact]
    public void Has_with_scope_and_first_item_scope()
    {
        Passes(p => p.Has("filters", f => f.Where("search", "a")));
        Passes(p => p.Has("users", 2, u => u.Where("name", "Ann").Etc()));
        Passes(p => p.Has("users.1", u => u.Has("id").Has("name").Has("email")));
        Fails("Property [title] is not scopeable.", p => p.Has("title", _ => { }));
        Fails("Cannot scope directly onto the first element of property [e] because it is empty.",
            p => new AssertableInertia(Empty()).Has("e", 0, _ => { }));
    }

    private static InertiaPage Empty() =>
        ParsingTests.JsonResponse("""{"component":"A","props":{"e":[]},"url":"/"}""").GetInertiaPageAsync().GetAwaiter().GetResult();

    [Fact]
    public void Missing_assertions()
    {
        Passes(p => p.Missing("zzz").MissingAll("a", "b").Missing("users.9"));
        Fails("Property [title] was found while it was expected to be missing.", p => p.Missing("title"));
        Fails("Property [n] was found while it was expected to be missing.", p => p.MissingAll("a", "n"));
    }

    [Fact]
    public void Where_assertions()
    {
        Passes(p => p.Where("title", "Hi").Where("n", 5).Where("ratio", 1.5).Where("ok", true).Where("nothing", (object?)null)
            .Where("filters", new { search = "a" }).Where("tags", JsonNode.Parse("[\"x\",\"y\"]")).Where("users.0.id", 1));
        Passes(p => p.Where("title", v => (string?)v == "Hi").WhereNot("title", "Nope").WhereNot("n", 6));
        Fails("Property [n] expected to be 1 but was 5.", p => p.Where("n", 1));
        Fails("Property [title] expected to be \"x\" but was \"Hi\".", p => p.Where("title", "x"));
        Fails("Property [nothing] expected to be 1 but was null.", p => p.Where("nothing", 1));
        Fails("Property [title] was marked as invalid using a closure.", p => p.Where("title", v => v is null));
        Fails("Property [title] contains a value that should be missing: \"Hi\".", p => p.WhereNot("title", "Hi"));
        Fails("Property [zzz] does not exist.", p => p.Where("zzz", 1));
    }

    [Fact]
    public void Null_assertions()
    {
        Passes(p => p.WhereNull("nothing").WhereNotNull("title"));
        Fails("Property [title] should be null but was \"Hi\".", p => p.WhereNull("title"));
        Fails("Property [nothing] should not be null.", p => p.WhereNotNull("nothing"));
        Passes(p => p.Where("nothing", (Func<JsonNode?, bool>)null!));
    }

    [Fact]
    public void Type_assertions()
    {
        Passes(p => p.WhereType("title", "string").WhereType("n", "integer").WhereType("ratio", "double").WhereType("n", "number")
            .WhereType("ratio", "number").WhereType("ok", "boolean").WhereType("tags", "array").WhereType("filters", "object")
            .WhereType("nothing", "string|null").WhereAllType(new Dictionary<string, string> { ["title"] = "string", ["n"] = "integer" }));
        Fails("Property [n] is not of expected type [string|null]; got [integer].", p => p.WhereType("n", "string|null"));
        Fails("Property [ratio] is not of expected type [integer]; got [double].", p => p.WhereType("ratio", "integer"));
        Fails("Property [title] is not of expected type [number]; got [string].", p => p.WhereType("title", "number"));
        Fails("Property [tags] is not of expected type [object]; got [array].",
            p => p.WhereAllType(new Dictionary<string, string> { ["title"] = "string", ["tags"] = "object" }));
    }

    [Fact]
    public void Contains_assertions()
    {
        Passes(p => p.WhereContains("tags", "x").WhereContains("tags", "y", "x").WhereContains("users", new { id = 1, name = "Ann", email = "a@x" }));
        Fails("Property [tags] does not contain [\"z\", \"w\"].", p => p.WhereContains("tags", "x", "z", "w"));
        Fails("Property [title] is not an array.", p => p.WhereContains("title", "H"));
    }

    [Fact]
    public void Count_assertions()
    {
        Passes(p => p.Count("users", 2).Count("filters", 1).CountBetween("users", 1, 3).CountBetween("tags", 2, 2));
        Fails("Property [users] does not have the expected size: expected 1 but was 2.", p => p.Count("users", 1));
        Fails("Property [users] size is not between 3 and 4: it was 2.", p => p.CountBetween("users", 3, 4));
        Fails("Property [title] is not countable.", p => p.Count("title", 1));
    }

    [Fact]
    public void Each_and_first()
    {
        Passes(p => p.Has("users", u => u.Each(i => i.Has("id").Has("name").Has("email"))));
        Passes(p => p.Has("users", u => u.First(i => i.Where("name", "Ann").Etc()).Etc()));
        Fails("Unexpected properties were found in scope [users.0]: [email].",
            p => p.Has("users", u => u.Each(i => i.Has("id").Has("name"))));
        Fails("Cannot scope directly onto each element of property [e] because it is empty.",
            _ => new AssertableInertia(Empty()).Has("e", e => e.Each(_ => { })));
        Fails("Cannot scope directly onto the first element of property [e] because it is empty.",
            _ => new AssertableInertia(Empty()).Has("e", e => e.First(_ => { })));
    }

    [Fact]
    public void Etc_tracking_in_nested_scopes()
    {
        Passes(p => p.Has("filters", f => f.Has("search")));
        Passes(p => p.Has("users", u => u.Each(i => i.Etc()).Etc()));
        Fails("Unexpected properties were found in scope [users]: [1].", p => p.Has("users", u => u.First(i => i.Etc())));
        Fails("Unexpected properties were found in scope [users.0]: [id, name, email].", p => p.Has("users", 2, u => { }));
        Fails("Unexpected properties were found in scope [filters]: [search].", p => p.Has("filters", f => { }));
        Fails("Unexpected properties were found in scope [users.0]: [name, email].", p => p.Has("users.0", u => u.Where("id", 1)));
    }

    [Fact]
    public void Root_scope_is_checked_only_on_demand()
    {
        Passes(p => p.Has("title"));
        Fails("Unexpected properties were found on the root level: [errors, users, n, ratio, ok, nothing, filters, tags].",
            p => p.Has("title").Interacted());
        Passes(p => p.Has("title").Etc().Interacted());
    }

    [Fact]
    public void Flash_assertions()
    {
        Passes(p => p.HasFlash("toast").HasFlash("toast", "Saved").HasFlash("nested.a", 1).MissingFlash("zzz"));
        Fails("Inertia Flash Data is missing key [zzz].", p => p.HasFlash("zzz"));
        Fails("Inertia Flash Data is missing key [zzz].", p => p.HasFlash("zzz", 1));
        Fails("Inertia Flash Data [toast] expected to be \"Other\" but was \"Saved\".", p => p.HasFlash("toast", "Other"));
        Fails("Inertia Flash Data has unexpected key [toast].", p => p.MissingFlash("toast"));
    }

    [Fact]
    public void Deferred_assertions()
    {
        Passes(p => p.HasDeferred("stats").HasDeferred("sidebar", "stats").HasDeferred("default", "plans"));
        Fails("Deferred prop [nope] was not found in any group.", p => p.HasDeferred("nope"));
        Fails("Deferred prop [stats] was not found in group [default].", p => p.HasDeferred("default", "stats"));
        Fails("Deferred prop [stats] was not found in group [zzz].", p => p.HasDeferred("zzz", "stats"));
    }

    [Fact]
    public void Dump_returns_scope_json()
    {
        Assert.Contains("\"search\": \"a\"", Make().Dump());
        string? scoped = null;
        Passes(p => p.Has("filters", f => { scoped = f.Dump(); f.Etc(); }));
        Assert.Contains("search", scoped);
    }

    [Fact]
    public async Task AssertInertiaAsync_runs_assertions_and_returns_page()
    {
        using var r = ParsingTests.JsonResponse(PageJson);
        var page = await r.AssertInertiaAsync(p => p.Component("Users/Index").Has("users", 2));
        Assert.Equal("Users/Index", page.Component);

        using var r2 = ParsingTests.JsonResponse(PageJson);
        await Assert.ThrowsAsync<InertiaAssertionException>(() => r2.AssertInertiaAsync(p => p.Component("Nope")));
    }
}
