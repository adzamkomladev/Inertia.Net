using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inertia.Net;

/// <summary>
/// A page prop with Inertia loading behavior (lazy, optional, deferred, always, merge, once, scroll, rescue).
/// Create instances with the factories on <see cref="Inertia"/>; the fluent methods mutate and return the same instance.
/// </summary>
[JsonConverter(typeof(InertiaPropJsonConverter))]
public abstract class InertiaProp
{
    private protected InertiaProp()
    {
    }

    internal PropLoad Load;
    internal string Group = "default";
    internal bool IsAlways;
    internal bool IsOnce;
    internal string? OnceKey;
    internal TimeSpan? OnceTtl;
    internal bool OnceFresh;
    internal bool IsMerge;
    internal bool IsDeepMerge;
    internal bool AppendAtRoot = true;
    internal List<string>? AppendPaths;
    internal List<string>? PrependPaths;
    internal List<string>? MatchOnKeys;
    internal string? ScrollWrapper;
    internal ScrollMetadata? ScrollMetadata;
    internal bool ShouldRescue;

    /// <summary>The declared type of the value, used to pick the <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo"/>.</summary>
    internal abstract Type ValueType { get; }

    /// <summary>Returns the constant value or runs the loader.</summary>
    internal abstract ValueTask<object?> ResolveAsync(CancellationToken cancellationToken);

    /// <summary>Resolves the prop once: the client keeps it and skips it on later visits.</summary>
    /// <param name="key">Custom once key (defaults to the prop path).</param>
    /// <param name="until">How long the client may keep the value.</param>
    public InertiaProp Once(string? key = null, TimeSpan? until = null)
    {
        IsOnce = true;
        if (key is not null)
        {
            OnceKey = key;
        }

        if (until is not null)
        {
            OnceTtl = until;
        }

        return this;
    }

    /// <summary>Sets the once key (implies <see cref="Once"/>).</summary>
    public InertiaProp As(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Once(key);
    }

    /// <summary>Sets how long the client may keep a once prop (implies <see cref="Once"/>).</summary>
    public InertiaProp Until(TimeSpan ttl) => Once(until: ttl);

    /// <summary>Forces a once prop to be sent even when the client already has it.</summary>
    public InertiaProp Fresh(bool fresh = true)
    {
        OnceFresh = fresh;
        return this;
    }

    /// <summary>Merges by appending, at the root (<paramref name="path"/> null) or at a nested path.</summary>
    /// <param name="path">Nested path to append at, relative to the prop.</param>
    /// <param name="matchOn">Key used to match items, relative to <paramref name="path"/>.</param>
    public InertiaProp Append(string? path = null, string? matchOn = null) => AddMergePath(append: true, path, matchOn);

    /// <summary>Merges by prepending, at the root (<paramref name="path"/> null) or at a nested path.</summary>
    /// <param name="path">Nested path to prepend at, relative to the prop.</param>
    /// <param name="matchOn">Key used to match items, relative to <paramref name="path"/>.</param>
    public InertiaProp Prepend(string? path = null, string? matchOn = null) => AddMergePath(append: false, path, matchOn);

    /// <summary>Deep merges the value into the client's existing value.</summary>
    public InertiaProp DeepMerge()
    {
        IsMerge = true;
        IsDeepMerge = true;
        return this;
    }

    /// <summary>Replaces the keys used to match merged items (implies merge).</summary>
    public InertiaProp MatchOn(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        IsMerge = true;
        MatchOnKeys = [.. keys];
        return this;
    }

    /// <summary>When the loader throws, logs the exception and reports the prop in <c>rescuedProps</c> instead of failing the page.</summary>
    public InertiaProp Rescue(bool rescue = true)
    {
        ShouldRescue = rescue;
        return this;
    }

    /// <summary>Excludes the prop from the initial load; the client fetches it afterwards with its group.</summary>
    public InertiaProp Defer(string group = "default")
    {
        ArgumentNullException.ThrowIfNull(group);
        Load = PropLoad.Deferred;
        Group = group;
        return this;
    }

    /// <summary>Sets the deferred group.</summary>
    public InertiaProp WithGroup(string group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Group = group;
        return this;
    }

    private InertiaProp AddMergePath(bool append, string? path, string? matchOn)
    {
        IsMerge = true;
        if (path is null)
        {
            AppendAtRoot = append;
            if (matchOn is not null)
            {
                (MatchOnKeys ??= []).Add(matchOn);
            }

            return this;
        }

        (append ? AppendPaths ??= [] : PrependPaths ??= []).Add(path);
        if (matchOn is not null)
        {
            (MatchOnKeys ??= []).Add($"{path}.{matchOn}");
        }

        return this;
    }
}

internal enum PropLoad
{
    Eager,
    Optional,
    Deferred,
}

internal sealed class InertiaProp<T> : InertiaProp
{
    private readonly T? _value;
    private readonly Func<CancellationToken, ValueTask<T>>? _loader;

    private InertiaProp(T? value, Func<CancellationToken, ValueTask<T>>? loader)
    {
        _value = value;
        _loader = loader;
    }

    internal static InertiaProp<T> FromValue(T value) => new(value, null);

    internal static InertiaProp<T> FromLoader(Func<CancellationToken, ValueTask<T>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new(default, loader);
    }

    internal static InertiaProp<T> FromLoader(Func<T> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new(default, _ => new ValueTask<T>(loader()));
    }

    internal override Type ValueType => typeof(T);

    internal override async ValueTask<object?> ResolveAsync(CancellationToken cancellationToken) =>
        _loader is null ? _value : await _loader(cancellationToken);
}

/// <summary>
/// Lets System.Text.Json (including source-generated contexts) accept types with <see cref="InertiaProp"/> members.
/// Props are only ever written by Inertia.Net; serializing one directly throws.
/// </summary>
public sealed class InertiaPropJsonConverter : JsonConverter<InertiaProp>
{
    /// <inheritdoc />
    public override InertiaProp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new InvalidOperationException("InertiaProp values can only be serialized by Inertia.Net");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, InertiaProp value, JsonSerializerOptions options) =>
        throw new InvalidOperationException("InertiaProp values can only be serialized by Inertia.Net");
}
