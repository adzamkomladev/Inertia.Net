using System.Runtime.CompilerServices;

namespace Inertia.Net;

/// <summary>
/// Factories for Inertia props and results. Import with <c>using static Inertia.Net.Inertia;</c> (added implicitly by the
/// package when <c>ImplicitUsings</c> is enabled) and call <c>Render(...)</c>, <c>Defer(...)</c>, <c>Back()</c> unqualified.
/// </summary>
/// <remarks>
/// Every lazy factory accepts sync (<c>() =&gt; value</c>), <see cref="Task{TResult}"/> and <see cref="ValueTask{TResult}"/> loaders,
/// with or without a <see cref="CancellationToken"/>. <see cref="OverloadResolutionPriorityAttribute"/> makes a loader that returns a
/// task bind to the awaiting overload, never to the sync one with the task as the value.
/// </remarks>
public static class Inertia
{
    // Overload priorities: a CancellationToken-taking loader wins over a parameterless one (method groups with both), a Task
    // loader over a ValueTask one (async lambdas match both), and any task loader over Func<T> (where T would be the task).
    private const int CtTask = 4, CtValueTask = 3, NoCtTask = 2, NoCtValueTask = 1;

    /// <summary>A lazy prop: the loader only runs when the prop is part of the response.</summary>
    [OverloadResolutionPriority(CtValueTask)]
    public static InertiaProp Prop<T>(Func<CancellationToken, ValueTask<T>> loader) => InertiaProp<T>.FromLoader(loader);

    /// <inheritdoc cref="Prop{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(CtTask)]
    public static InertiaProp Prop<T>(Func<CancellationToken, Task<T>> loader) => InertiaProp<T>.FromLoader(Wrap(loader));

    /// <inheritdoc cref="Prop{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtTask)]
    public static InertiaProp Prop<T>(Func<Task<T>> loader) => InertiaProp<T>.FromLoader(Wrap(loader));

    /// <inheritdoc cref="Prop{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtValueTask)]
    public static InertiaProp Prop<T>(Func<ValueTask<T>> loader) => InertiaProp<T>.FromLoader(Wrap(loader));

    /// <inheritdoc cref="Prop{T}(Func{CancellationToken, ValueTask{T}})"/>
    public static InertiaProp Prop<T>(Func<T> loader) => InertiaProp<T>.FromLoader(loader);

    /// <summary>A prop that is never sent on the first load, only when a partial reload asks for it.</summary>
    [OverloadResolutionPriority(CtValueTask)]
    public static InertiaProp Optional<T>(Func<CancellationToken, ValueTask<T>> loader) => WithLoad(InertiaProp<T>.FromLoader(loader), PropLoad.Optional);

    /// <inheritdoc cref="Optional{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(CtTask)]
    public static InertiaProp Optional<T>(Func<CancellationToken, Task<T>> loader) => Optional(Wrap(loader));

    /// <inheritdoc cref="Optional{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtTask)]
    public static InertiaProp Optional<T>(Func<Task<T>> loader) => Optional(Wrap(loader));

    /// <inheritdoc cref="Optional{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtValueTask)]
    public static InertiaProp Optional<T>(Func<ValueTask<T>> loader) => Optional(Wrap(loader));

    /// <inheritdoc cref="Optional{T}(Func{CancellationToken, ValueTask{T}})"/>
    public static InertiaProp Optional<T>(Func<T> loader) => WithLoad(InertiaProp<T>.FromLoader(loader), PropLoad.Optional);

    /// <summary>A prop the client loads right after the first render, together with the other props of its group.</summary>
    [OverloadResolutionPriority(CtValueTask)]
    public static InertiaProp Defer<T>(Func<CancellationToken, ValueTask<T>> loader, string group = "default") => InertiaProp<T>.FromLoader(loader).Defer(group);

    /// <inheritdoc cref="Defer{T}(Func{CancellationToken, ValueTask{T}}, string)"/>
    [OverloadResolutionPriority(CtTask)]
    public static InertiaProp Defer<T>(Func<CancellationToken, Task<T>> loader, string group = "default") => Defer(Wrap(loader), group);

    /// <inheritdoc cref="Defer{T}(Func{CancellationToken, ValueTask{T}}, string)"/>
    [OverloadResolutionPriority(NoCtTask)]
    public static InertiaProp Defer<T>(Func<Task<T>> loader, string group = "default") => Defer(Wrap(loader), group);

    /// <inheritdoc cref="Defer{T}(Func{CancellationToken, ValueTask{T}}, string)"/>
    [OverloadResolutionPriority(NoCtValueTask)]
    public static InertiaProp Defer<T>(Func<ValueTask<T>> loader, string group = "default") => Defer(Wrap(loader), group);

    /// <inheritdoc cref="Defer{T}(Func{CancellationToken, ValueTask{T}}, string)"/>
    public static InertiaProp Defer<T>(Func<T> loader, string group = "default") => InertiaProp<T>.FromLoader(loader).Defer(group);

    /// <summary>A prop that is always sent, even when a partial reload does not ask for it.</summary>
    public static InertiaProp Always(object? value) => WithAlways(InertiaProp<object?>.FromValue(value));

    /// <summary>A lazy prop that is always sent, even when a partial reload does not ask for it.</summary>
    [OverloadResolutionPriority(CtValueTask)]
    public static InertiaProp Always<T>(Func<CancellationToken, ValueTask<T>> loader) => WithAlways(InertiaProp<T>.FromLoader(loader));

    /// <inheritdoc cref="Always{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(CtTask)]
    public static InertiaProp Always<T>(Func<CancellationToken, Task<T>> loader) => Always(Wrap(loader));

    /// <inheritdoc cref="Always{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtTask)]
    public static InertiaProp Always<T>(Func<Task<T>> loader) => Always(Wrap(loader));

    /// <inheritdoc cref="Always{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtValueTask)]
    public static InertiaProp Always<T>(Func<ValueTask<T>> loader) => Always(Wrap(loader));

    /// <inheritdoc cref="Always{T}(Func{CancellationToken, ValueTask{T}})"/>
    public static InertiaProp Always<T>(Func<T> loader) => WithAlways(InertiaProp<T>.FromLoader(loader));

    /// <summary>A prop the client appends to its current value instead of replacing it.</summary>
    public static InertiaProp Merge<T>(T value) => InertiaProp<T>.FromValue(value).Append();

    /// <summary>A lazy prop the client appends to its current value instead of replacing it.</summary>
    [OverloadResolutionPriority(CtValueTask)]
    public static InertiaProp Merge<T>(Func<CancellationToken, ValueTask<T>> loader) => InertiaProp<T>.FromLoader(loader).Append();

    /// <inheritdoc cref="Merge{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(CtTask)]
    public static InertiaProp Merge<T>(Func<CancellationToken, Task<T>> loader) => Merge(Wrap(loader));

    /// <inheritdoc cref="Merge{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtTask)]
    public static InertiaProp Merge<T>(Func<Task<T>> loader) => Merge(Wrap(loader));

    /// <inheritdoc cref="Merge{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtValueTask)]
    public static InertiaProp Merge<T>(Func<ValueTask<T>> loader) => Merge(Wrap(loader));

    /// <inheritdoc cref="Merge{T}(Func{CancellationToken, ValueTask{T}})"/>
    public static InertiaProp Merge<T>(Func<T> loader) => InertiaProp<T>.FromLoader(loader).Append();

    /// <summary>A prop the client deep merges into its current value.</summary>
    public static InertiaProp DeepMerge<T>(T value) => InertiaProp<T>.FromValue(value).DeepMerge();

    /// <summary>A lazy prop the client deep merges into its current value.</summary>
    [OverloadResolutionPriority(CtValueTask)]
    public static InertiaProp DeepMerge<T>(Func<CancellationToken, ValueTask<T>> loader) => InertiaProp<T>.FromLoader(loader).DeepMerge();

    /// <inheritdoc cref="DeepMerge{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(CtTask)]
    public static InertiaProp DeepMerge<T>(Func<CancellationToken, Task<T>> loader) => DeepMerge(Wrap(loader));

    /// <inheritdoc cref="DeepMerge{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtTask)]
    public static InertiaProp DeepMerge<T>(Func<Task<T>> loader) => DeepMerge(Wrap(loader));

    /// <inheritdoc cref="DeepMerge{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtValueTask)]
    public static InertiaProp DeepMerge<T>(Func<ValueTask<T>> loader) => DeepMerge(Wrap(loader));

    /// <inheritdoc cref="DeepMerge{T}(Func{CancellationToken, ValueTask{T}})"/>
    public static InertiaProp DeepMerge<T>(Func<T> loader) => InertiaProp<T>.FromLoader(loader).DeepMerge();

    /// <summary>A prop resolved once and remembered by the client across visits.</summary>
    [OverloadResolutionPriority(CtValueTask)]
    public static InertiaProp Once<T>(Func<CancellationToken, ValueTask<T>> loader) => InertiaProp<T>.FromLoader(loader).Once();

    /// <inheritdoc cref="Once{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(CtTask)]
    public static InertiaProp Once<T>(Func<CancellationToken, Task<T>> loader) => Once(Wrap(loader));

    /// <inheritdoc cref="Once{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtTask)]
    public static InertiaProp Once<T>(Func<Task<T>> loader) => Once(Wrap(loader));

    /// <inheritdoc cref="Once{T}(Func{CancellationToken, ValueTask{T}})"/>
    [OverloadResolutionPriority(NoCtValueTask)]
    public static InertiaProp Once<T>(Func<ValueTask<T>> loader) => Once(Wrap(loader));

    /// <inheritdoc cref="Once{T}(Func{CancellationToken, ValueTask{T}})"/>
    public static InertiaProp Once<T>(Func<T> loader) => InertiaProp<T>.FromLoader(loader).Once();

    /// <summary>An infinite-scroll prop: <paramref name="wrapper"/> is merged and the paging metadata goes to <c>scrollProps</c>.</summary>
    /// <param name="value">The page of data; may implement <see cref="IProvidesScrollMetadata"/>.</param>
    /// <param name="wrapper">The key holding the items inside the value.</param>
    /// <param name="metadata">Paging metadata; when null the value must implement <see cref="IProvidesScrollMetadata"/>.</param>
    public static InertiaProp Scroll<T>(T value, string wrapper = "data", ScrollMetadata? metadata = null) =>
        WithScroll(InertiaProp<T>.FromValue(value), wrapper, metadata);

    /// <summary>A lazy infinite-scroll prop. See <see cref="Scroll{T}(T, string, ScrollMetadata?)"/>.</summary>
    [OverloadResolutionPriority(CtValueTask)]
    public static InertiaProp Scroll<T>(Func<CancellationToken, ValueTask<T>> loader, string wrapper = "data", ScrollMetadata? metadata = null) =>
        WithScroll(InertiaProp<T>.FromLoader(loader), wrapper, metadata);

    /// <inheritdoc cref="Scroll{T}(Func{CancellationToken, ValueTask{T}}, string, ScrollMetadata?)"/>
    [OverloadResolutionPriority(CtTask)]
    public static InertiaProp Scroll<T>(Func<CancellationToken, Task<T>> loader, string wrapper = "data", ScrollMetadata? metadata = null) =>
        Scroll(Wrap(loader), wrapper, metadata);

    /// <inheritdoc cref="Scroll{T}(Func{CancellationToken, ValueTask{T}}, string, ScrollMetadata?)"/>
    [OverloadResolutionPriority(NoCtTask)]
    public static InertiaProp Scroll<T>(Func<Task<T>> loader, string wrapper = "data", ScrollMetadata? metadata = null) =>
        Scroll(Wrap(loader), wrapper, metadata);

    /// <inheritdoc cref="Scroll{T}(Func{CancellationToken, ValueTask{T}}, string, ScrollMetadata?)"/>
    [OverloadResolutionPriority(NoCtValueTask)]
    public static InertiaProp Scroll<T>(Func<ValueTask<T>> loader, string wrapper = "data", ScrollMetadata? metadata = null) =>
        Scroll(Wrap(loader), wrapper, metadata);

    /// <inheritdoc cref="Scroll{T}(Func{CancellationToken, ValueTask{T}}, string, ScrollMetadata?)"/>
    public static InertiaProp Scroll<T>(Func<T> loader, string wrapper = "data", ScrollMetadata? metadata = null) =>
        WithScroll(InertiaProp<T>.FromLoader(loader), wrapper, metadata);

    /// <summary>Renders an Inertia page: JSON for Inertia requests, the root view otherwise.</summary>
    /// <param name="component">The client-side page component name.</param>
    /// <param name="props">An <see cref="InertiaProps"/>/dictionary, or a typed page-props object.</param>
    public static InertiaResult Render(string component, object? props = null) => new(component, props);

    /// <summary>A full page visit to <paramref name="url"/>: 409 with <c>X-Inertia-Location</c> for Inertia requests, otherwise a 302.</summary>
    public static InertiaLocationResult Location(string url) => new(url);

    /// <summary>
    /// Redirects back to the same-origin <c>Referer</c>, or to <paramref name="fallback"/> when there is none (a leading <c>~/</c> is
    /// the app root, i.e. it includes <c>PathBase</c>).
    /// Chain <see cref="InertiaBackResult.WithErrors(IDictionary{string, string[]}, string)"/> and <see cref="InertiaBackResult.WithFlash"/>.
    /// </summary>
    public static InertiaBackResult Back(string fallback = "~/") => new(fallback);

    private static Func<CancellationToken, ValueTask<T>> Wrap<T>(Func<CancellationToken, Task<T>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return ct => new ValueTask<T>(loader(ct));
    }

    private static Func<CancellationToken, ValueTask<T>> Wrap<T>(Func<Task<T>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return _ => new ValueTask<T>(loader());
    }

    private static Func<CancellationToken, ValueTask<T>> Wrap<T>(Func<ValueTask<T>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return _ => loader();
    }

    private static InertiaProp WithLoad(InertiaProp prop, PropLoad load)
    {
        prop.Load = load;
        return prop;
    }

    private static InertiaProp WithAlways(InertiaProp prop)
    {
        prop.IsAlways = true;
        return prop;
    }

    private static InertiaProp WithScroll(InertiaProp prop, string wrapper, ScrollMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(wrapper);
        prop.IsMerge = true;
        prop.ScrollWrapper = wrapper;
        prop.ScrollMetadata = metadata;
        return prop;
    }
}
