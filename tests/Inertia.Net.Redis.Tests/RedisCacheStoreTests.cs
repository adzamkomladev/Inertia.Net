using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Redis.Tests;

/// <summary>
/// The Redis store against a real server: set <c>REDIS_URL</c> (e.g. <c>localhost:6379</c>); without it the tests are skipped.
/// Each test uses a key prefix of its own.
/// </summary>
public sealed class RedisCacheStoreTests
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("REDIS_URL");
    private readonly string _prefix = "inertia-test:" + Guid.NewGuid().ToString("N") + ":";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider App(string? configuration = null, string? prefix = null)
    {
        Assert.SkipWhen(Url is null, "REDIS_URL is not set");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddInertia();
        services.AddInertiaRedisCache(o =>
        {
            o.Configuration = configuration ?? Url;
            o.KeyPrefix = prefix ?? _prefix;
        });
        return services.BuildServiceProvider();
    }

    private static IInertiaCacheStore Store(IServiceProvider app) => app.GetRequiredService<IInertiaCacheStore>();

    private static InertiaCacheKey Key(string key) => new(InertiaCacheEntryKind.Prop, key);

    private static InertiaCacheEntryOptions Entry(TimeSpan? ttl = null, params string[] tags) => new(ttl ?? TimeSpan.FromMinutes(1), tags);

    [Fact]
    public async Task Values_are_stored_replaced_removed_and_expire()
    {
        await using var app = App();
        var store = Store(app);
        Assert.Equal("RedisInertiaCacheStore", store.GetType().Name);

        Assert.Null(await store.GetAsync(Key("a"), Ct));
        await store.SetAsync(Key("a"), [1, 2], Entry(), Ct);
        await store.SetAsync(Key("a"), [3], Entry(), Ct);
        Assert.Equal([3], await store.GetAsync(Key("a"), Ct));
        await store.RemoveAsync(Key("a"), Ct);
        Assert.Null(await store.GetAsync(Key("a"), Ct));

        await store.SetAsync(Key("short"), [1], Entry(TimeSpan.FromMilliseconds(100)), Ct);
        await store.SetAsync(Key("none"), [1], Entry(TimeSpan.Zero), Ct);
        Assert.Null(await store.GetAsync(Key("none"), Ct));
        await Task.Delay(300, Ct);
        Assert.Null(await store.GetAsync(Key("short"), Ct));
    }

    [Fact]
    public async Task Tags_remove_their_entries_and_a_replaced_entry_leaves_its_old_tags()
    {
        await using var app = App();
        var store = Store(app);
        await store.SetAsync(Key("a"), [1], Entry(null, "user:1", InertiaCacheTags.Props), Ct);
        await store.SetAsync(Key("b"), [2], Entry(null, "user:2", InertiaCacheTags.Props), Ct);
        await store.SetAsync(new(InertiaCacheEntryKind.Ssr, "h"), [3], Entry(null, InertiaCacheTags.Ssr), Ct);
        await store.SetAsync(Key("a"), [4], Entry(null, "user:3"), Ct); // no longer tagged user:1

        await store.RemoveByTagAsync("user:1", Ct);
        Assert.Equal([4], await store.GetAsync(Key("a"), Ct));

        await store.RemoveByTagAsync(InertiaCacheTags.Props, Ct);
        Assert.Equal([4], await store.GetAsync(Key("a"), Ct));
        Assert.Null(await store.GetAsync(Key("b"), Ct));
        Assert.Equal([3], await store.GetAsync(new(InertiaCacheEntryKind.Ssr, "h"), Ct));

        await store.RemoveByTagAsync("user:3", Ct);
        await store.RemoveByTagAsync(InertiaCacheTags.Ssr, Ct);
        Assert.Null(await store.GetAsync(Key("a"), Ct));
        Assert.Null(await store.GetAsync(new(InertiaCacheEntryKind.Ssr, "h"), Ct));

        // Nothing is left behind: entries, tag indexes and tag sets are all gone.
        using var redis = await ConnectionMultiplexer.ConnectAsync(Url!);
        Assert.Empty(redis.GetServers()[0].Keys(pattern: _prefix + "*"));
    }

    [Fact]
    public async Task Removing_a_former_tag_after_expiry_leaves_the_retagged_entry()
    {
        await using var app = App();
        var store = Store(app);
        // The sibling keeps the "old" tag set alive after the short entry and its index expire.
        await store.SetAsync(Key("sibling"), [9], Entry(TimeSpan.FromMinutes(1), "old"), Ct);
        await store.SetAsync(Key("k"), [1], Entry(TimeSpan.FromMilliseconds(100), "old"), Ct);
        await Task.Delay(300, Ct);
        Assert.Null(await store.GetAsync(Key("k"), Ct));

        await store.SetAsync(Key("k"), [2], Entry(TimeSpan.FromMinutes(1), "new"), Ct);
        await store.RemoveByTagAsync("old", Ct);
        Assert.Equal([2], await store.GetAsync(Key("k"), Ct));
        Assert.Null(await store.GetAsync(Key("sibling"), Ct));

        await store.RemoveByTagAsync("new", Ct);
        Assert.Null(await store.GetAsync(Key("k"), Ct));
    }

    [Fact]
    public async Task Removing_a_tag_with_more_entries_than_one_batch_removes_them_all()
    {
        await using var app = App();
        var store = Store(app);
        for (var i = 0; i < 600; i++)
        {
            await store.SetAsync(Key($"k{i}"), [1], Entry(null, "bulk"), Ct);
        }

        await store.RemoveByTagAsync("bulk", Ct);
        using var redis = await ConnectionMultiplexer.ConnectAsync(Url!);
        Assert.Empty(redis.GetServers()[0].Keys(pattern: _prefix + "*"));
    }

    [Fact]
    public async Task Repeated_tags_are_indexed_once_and_a_single_tag_needs_no_deduplication()
    {
        await using var app = App();
        var store = Store(app);
        await store.SetAsync(Key("one"), [1], Entry(null, InertiaCacheTags.Props), Ct);
        await store.SetAsync(Key("many"), [2], Entry(null, "user:1", "user:1", InertiaCacheTags.Props), Ct);

        using var redis = await ConnectionMultiplexer.ConnectAsync(Url!);
        var db = redis.GetDatabase();
        Assert.Equal(1, await db.SetLengthAsync(_prefix + "tags:" + Key("one")));
        Assert.Equal(2, await db.SetLengthAsync(_prefix + "tags:" + Key("many")));

        await store.RemoveByTagAsync("user:1", Ct);
        Assert.Null(await store.GetAsync(Key("many"), Ct));
        Assert.Equal([1], await store.GetAsync(Key("one"), Ct));
    }

    [Fact]
    public async Task Tag_sets_shed_the_members_of_expired_entries_as_new_entries_are_written()
    {
        const int Expired = 1000;
        await using var app = App();
        var store = Store(app);
        await store.SetAsync(Key("live"), [1], Entry(null, InertiaCacheTags.Props), Ct); // keeps the tag set alive
        for (var i = 0; i < Expired; i++)
        {
            await store.SetAsync(Key($"k{i}"), [1], Entry(TimeSpan.FromMilliseconds(200), InertiaCacheTags.Props), Ct);
        }

        await Task.Delay(500, Ct);
        await store.SetAsync(Key("live"), [1], Entry(null, "other"), Ct); // a set without the tag leaves it alone
        for (var i = 0; i < Expired; i++)
        {
            await store.SetAsync(Key("live"), [1], Entry(null, InertiaCacheTags.Props), Ct);
        }

        using var redis = await ConnectionMultiplexer.ConnectAsync(Url!);
        var members = await redis.GetDatabase().SetMembersAsync(_prefix + "tag:" + InertiaCacheTags.Props);
        Assert.InRange(members.Length, 1, Expired / 10);
        Assert.Contains(members, m => m == Key("live").ToString());

        // The live entry is still invalidated by its tag.
        await store.RemoveByTagAsync(InertiaCacheTags.Props, Ct);
        Assert.Null(await store.GetAsync(Key("live"), Ct));
    }

    [Fact]
    public async Task A_set_racing_a_tag_removal_is_either_removed_or_kept_whole()
    {
        await using var app = App();
        var store = Store(app);
        for (var round = 0; round < 50; round++)
        {
            await store.SetAsync(Key("race"), [1], Entry(null, "t"), Ct);
            await Task.WhenAll(store.RemoveByTagAsync("t", Ct).AsTask(), store.SetAsync(Key("race"), [2], Entry(null, "t"), Ct).AsTask());
            var value = await store.GetAsync(Key("race"), Ct);
            if (value is not null)
            {
                // Kept: it is still tagged, so the next removal finds it.
                Assert.Equal([2], value);
                await store.RemoveByTagAsync("t", Ct);
                Assert.Null(await store.GetAsync(Key("race"), Ct));
            }
        }
    }

    [Fact]
    public async Task Key_prefixes_keep_apps_apart()
    {
        await using var one = App();
        await using var two = App(prefix: _prefix + "other:");
        await Store(one).SetAsync(Key("a"), [1], Entry(null, "t"), Ct);
        Assert.Null(await Store(two).GetAsync(Key("a"), Ct));
        await Store(two).RemoveByTagAsync("t", Ct);
        Assert.Equal([1], await Store(one).GetAsync(Key("a"), Ct));
    }

    [Fact]
    public async Task Non_ascii_and_long_keys_and_tags_work()
    {
        await using var app = App();
        var store = Store(app);
        var key = Key("ключ:日本:" + new string('x', 4000));
        await store.SetAsync(key, [1], Entry(null, "тег:é"), Ct);
        Assert.Equal([1], await store.GetAsync(key, Ct));
        await store.RemoveByTagAsync("тег:é", Ct);
        Assert.Null(await store.GetAsync(key, Ct));
    }

    [Fact]
    public async Task Large_values_round_trip()
    {
        await using var app = App();
        var value = new byte[5 * 1024 * 1024];
        Random.Shared.NextBytes(value);
        await Store(app).SetAsync(Key("big"), value, Entry(), Ct);
        Assert.Equal(value, await Store(app).GetAsync(Key("big"), Ct));
    }

    [Fact]
    public async Task Two_app_instances_share_cached_props_and_invalidation()
    {
        await using var one = App();
        await using var two = App();
        var loads = 0;
        InertiaProps Props() => new() { ["stats"] = Prop(() => ++loads).Cache("stats", TimeSpan.FromMinutes(1), tags: ["stats"]) };

        Assert.Equal(1, (int?)(await RenderAsync(one, Props()))["props"]!["stats"]);
        Assert.Equal(1, (int?)(await RenderAsync(two, Props()))["props"]!["stats"]);
        Assert.Equal(1, loads);

        await two.GetRequiredService<IInertiaCache>().RemoveByTagAsync("stats", Ct);
        Assert.Equal(2, (int?)(await RenderAsync(one, Props()))["props"]!["stats"]);
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_renders_without_the_cache_and_fails_invalidation()
    {
        await using var app = App("127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200,asyncTimeout=200");
        var loads = 0;
        InertiaProps Props() => new() { ["stats"] = Prop(() => ++loads).Cache("stats", TimeSpan.FromMinutes(1)) };

        Assert.Equal(1, (int?)(await RenderAsync(app, Props()))["props"]!["stats"]);
        Assert.Equal(2, (int?)(await RenderAsync(app, Props()))["props"]!["stats"]);
        await Assert.ThrowsAnyAsync<Exception>(async () => await app.GetRequiredService<IInertiaCache>().RemoveByTagAsync("stats", Ct));
    }

    private static async Task<JsonObject> RenderAsync(IServiceProvider app, InertiaProps props)
    {
        await using var scope = app.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "GET";
        context.Request.Path = "/";
        context.Request.Headers[InertiaHeaders.Inertia] = "true";
        context.Response.Body = new MemoryStream();
        await Render("Test", props).ExecuteAsync(context);
        return JsonNode.Parse(Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()))!.AsObject();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
