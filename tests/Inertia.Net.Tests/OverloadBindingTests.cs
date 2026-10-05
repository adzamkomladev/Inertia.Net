using Inertia.Net;
using static Inertia.Net.Inertia;

// Deliberately outside the Inertia.Net namespace: this is how apps call the factories (the package adds these usings implicitly).
namespace AppStyle.Tests;

/// <summary>
/// Compile coverage for the loader overloads: every call shape must compile and bind to an overload that awaits the task,
/// never to <c>Func&lt;T&gt;</c> with the task itself as T.
/// </summary>
public class OverloadBindingTests
{
    private static Task<int> SomeTaskMethod() => Task.FromResult(42);

    private static ValueTask<int> GetValueTaskMethod(CancellationToken cancellationToken) => ValueTask.FromResult(42);

    private static ValueTask<int> GetValueTaskNoCt() => ValueTask.FromResult(42);

    private static Task<int> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(42);

    [Fact]
    public async Task Every_loader_shape_binds_to_an_awaiting_overload()
    {
        var x = Task.FromResult(42);
        InertiaProp[] props =
        [
            Prop(ct => GetAsync(ct)), Prop(async ct => await x), Prop(() => SomeTaskMethod()), Prop(() => 42), Prop(GetValueTaskMethod), Prop(SomeTaskMethod), Prop(GetAsync), Prop(GetValueTaskNoCt), Prop(async () => await x),
            Optional(ct => GetAsync(ct)), Optional(async ct => await x), Optional(() => SomeTaskMethod()), Optional(() => 42), Optional(GetValueTaskMethod), Optional(SomeTaskMethod), Optional(GetValueTaskNoCt),
            Defer(ct => GetAsync(ct)), Defer(async ct => await x), Defer(() => SomeTaskMethod()), Defer(() => 42), Defer(GetValueTaskMethod), Defer(SomeTaskMethod), Defer(GetValueTaskNoCt),
            Always(ct => GetAsync(ct)), Always(async ct => await x), Always(() => SomeTaskMethod()), Always(() => 42), Always(GetValueTaskMethod), Always(SomeTaskMethod), Always(GetValueTaskNoCt),
            Merge(ct => GetAsync(ct)), Merge(async ct => await x), Merge(() => SomeTaskMethod()), Merge(() => 42), Merge(GetValueTaskMethod), Merge(SomeTaskMethod), Merge(GetValueTaskNoCt),
            DeepMerge(ct => GetAsync(ct)), DeepMerge(async ct => await x), DeepMerge(() => SomeTaskMethod()), DeepMerge(() => 42), DeepMerge(GetValueTaskMethod), DeepMerge(SomeTaskMethod), DeepMerge(GetValueTaskNoCt),
            Once(ct => GetAsync(ct)), Once(async ct => await x), Once(() => SomeTaskMethod()), Once(() => 42), Once(GetValueTaskMethod), Once(SomeTaskMethod), Once(GetValueTaskNoCt),
            Scroll(ct => GetAsync(ct)), Scroll(async ct => await x), Scroll(() => SomeTaskMethod()), Scroll(() => 42), Scroll(GetValueTaskMethod), Scroll(SomeTaskMethod), Scroll(GetValueTaskNoCt),
            Defer(async () => { await Task.Yield(); return 42; }, "group"),
            Defer(ct => new ValueTask<int>(42), "group"),
            Prop<int>(() => SomeTaskMethod()),
        ];

        for (var i = 0; i < props.Length; i++)
        {
            Assert.True(props[i].ValueType == typeof(int), $"props[{i}] bound with T = {props[i].ValueType}");
            Assert.Equal(42, await props[i].ResolveAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public void Value_factories_still_take_values()
    {
        Assert.Equal(typeof(int[]), Merge(new[] { 1 }).ValueType);
        Assert.Equal(typeof(int[]), DeepMerge(new[] { 1 }).ValueType);
        Assert.Equal(typeof(int[]), Scroll(new[] { 1 }, metadata: ScrollMetadata.FromPage(1, false)).ValueType);
        Assert.Equal(typeof(object), Always(1).ValueType);
        Assert.IsType<InertiaResult>(Render("Page"));
        Assert.IsType<InertiaLocationResult>(Location("/"));
        Assert.IsType<InertiaBackResult>(Back());
    }

    [Fact]
    public async Task Loaders_receive_the_cancellation_token()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        await Defer(ct => { seen = ct; return Task.FromResult(1); }).ResolveAsync(cts.Token);
        Assert.Equal(cts.Token, seen);
    }
}
