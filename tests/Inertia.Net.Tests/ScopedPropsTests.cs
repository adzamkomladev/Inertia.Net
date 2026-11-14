using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

/// <summary><c>Prop&lt;TService, T&gt;</c>: loaders that take a service from the request, or from a scope of their own.</summary>
public sealed class ScopedPropsTests
{
    private sealed class Inbox : IAsyncDisposable
    {
        private static int _next;

        public int Id { get; } = Interlocked.Increment(ref _next);

        public bool Disposed { get; private set; }

        public bool Fail { get; set; }

        public static List<Inbox> Created { get; } = [];

        public Inbox()
        {
            lock (Created) Created.Add(this);
        }

        public Task<int> IdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Fail ? throw new InvalidOperationException("inbox down") : Task.FromResult(Id);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static InertiaProp InboxId() => Prop<Inbox, int>((inbox, ct) => inbox.IdAsync(ct));

    private static Harness NewHarness(TimeProvider? time = null) => new(timeProvider: time, configureServices: s => s.AddScoped<Inbox>());

    private static async Task<(System.Text.Json.Nodes.JsonObject Page, Inbox Requested)> RenderInScopeAsync(Harness harness, InertiaProps props, Action<Microsoft.AspNetCore.Http.HttpContext>? arrange = null)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var context = harness.Context();
        context.RequestServices = scope.ServiceProvider;
        arrange?.Invoke(context);
        var page = Harness.ParsePage(await Harness.ExecuteAsync(context, Render("TestComponent", props)));
        return (page, scope.ServiceProvider.GetRequiredService<Inbox>());
    }

    [Fact]
    public async Task The_loader_gets_the_request_scope_instance()
    {
        var (page, requested) = await RenderInScopeAsync(NewHarness(), new() { ["id"] = InboxId(), ["value"] = Prop<Inbox, int>((inbox, _) => ValueTask.FromResult(inbox.Id)) });
        Assert.Equal(requested.Id, (int?)page["props"]!["id"]);
        Assert.Equal(requested.Id, (int?)page["props"]!["value"]);
    }

    [Fact]
    public async Task Deferred_and_rescued_scoped_props_keep_their_behavior()
    {
        var harness = NewHarness();
        InertiaProps Props() => new() { ["id"] = InboxId().Defer(), ["broken"] = Prop<Inbox, int>((inbox, ct) => { inbox.Fail = true; return inbox.IdAsync(ct); }).Rescue() };

        var (initial, _) = await RenderInScopeAsync(harness, Props());
        Assert.Null(initial["props"]!["id"]);
        var (partial, requested) = await RenderInScopeAsync(harness, Props(), c => c.AsPartial(only: "id,broken"));
        Assert.Equal(requested.Id, (int?)partial["props"]!["id"]);
        Assert.Null(partial["props"]!["broken"]);
        Assert.Equal("broken", (string?)partial["rescuedProps"]![0]);
    }

    [Fact]
    public async Task An_unregistered_service_names_the_prop()
    {
        var harness = new Harness();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.PageAsync(new InertiaProps { ["inbox"] = new InertiaProps { ["id"] = InboxId() } }));
        Assert.Contains("'inbox.id'", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Inbox), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parallel_scoped_loaders_each_get_a_scope_of_their_own()
    {
        var (page, requested) = await RenderInScopeAsync(NewHarness(), new() { ["a"] = InboxId().Parallel(), ["b"] = InboxId().Parallel() });
        int[] ids = [(int)page["props"]!["a"]!, (int)page["props"]!["b"]!];
        Assert.DoesNotContain(requested.Id, ids);
        Assert.NotEqual(ids[0], ids[1]);
        lock (Inbox.Created) Assert.All(Inbox.Created.Where(i => ids.Contains(i.Id)), i => Assert.True(i.Disposed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_refresh_after_the_response_gets_a_new_scope_and_disposes_it(bool fail)
    {
        var time = new FakeTimeProvider();
        var harness = NewHarness(time);
        var failing = false;
        InertiaProp Feed() => Prop<Inbox, int>((inbox, ct) => { inbox.Fail = failing; return inbox.IdAsync(ct); })
            .Cache("feed", TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromMinutes(5));

        var (first, _) = await RenderInScopeAsync(harness, new() { ["feed"] = Feed() });
        time.Advance(TimeSpan.FromMinutes(2));
        failing = fail;
        await using var scope = harness.Services.CreateAsyncScope();
        var context = harness.Context();
        context.RequestServices = scope.ServiceProvider;
        var completion = CompletingResponseFeature.On(context);
        var stale = Harness.ParsePage(await Harness.ExecuteAsync(context, Render("TestComponent", new InertiaProps { ["feed"] = Feed() })));
        Assert.Equal((int?)first["props"]!["feed"], (int?)stale["props"]!["feed"]);
        int before;
        lock (Inbox.Created) before = Inbox.Created.Count;
        await harness.CompleteRefreshAsync(completion);

        Inbox refreshed;
        lock (Inbox.Created) refreshed = Assert.Single(Inbox.Created.Skip(before));
        Assert.True(refreshed.Disposed);
        var (after, _) = await RenderInScopeAsync(harness, new() { ["feed"] = Feed() });
        Assert.Equal(fail ? (int?)first["props"]!["feed"] : refreshed.Id, (int?)after["props"]!["feed"]);
    }

    [Fact]
    public async Task A_live_scoped_prop_lists_its_listeners_and_reloads_from_the_request_scope()
    {
        var harness = NewHarness();
        var changed = new LiveEvent("InboxChanged", LiveChannel.Private("inbox.1"));
        InertiaProps Props() => new() { ["id"] = InboxId().Live(changed), ["other"] = 1 };

        var (initial, first) = await RenderInScopeAsync(harness, Props());
        Assert.Equal(first.Id, (int?)initial["props"]!["id"]);
        Assert.Equal("inbox.1", (string?)initial["liveProps"]!["id"]!["listeners"]![0]!["channel"]!["name"]);
        var (reload, requested) = await RenderInScopeAsync(harness, Props(), c => c.AsPartial(only: "id"));
        Assert.Equal(requested.Id, (int?)reload["props"]!["id"]);
        Assert.Null(reload["props"]!["other"]);
    }
}
