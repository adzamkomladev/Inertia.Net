using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

namespace Inertia.Net;

/// <summary>A top-level prop with the declared type of its value.</summary>
internal readonly record struct PropEntry(string Key, object? Value, Type Type);

/// <summary>
/// Port of inertia-laravel 3.x <c>PropsResolver</c> (docs/protocol.md §4): one walk that filters, resolves,
/// writes the props to JSON and collects the page metadata. One instance per render.
/// </summary>
internal sealed class PropsWriter : IDisposable
{
    private readonly InertiaPageWriter _owner;
    private readonly JsonSerializerOptions _options;
    private readonly Utf8JsonWriter _writer;
    private readonly InertiaRequest _request;
    private readonly bool _isPartial;
    private readonly CancellationToken _cancellationToken;
    private char[] _path = ArrayPool<char>.Shared.Rent(128);
    private int _pathLength;

    private List<string>? _mergeProps;
    private List<string>? _prependProps;
    private List<string>? _deepMergeProps;
    private List<string>? _matchPropsOn;
    private List<string>? _rescuedProps;
    private Dictionary<string, List<string>>? _deferredProps;
    private Dictionary<string, (ScrollMetadata Metadata, bool Reset)>? _scrollProps;
    private Dictionary<string, (string Prop, long? ExpiresAt)>? _onceProps;

    public PropsWriter(InertiaPageWriter owner, Utf8JsonWriter writer, InertiaRequest request, string component, CancellationToken cancellationToken)
    {
        _owner = owner;
        _options = owner.SerializerOptions;
        _writer = writer;
        _request = request;
        _isPartial = string.Equals(request.PartialComponent, component, StringComparison.Ordinal);
        _cancellationToken = cancellationToken;
    }

    private ReadOnlySpan<char> Path => _path.AsSpan(0, _pathLength);

    public async ValueTask WritePropsAsync(List<PropEntry> props)
    {
        _writer.WriteStartObject();
        foreach (var entry in props)
        {
            await WriteEntryAsync(entry.Key, -1, entry.Value, entry.Type, parentWasResolved: false);
        }

        _writer.WriteEndObject();
    }

    /// <summary>Writes sharedProps through onceProps, in protocol order, omitting empty ones.</summary>
    public void WriteMetadata(List<string>? sharedPropKeys)
    {
        WriteList("sharedProps", sharedPropKeys);
        WriteList("mergeProps", _mergeProps);
        WriteList("prependProps", _prependProps);
        WriteList("deepMergeProps", _deepMergeProps);
        WriteList("matchPropsOn", _matchPropsOn);

        if (_deferredProps is not null)
        {
            _writer.WriteStartObject("deferredProps");
            foreach (var (group, paths) in _deferredProps)
            {
                WriteList(group, paths);
            }

            _writer.WriteEndObject();
        }

        WriteList("rescuedProps", _rescuedProps);

        if (_scrollProps is not null)
        {
            _writer.WriteStartObject("scrollProps");
            foreach (var (path, (metadata, reset)) in _scrollProps)
            {
                _writer.WriteStartObject(path);
                _writer.WriteString("pageName", metadata.PageName);
                WritePage("previousPage", metadata.PreviousPage);
                WritePage("nextPage", metadata.NextPage);
                WritePage("currentPage", metadata.CurrentPage);
                _writer.WriteBoolean("reset", reset);
                _writer.WriteEndObject();
            }

            _writer.WriteEndObject();
        }

        if (_onceProps is not null)
        {
            _writer.WriteStartObject("onceProps");
            foreach (var (key, (prop, expiresAt)) in _onceProps)
            {
                _writer.WriteStartObject(key);
                _writer.WriteString("prop", prop);
                if (expiresAt is { } ms)
                {
                    _writer.WriteNumber("expiresAt", ms);
                }
                else
                {
                    _writer.WriteNull("expiresAt");
                }

                _writer.WriteEndObject();
            }

            _writer.WriteEndObject();
        }
    }

    public void Dispose()
    {
        var path = _path;
        _path = [];
        if (path.Length > 0)
        {
            ArrayPool<char>.Shared.Return(path);
        }
    }

    // resolveProps loop body for one key (or array index).
    private async ValueTask WriteEntryAsync(string? key, int index, object? prop, Type declaredType, bool parentWasResolved)
    {
        var mark = key is null ? Push(index) : Push(key);
        try
        {
            var p = prop as InertiaProp;

            // Always props and the children of resolved values bypass partial filtering.
            if (_isPartial && !parentWasResolved && p is not { IsAlways: true } && !PathMatchesPartialRequest())
            {
                return;
            }

            if (p is not null && !_isPartial && ExcludeFromInitialResponse(p))
            {
                return;
            }

            var value = prop;
            var type = declaredType;
            if (p is not null)
            {
                bool resolved;
                (resolved, value) = await ResolveAsync(p);
                if (!resolved)
                {
                    return;
                }

                // A loader returned a prop: unwrap it once so it takes part in exclusion and metadata.
                if (value is InertiaProp inner)
                {
                    p = inner;
                    if (!_isPartial && ExcludeFromInitialResponse(p))
                    {
                        return;
                    }

                    (resolved, value) = await ResolveAsync(p);
                    if (!resolved)
                    {
                        return;
                    }
                }

                type = p.ValueType;
                CollectMetadata(p, value);
            }

            if (key is not null)
            {
                _writer.WritePropertyName(key);
            }

            // Only literal objects (dictionaries, POCOs and lists given directly) keep filtering their children.
            await WriteValueAsync(value, type, parentWasResolved || p is not null);
        }
        finally
        {
            _pathLength = mark;
        }
    }

    private async ValueTask WriteValueAsync(object? value, Type declaredType, bool parentWasResolved)
    {
        switch (value)
        {
            case InertiaPageWriter.PropertyValue member:
                JsonSerializer.Serialize(_writer, member.Value, _owner.PropertyTypeInfo(member.Property));
                return;
            case null:
                _writer.WriteNullValue();
                return;
            case InertiaProp:
                throw new InvalidOperationException($"The prop at '{new string(Path)}' resolved to an InertiaProp more than once.");
            case JsonElement element:
                element.WriteTo(_writer);
                return;
            case JsonNode node:
                node.WriteTo(_writer, _options);
                return;
            case Dictionary<string, object?> dictionary: // InertiaProps: struct enumerator, no boxing
                await WriteObjectAsync(dictionary, parentWasResolved);
                return;
            case IReadOnlyDictionary<string, object?> dictionary:
                await WriteObjectAsync(dictionary, parentWasResolved);
                return;
            case IDictionary<string, object?> dictionary:
                await WriteObjectAsync(dictionary, parentWasResolved);
                return;
        }

        var type = declaredType == typeof(object) ? value.GetType() : declaredType;
        var info = _options.GetTypeInfo(type);
        if (_owner.ContainsProps(type))
        {
            if (info.Kind == JsonTypeInfoKind.Object)
            {
                await WriteObjectAsync(value, info, parentWasResolved);
                return;
            }

            if (info.Kind == JsonTypeInfoKind.Enumerable && value is IEnumerable items)
            {
                await WriteArrayAsync(items, info.ElementType ?? typeof(object), parentWasResolved);
                return;
            }
        }

        JsonSerializer.Serialize(_writer, value, info);
    }

    private async ValueTask WriteObjectAsync(IEnumerable<KeyValuePair<string, object?>> entries, bool parentWasResolved)
    {
        _writer.WriteStartObject();
        foreach (var (key, value) in entries)
        {
            await WriteEntryAsync(key, -1, value, typeof(object), parentWasResolved);
        }

        _writer.WriteEndObject();
    }

    private async ValueTask WriteObjectAsync(Dictionary<string, object?> entries, bool parentWasResolved)
    {
        _writer.WriteStartObject();
        foreach (var (key, value) in entries)
        {
            await WriteEntryAsync(key, -1, value, typeof(object), parentWasResolved);
        }

        _writer.WriteEndObject();
    }

    private async ValueTask WriteObjectAsync(object value, JsonTypeInfo info, bool parentWasResolved)
    {
        _writer.WriteStartObject();
        foreach (var entry in _owner.ObjectEntries(value, info))
        {
            await WriteEntryAsync(entry.Key, -1, entry.Value, entry.Type, parentWasResolved);
        }

        _writer.WriteEndObject();
    }

    private async ValueTask WriteArrayAsync(IEnumerable items, Type elementType, bool parentWasResolved)
    {
        _writer.WriteStartArray();
        var index = 0;
        foreach (var item in items)
        {
            await WriteEntryAsync(null, index++, item, elementType, parentWasResolved);
        }

        _writer.WriteEndArray();
    }

    private async ValueTask<(bool Resolved, object? Value)> ResolveAsync(InertiaProp prop)
    {
        try
        {
            return (true, await prop.ResolveAsync(_cancellationToken));
        }
        catch (Exception ex) when (prop.ShouldRescue && !(ex is OperationCanceledException && _cancellationToken.IsCancellationRequested))
        {
            var path = new string(Path);
            _owner.Logger.LogError(ex, "Inertia prop '{Path}' failed to resolve and was rescued.", path);
            (_rescuedProps ??= []).Add(path);
            return (false, null);
        }
    }

    private bool ExcludeFromInitialResponse(InertiaProp prop)
    {
        // Optional and deferred props are never part of the initial response; they still report their metadata.
        if (prop.Load != PropLoad.Eager)
        {
            if (prop.Load == PropLoad.Deferred && !WasAlreadyLoadedByClient(prop))
            {
                _deferredProps ??= new(StringComparer.Ordinal);
                if (!_deferredProps.TryGetValue(prop.Group, out var paths))
                {
                    _deferredProps[prop.Group] = paths = [];
                }

                paths.Add(new string(Path));
            }

            if (prop.IsMerge)
            {
                CollectMergeable(prop);
            }

            CollectOnce(prop);
            return true;
        }

        if (_request.IsInertia && WasAlreadyLoadedByClient(prop))
        {
            CollectOnce(prop);
            return true;
        }

        return false;
    }

    private bool WasAlreadyLoadedByClient(InertiaProp prop)
    {
        if (!prop.IsOnce || prop.OnceFresh || _request.ExceptOnceProps is not { } loaded)
        {
            return false;
        }

        return prop.OnceKey is null ? Contains(loaded, Path) : Contains(loaded, prop.OnceKey);
    }

    private void CollectMetadata(InertiaProp prop, object? value)
    {
        if (prop.IsMerge)
        {
            CollectMergeable(prop);
        }

        if (prop.ScrollWrapper is not null)
        {
            var path = new string(Path);
            var metadata = prop.ScrollMetadata
                ?? (value as IProvidesScrollMetadata)?.GetScrollMetadata()
                ?? throw new InvalidOperationException($"The scroll prop at '{path}' has no metadata. Pass a ScrollMetadata or return a value that implements IProvidesScrollMetadata.");
            (_scrollProps ??= new(StringComparer.Ordinal))[path] = (metadata, Contains(_request.Reset, Path));
        }

        CollectOnce(prop);
    }

    private void CollectMergeable(InertiaProp prop)
    {
        if (Contains(_request.Reset, Path) || (_isPartial && !IsIncludedInPartialMetadata()))
        {
            return;
        }

        var path = new string(Path);
        var appendPaths = prop.AppendPaths;
        var prependPaths = prop.PrependPaths;

        // Scroll merge intent. Deliberate deviation 2: applied before any metadata is collected,
        // so a deferred scroll prop reports "{path}.{wrapper}" just like a resolved one.
        if (prop.ScrollWrapper is { } wrapper)
        {
            if (_request.MergeIntentPrepend)
            {
                prependPaths = [.. prependPaths ?? [], wrapper];
            }
            else
            {
                appendPaths = [.. appendPaths ?? [], wrapper];
            }
        }

        if (prop.IsDeepMerge)
        {
            (_deepMergeProps ??= []).Add(path);
        }
        else if (appendPaths is not { Count: > 0 } && prependPaths is not { Count: > 0 })
        {
            (prop.AppendAtRoot ? _mergeProps ??= [] : _prependProps ??= []).Add(path);
        }
        else
        {
            AddPaths(ref _mergeProps, path, appendPaths);
            AddPaths(ref _prependProps, path, prependPaths);
        }

        AddPaths(ref _matchPropsOn, path, prop.MatchOnKeys);
    }

    // Adds "{path}.{suffix}" for each suffix. (A `suffixes ?? []` fallback would allocate an empty list per call.)
    private static void AddPaths(ref List<string>? target, string path, List<string>? suffixes)
    {
        if (suffixes is null)
        {
            return;
        }

        foreach (var suffix in suffixes)
        {
            (target ??= []).Add($"{path}.{suffix}");
        }
    }

    private void CollectOnce(InertiaProp prop)
    {
        if (!prop.IsOnce || (_isPartial && !IsIncludedInPartialMetadata()))
        {
            return;
        }

        var path = new string(Path);
        long? expiresAt = prop.OnceTtl is { } ttl
            ? _owner.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds() + (long)ttl.TotalMilliseconds
            : null;
        (_onceProps ??= new(StringComparer.Ordinal))[prop.OnceKey ?? path] = (path, expiresAt);
    }

    private bool PathMatchesPartialRequest()
    {
        var path = Path;
        if (_request.Only is { } only && !MatchesAny(only, path) && !LeadsToAny(only, path))
        {
            return false;
        }

        return _request.Except is not { } except || !MatchesAny(except, path);
    }

    private bool IsIncludedInPartialMetadata()
    {
        var path = Path;
        return (_request.Only is not { } only || MatchesAny(only, path))
            && (_request.Except is not { } except || !MatchesAny(except, path));
    }

    // path == filter, or path is a descendant of filter.
    internal static bool MatchesAny(string[] filters, ReadOnlySpan<char> path)
    {
        foreach (var filter in filters)
        {
            if (path.StartsWith(filter) && (path.Length == filter.Length || path[filter.Length] == '.'))
            {
                return true;
            }
        }

        return false;
    }

    // path is an ancestor of filter.
    internal static bool LeadsToAny(string[] filters, ReadOnlySpan<char> path)
    {
        foreach (var filter in filters)
        {
            if (filter.Length > path.Length && filter[path.Length] == '.' && filter.AsSpan().StartsWith(path))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(string[]? list, ReadOnlySpan<char> value)
    {
        foreach (var item in list ?? [])
        {
            if (value.SequenceEqual(item))
            {
                return true;
            }
        }

        return false;
    }

    private int Push(string key)
    {
        var mark = _pathLength;
        EnsurePathCapacity(mark + key.Length + 1);
        if (mark > 0)
        {
            _path[_pathLength++] = '.';
        }

        key.CopyTo(_path.AsSpan(_pathLength));
        _pathLength += key.Length;
        return mark;
    }

    private int Push(int index)
    {
        var mark = _pathLength;
        EnsurePathCapacity(mark + 12);
        if (mark > 0)
        {
            _path[_pathLength++] = '.';
        }

        index.TryFormat(_path.AsSpan(_pathLength), out var written, default, CultureInfo.InvariantCulture);
        _pathLength += written;
        return mark;
    }

    private void EnsurePathCapacity(int capacity)
    {
        if (_path.Length >= capacity)
        {
            return;
        }

        var bigger = ArrayPool<char>.Shared.Rent(Math.Max(capacity, _path.Length * 2));
        Path.CopyTo(bigger);
        ArrayPool<char>.Shared.Return(_path);
        _path = bigger;
    }

    private void WriteList(string name, List<string>? values)
    {
        if (values is not { Count: > 0 })
        {
            return;
        }

        _writer.WriteStartArray(name);
        foreach (var value in values)
        {
            _writer.WriteStringValue(value);
        }

        _writer.WriteEndArray();
    }

    private void WritePage(string name, object? page)
    {
        switch (page)
        {
            case null:
                _writer.WriteNull(name);
                break;
            case int number:
                _writer.WriteNumber(name, number);
                break;
            case long number:
                _writer.WriteNumber(name, number);
                break;
            case string text:
                _writer.WriteString(name, text);
                break;
            default:
                throw new InvalidOperationException($"Scroll page values must be int, long, string or null, not '{page.GetType()}'.");
        }
    }
}
