using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Tests;

/// <summary><see cref="InertiaProp.Parallel"/>: concurrency, laziness, deterministic output, and failure, rescue and abort rules.</summary>
public class ParallelPropsTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Harness _harness = new();

    private Task<JsonObject> Page(object props, Action<HttpContext>? arrange = null) => _harness.PageAsync(props, arrange);

    // Completes once `count` loaders have started: only reachable when they run at the same time.
    private sealed class Barrier(int count)
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == count)
            {
                _all.SetResult();
            }

            await _all.Task.WaitAsync(Patience);
        }
    }

    [Fact]
    public async Task Parallel_loaders_run_at_the_same_time()
    {
        var barrier = new Barrier(3);
        InertiaProp Load(int value) => Inertia.Prop(async () =>
        {
            await barrier.ArriveAsync();
            return value;
        }).Parallel();

        var page = await Page(new InertiaProps { ["a"] = Load(1), ["b"] = Load(2), ["nested"] = new InertiaProps { ["c"] = Load(3) } });

        JsonAssert.Equal("""{"errors":{},"a":1,"b":2,"nested":{"c":3}}""", page["props"]);
    }

    [Fact]
    public async Task Deferred_group_reload_loads_its_parallel_props_together()
    {
        var barrier = new Barrier(2);
        InertiaProp Load(string value) => Inertia.Defer(async () =>
        {
            await barrier.ArriveAsync();
            return value;
        }).Parallel();

        var page = await Page(new InertiaProps { ["a"] = Load("a"), ["b"] = Load("b"), ["c"] = "c" }, c => c.AsPartial(only: "a,b"));

        JsonAssert.Equal("""{"errors":{},"a":"a","b":"b"}""", page["props"]);
    }

    [Fact]
    public async Task Max_concurrency_bounds_the_running_loaders()
    {
        var harness = new Harness(o => o.Props.MaxConcurrency = 2);
        var running = 0;
        var maxRunning = 0;
        InertiaProp Load(int value) => Inertia.Prop(async ct =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxRunning, now);
            await Task.Delay(30, ct);
            Interlocked.Decrement(ref running);
            return value;
        }).Parallel();

        var page = await harness.PageAsync(new InertiaProps { ["a"] = Load(1), ["b"] = Load(2), ["c"] = Load(3), ["d"] = Load(4), ["e"] = Load(5) });

        JsonAssert.Equal("""{"errors":{},"a":1,"b":2,"c":3,"d":4,"e":5}""", page["props"]);
        Assert.Equal(2, maxRunning);
    }

    [Fact]
    public void Max_concurrency_must_be_positive()
    {
        var harness = new Harness(o => o.Props.MaxConcurrency = 0);
        var ex = Assert.ThrowsAny<Exception>(() => harness.Services.GetService(typeof(InertiaPageWriter)));
        Assert.Contains("Props.MaxConcurrency", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Loaders_left_out_of_the_response_never_start()
    {
        var started = new List<string>();
        InertiaProp Load(string name, Func<Func<CancellationToken, Task<string>>, InertiaProp> make) =>
            make(_ =>
            {
                lock (started) started.Add(name);
                return Task.FromResult(name);
            }).Parallel();

        InertiaProps Props() => new()
        {
            ["optional"] = Load("optional", Inertia.Optional),
            ["deferred"] = Load("deferred", f => Inertia.Defer(f)),
            ["once"] = Load("once", Inertia.Once),
            ["lazy"] = Load("lazy", Inertia.Prop),
            ["nested"] = new InertiaProps { ["optional"] = Load("nested.optional", Inertia.Optional), ["lazy"] = Load("nested.lazy", Inertia.Prop) },
        };

        await Page(Props(), c => c.AsInertia().WithHeader(InertiaHeaders.ExceptOnceProps, "once"));
        Assert.Equal(["lazy", "nested.lazy"], started.Order());

        started.Clear();
        await Page(Props(), c => c.AsPartial(only: "nested.lazy"));
        Assert.Equal(["nested.lazy"], started);

        started.Clear();
        await Page(Props(), c => c.AsPartial(except: "nested,lazy"));
        Assert.Equal(["deferred", "once", "optional"], started.Order());
    }

    [Fact]
    public async Task Output_and_metadata_match_sequential_resolution()
    {
        // Later props finish first, so completion order is the reverse of protocol order.
        InertiaProps Props(bool parallel)
        {
            InertiaProp Mark(InertiaProp prop) => parallel ? prop.Parallel() : prop;
            async Task<T> After<T>(int ms, T value, CancellationToken ct)
            {
                await Task.Delay(ms, ct);
                return value;
            }

            return new InertiaProps
            {
                ["posts"] = Mark(Inertia.Merge(ct => After(60, new[] { 1, 2 }, ct))),
                ["config"] = Mark(Inertia.Once(ct => After(50, "en", ct))),
                ["deep"] = Mark(Inertia.DeepMerge(ct => After(40, new InertiaProps { ["x"] = 1 }, ct))),
                ["later"] = Mark(Inertia.Defer(ct => After(1, 1, ct), "slow")),
                ["unwrapped"] = Mark(Inertia.Prop(ct => After(30, Inertia.Merge(new[] { 3 }), ct))),
                ["nested"] = new InertiaProps
                {
                    ["user"] = Mark(Inertia.Prop(ct => After(20, new User("Ada"), ct))),
                    ["broken"] = Mark(Inertia.Prop<int>(async ct =>
                    {
                        await Task.Delay(10, ct);
                        throw new InvalidOperationException("rescued");
                    }).Rescue()),
                },
                ["plain"] = "value",
            };
        }

        var sequential = await Page(Props(parallel: false), c => c.AsInertia());
        var parallel = await Page(Props(parallel: true), c => c.AsInertia());

        Assert.Equal(sequential.ToJsonString(), parallel.ToJsonString());
        JsonAssert.Equal("""["posts","unwrapped"]""", parallel["mergeProps"]);
        JsonAssert.Equal("""["nested.broken"]""", parallel["rescuedProps"]);
    }

    [Fact]
    public async Task Rescued_parallel_failure_keeps_its_siblings()
    {
        var page = await Page(
            new InertiaProps
            {
                ["broken"] = Inertia.Prop<int>(() => Task.FromException<int>(new InvalidOperationException("boom"))).Rescue().Parallel(),
                ["ok"] = Inertia.Prop(() => Task.FromResult(1)).Parallel(),
            },
            c => c.AsInertia());

        JsonAssert.Equal("""{"errors":{},"ok":1}""", page["props"]);
        JsonAssert.Equal("""["broken"]""", page["rescuedProps"]);
    }

    [Fact]
    public async Task First_failure_in_protocol_order_wins()
    {
        // "b" fails first in time; "a" ignores cancellation and fails afterwards, but comes first.
        var props = new InertiaProps
        {
            ["a"] = Inertia.Prop<int>(async () =>
            {
                await Task.Delay(50);
                throw new InvalidOperationException("a");
            }).Parallel(),
            ["b"] = Inertia.Prop<int>(() => Task.FromException<int>(new InvalidOperationException("b"))).Parallel(),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Page(props));
        Assert.Equal("a", ex.Message);
    }

    [Fact]
    public async Task Cancelled_siblings_do_not_hide_the_failure()
    {
        var props = new InertiaProps
        {
            ["a"] = Inertia.Prop(async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return 1;
            }).Rescue().Parallel(),
            ["b"] = Inertia.Prop<int>(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("b");
            }).Parallel(),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Page(props));
        Assert.Equal("b", ex.Message);
    }

    [Fact]
    public async Task No_loader_outlives_a_failed_render()
    {
        var finished = 0;
        var props = new InertiaProps
        {
            ["inline"] = Inertia.Prop<int>(() => throw new InvalidOperationException("inline")),
            ["cancellable"] = Inertia.Prop(async ct =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return 1;
                }
                finally
                {
                    Interlocked.Increment(ref finished);
                }
            }).Parallel(),
            ["stubborn"] = Inertia.Prop(async () =>
            {
                await Task.Delay(100); // ignores cancellation
                Interlocked.Increment(ref finished);
                return 2;
            }).Parallel(),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Page(props));
        Assert.Equal("inline", ex.Message);
        Assert.Equal(2, Volatile.Read(ref finished));
    }

    [Fact]
    public async Task Abort_propagates_after_every_loader_finished()
    {
        using var cts = new CancellationTokenSource();
        var finished = 0;
        var context = _harness.Context();
        context.RequestAborted = cts.Token;
        InertiaProp Load() => Inertia.Prop(async ct =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                return 1;
            }
            finally
            {
                Interlocked.Increment(ref finished);
            }
        }).Rescue().Parallel();

        var render = Inertia.Render("TestComponent", new InertiaProps { ["a"] = Load(), ["b"] = Load() }).ExecuteAsync(context);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render);
        Assert.Equal(2, Volatile.Read(ref finished));
    }

    [Fact]
    public void Parallel_needs_an_async_loader()
    {
        Assert.Throws<InvalidOperationException>(() => Inertia.Prop(() => 1).Parallel());
        Assert.Throws<InvalidOperationException>(() => Inertia.Always("value").Parallel());
        Assert.Throws<InvalidOperationException>(() => Inertia.Merge(new[] { 1 }).Parallel());
        Assert.True(Inertia.Prop(() => Task.FromResult(1)).Parallel().IsParallel);
        Assert.True(Inertia.Prop(_ => ValueTask.FromResult(1)).Parallel().IsParallel);
    }

    [Fact]
    public async Task A_parallel_prop_with_dot_keys_below_it_starts_early()
    {
        var barrier = new Barrier(2);
        var loads = 0;
        InertiaProps Props() => new()
        {
            ["auth"] = Inertia.Prop(async () =>
            {
                Interlocked.Increment(ref loads);
                await barrier.ArriveAsync();
                return new InertiaProps { ["user"] = "Ada" };
            }).Parallel(),
            ["auth.permissions"] = new[] { "edit" },
            ["b"] = Inertia.Prop(async () =>
            {
                await barrier.ArriveAsync();
                return "b";
            }).Parallel(),
        };

        var page = await Page(Props());
        JsonAssert.Equal("""{"errors":{},"auth":{"user":"Ada","permissions":["edit"]},"b":"b"}""", page["props"]);

        barrier = new Barrier(1);
        var partial = await Page(Props(), c => c.AsPartial(only: "b"));
        JsonAssert.Equal("""{"errors":{},"b":"b"}""", partial["props"]);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task A_prop_instance_at_two_paths_resolves_at_each()
    {
        var calls = 0;
        var shared = Inertia.Prop(() => Task.FromResult(Interlocked.Increment(ref calls))).Parallel();

        var page = await Page(new InertiaProps { ["a"] = shared, ["b"] = shared });

        JsonAssert.Equal("""{"errors":{},"a":1,"b":2}""", page["props"]);
    }

    // Stands in for an EF Core DbContext: its concurrency detector throws when a second operation starts before the first ends.
    private sealed class NotThreadSafeContext
    {
        private int _busy;

        public async Task<int> QueryAsync(int value)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1)
            {
                throw new InvalidOperationException("A second operation was started on this context instance before a previous operation completed.");
            }

            try
            {
                await Task.Delay(20);
                return value;
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }
    }

    [Fact]
    public async Task Props_sharing_a_request_scoped_context_stay_sequential_unless_marked()
    {
        var context = new NotThreadSafeContext();
        var sequential = await Page(new InertiaProps
        {
            ["a"] = Inertia.Prop(() => context.QueryAsync(1)),
            ["b"] = Inertia.Prop(() => context.QueryAsync(2)),
        });
        JsonAssert.Equal("""{"errors":{},"a":1,"b":2}""", sequential["props"]);

        // A new app: here the component's first render without parallel props would delay planning by one render.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new Harness().PageAsync(new InertiaProps
        {
            ["a"] = Inertia.Prop(() => context.QueryAsync(1)).Parallel(),
            ["b"] = Inertia.Prop(() => context.QueryAsync(2)).Parallel(),
        }));
        Assert.StartsWith("A second operation", ex.Message, StringComparison.Ordinal);

        // A context per loader (IDbContextFactory) is safe in parallel.
        var own = await Page(new InertiaProps
        {
            ["a"] = Inertia.Prop(() => new NotThreadSafeContext().QueryAsync(1)).Parallel(),
            ["b"] = Inertia.Prop(() => new NotThreadSafeContext().QueryAsync(2)).Parallel(),
        });
        JsonAssert.Equal("""{"errors":{},"a":1,"b":2}""", own["props"]);
    }

    [Fact]
    public async Task A_component_skips_planning_until_it_meets_a_parallel_prop()
    {
        var writer = _harness.Services.GetRequiredService<InertiaPageWriter>();
        Assert.Null(writer.UsesParallel("TestComponent"));

        await Page(new InertiaProps { ["a"] = 1 });
        Assert.False(writer.UsesParallel("TestComponent"));

        // The first parallel render after that resolves in turn (same output); it marks the component, and later renders start early.
        var running = 0;
        var maxRunning = 0;
        InertiaProps Props() => new()
        {
            ["a"] = Load(1),
            ["b"] = Load(2),
        };
        InertiaProp Load(int value) => Inertia.Prop(async ct =>
        {
            InterlockedMax(ref maxRunning, Interlocked.Increment(ref running));
            await Task.Delay(30, ct);
            Interlocked.Decrement(ref running);
            return value;
        }).Parallel();

        JsonAssert.Equal("""{"errors":{},"a":1,"b":2}""", (await Page(Props()))["props"]);
        Assert.Equal(1, maxRunning);
        Assert.True(writer.UsesParallel("TestComponent"));

        var barrier = new Barrier(2);
        var together = await Page(new InertiaProps
        {
            ["a"] = Inertia.Prop(async () => { await barrier.ArriveAsync(); return 1; }).Parallel(),
            ["b"] = Inertia.Prop(async () => { await barrier.ArriveAsync(); return 2; }).Parallel(),
        });
        JsonAssert.Equal("""{"errors":{},"a":1,"b":2}""", together["props"]);

        // A partial reload that filters the parallel props out does not turn planning off again.
        await Page(new InertiaProps { ["a"] = Load(1), ["c"] = 3 }, c => c.AsPartial(only: "c"));
        Assert.True(writer.UsesParallel("TestComponent"));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }
}
