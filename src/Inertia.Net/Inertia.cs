namespace Inertia.Net;

/// <summary>Factories for Inertia props and results.</summary>
public static class Inertia
{
    /// <summary>A lazy prop: the loader only runs when the prop is part of the response.</summary>
    public static InertiaProp Prop<T>(Func<CancellationToken, ValueTask<T>> loader) => InertiaProp<T>.FromLoader(loader);

    /// <summary>A lazy prop: the loader only runs when the prop is part of the response.</summary>
    public static InertiaProp Prop<T>(Func<T> loader) => InertiaProp<T>.FromLoader(loader);

    /// <summary>A prop that is never sent on the first load, only when a partial reload asks for it.</summary>
    public static InertiaProp Optional<T>(Func<CancellationToken, ValueTask<T>> loader) => WithLoad(InertiaProp<T>.FromLoader(loader), PropLoad.Optional);

    /// <summary>A prop that is never sent on the first load, only when a partial reload asks for it.</summary>
    public static InertiaProp Optional<T>(Func<T> loader) => WithLoad(InertiaProp<T>.FromLoader(loader), PropLoad.Optional);

    /// <summary>A prop the client loads right after the first render, together with the other props of its group.</summary>
    public static InertiaProp Defer<T>(Func<CancellationToken, ValueTask<T>> loader, string group = "default") => InertiaProp<T>.FromLoader(loader).Defer(group);

    /// <summary>A prop the client loads right after the first render, together with the other props of its group.</summary>
    public static InertiaProp Defer<T>(Func<T> loader, string group = "default") => InertiaProp<T>.FromLoader(loader).Defer(group);

    /// <summary>A prop that is always sent, even when a partial reload does not ask for it.</summary>
    public static InertiaProp Always(object? value) => WithAlways(InertiaProp<object?>.FromValue(value));

    /// <summary>A lazy prop that is always sent, even when a partial reload does not ask for it.</summary>
    public static InertiaProp Always<T>(Func<CancellationToken, ValueTask<T>> loader) => WithAlways(InertiaProp<T>.FromLoader(loader));

    /// <summary>A lazy prop that is always sent, even when a partial reload does not ask for it.</summary>
    public static InertiaProp Always<T>(Func<T> loader) => WithAlways(InertiaProp<T>.FromLoader(loader));

    /// <summary>A prop the client appends to its current value instead of replacing it.</summary>
    public static InertiaProp Merge<T>(T value) => InertiaProp<T>.FromValue(value).Append();

    /// <summary>A lazy prop the client appends to its current value instead of replacing it.</summary>
    public static InertiaProp Merge<T>(Func<CancellationToken, ValueTask<T>> loader) => InertiaProp<T>.FromLoader(loader).Append();

    /// <summary>A lazy prop the client appends to its current value instead of replacing it.</summary>
    public static InertiaProp Merge<T>(Func<T> loader) => InertiaProp<T>.FromLoader(loader).Append();

    /// <summary>A prop the client deep merges into its current value.</summary>
    public static InertiaProp DeepMerge<T>(T value) => InertiaProp<T>.FromValue(value).DeepMerge();

    /// <summary>A lazy prop the client deep merges into its current value.</summary>
    public static InertiaProp DeepMerge<T>(Func<CancellationToken, ValueTask<T>> loader) => InertiaProp<T>.FromLoader(loader).DeepMerge();

    /// <summary>A lazy prop the client deep merges into its current value.</summary>
    public static InertiaProp DeepMerge<T>(Func<T> loader) => InertiaProp<T>.FromLoader(loader).DeepMerge();

    /// <summary>A prop resolved once and remembered by the client across visits.</summary>
    public static InertiaProp Once<T>(Func<CancellationToken, ValueTask<T>> loader) => InertiaProp<T>.FromLoader(loader).Once();

    /// <summary>A prop resolved once and remembered by the client across visits.</summary>
    public static InertiaProp Once<T>(Func<T> loader) => InertiaProp<T>.FromLoader(loader).Once();

    /// <summary>An infinite-scroll prop: <paramref name="wrapper"/> is merged and the paging metadata goes to <c>scrollProps</c>.</summary>
    /// <param name="value">The page of data; may implement <see cref="IProvidesScrollMetadata"/>.</param>
    /// <param name="wrapper">The key holding the items inside the value.</param>
    /// <param name="metadata">Paging metadata; when null the value must implement <see cref="IProvidesScrollMetadata"/>.</param>
    public static InertiaProp Scroll<T>(T value, string wrapper = "data", ScrollMetadata? metadata = null) =>
        WithScroll(InertiaProp<T>.FromValue(value), wrapper, metadata);

    /// <summary>A lazy infinite-scroll prop. See <see cref="Scroll{T}(T, string, ScrollMetadata?)"/>.</summary>
    public static InertiaProp Scroll<T>(Func<CancellationToken, ValueTask<T>> loader, string wrapper = "data", ScrollMetadata? metadata = null) =>
        WithScroll(InertiaProp<T>.FromLoader(loader), wrapper, metadata);

    /// <summary>A lazy infinite-scroll prop. See <see cref="Scroll{T}(T, string, ScrollMetadata?)"/>.</summary>
    public static InertiaProp Scroll<T>(Func<T> loader, string wrapper = "data", ScrollMetadata? metadata = null) =>
        WithScroll(InertiaProp<T>.FromLoader(loader), wrapper, metadata);

    /// <summary>Renders an Inertia page: JSON for Inertia requests, the root view otherwise.</summary>
    /// <param name="component">The client-side page component name.</param>
    /// <param name="props">An <see cref="InertiaProps"/>/dictionary, or a typed page-props object.</param>
    public static InertiaResult Render(string component, object? props = null) => new(component, props);

    /// <summary>A full page visit to <paramref name="url"/>: 409 with <c>X-Inertia-Location</c> for Inertia requests, otherwise a 302.</summary>
    public static InertiaLocationResult Location(string url) => new(url);

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
