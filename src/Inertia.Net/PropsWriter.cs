using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly Utf8JsonWriter _writer;
    private readonly InertiaRequest _request;
    private readonly bool _isPartial;
    private readonly CancellationToken _cancellationToken;
    private char[] _path = ArrayPool<char>.Shared.Rent(128);
    private int _pathLength;
    private int _arrayPathLength;

    private List<string>? _mergeProps;
    private List<string>? _prependProps;
    private List<string>? _deepMergeProps;
    private List<string>? _matchPropsOn;
    private List<string>? _rescuedProps;
    private Dictionary<string, List<string>>? _deferredProps;
    private Dictionary<string, (ScrollMetadata Metadata, bool Reset)>? _scrollProps;
    private Dictionary<string, (string Prop, long? ExpiresAt)>? _onceProps;
    private PropsPlanner? _planner;

    public PropsWriter(InertiaPageWriter owner, Utf8JsonWriter writer, InertiaRequest request, string component, CancellationToken cancellationToken)
    {
        _owner = owner;
        _writer = writer;
        _request = request;
        _isPartial = string.Equals(request.PartialComponent, component, StringComparison.Ordinal);
        _cancellationToken = cancellationToken;
    }

    private ReadOnlySpan<char> Path => _path.AsSpan(0, _pathLength);

    public async ValueTask WritePropsAsync(List<PropEntry> props)
    {
        if (InertiaProp.ParallelUsed)
        {
            Plan(props);
        }

        if (_planner is null)
        {
            await WriteEntriesAsync(props);
            return;
        }

        try
        {
            await WriteEntriesAsync(props);
            await _planner.DrainAsync(cancel: false);
        }
        catch (Exception ex)
        {
            // No loader outlives the render. The walk meets failures in protocol order, except a cancellation caused by a parallel
            // loader that failed further on: then that loader's failure (the first such, in protocol order) is the render's.
            await _planner.DrainAsync(cancel: true);
            if (ex is OperationCanceledException && _planner.Failed && !_cancellationToken.IsCancellationRequested)
            {
                _planner.FirstFailure()?.Throw();
            }

            throw;
        }
        finally
        {
            _planner.Dispose();
        }
    }

    private async ValueTask WriteEntriesAsync(List<PropEntry> props)
    {
        _writer.WriteStartObject();
        foreach (var entry in props)
        {
            await WriteEntryAsync(entry.Key, -1, entry.Value, entry.Type, parentWasResolved: false);
        }

        _writer.WriteEndObject();
    }

    // Finds the Parallel() props the walk will resolve, with the walk's own inclusion rules, and starts their loaders. Covers top-level
    // props and literal objects (their children keep partial filtering); props anywhere else resolve in turn.
    private void Plan(List<PropEntry> props)
    {
        try
        {
            foreach (var entry in props)
            {
                PlanEntry(entry.Key, entry.Value, entry.Type);
            }
        }
        catch
        {
            _planner?.Dispose(); // nothing was started yet
            throw;
        }

        _planner?.Start();
    }

    private void PlanEntry(string key, object? value, Type declaredType)
    {
        var mark = Push(key);
        try
        {
            if (value is InertiaProp prop)
            {
                if (prop.IsParallel && !IsCached(prop) && Decide(prop, prop, declaredType, parentWasResolved: false) == Inclusion.Resolved)
                {
                    Prefetch(prop);
                }

                return;
            }

            // A lazy object's loader can start early; the dot keys below it are only known once it resolves, so they resolve in turn.
            if (value is InertiaPageWriter.LazyDotObject { Source.IsParallel: true } lazy
                && Decide(null, value, declaredType, parentWasResolved: false) == Inclusion.Resolved)
            {
                Prefetch(lazy.Source);
                return;
            }

            if (value is not IEnumerable<KeyValuePair<string, object?>> || Decide(null, value, declaredType, parentWasResolved: false) != Inclusion.Resolved)
            {
                return;
            }

            switch (value)
            {
                case Dictionary<string, object?> dictionary:
                    foreach (var (childKey, child) in dictionary)
                    {
                        PlanEntry(childKey, child, typeof(object));
                    }

                    break;
                case IReadOnlyDictionary<string, object?> dictionary:
                    foreach (var (childKey, child) in dictionary)
                    {
                        PlanEntry(childKey, child, typeof(object));
                    }

                    break;
                case IDictionary<string, object?> dictionary:
                    foreach (var (childKey, child) in dictionary)
                    {
                        PlanEntry(childKey, child, typeof(object));
                    }

                    break;
            }
        }
        finally
        {
            _pathLength = mark;
        }
    }

    private void Prefetch(InertiaProp prop) => (_planner ??= new PropsPlanner(_owner.Options.Props.MaxConcurrency, _cancellationToken)).Add(prop);

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
        var arrayMark = _arrayPathLength;
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            ThrowIfPlanFailed();
            var p = prop as InertiaProp;
            switch (Decide(p, prop, declaredType, parentWasResolved))
            {
                case Inclusion.Filtered:
                    return;
                case Inclusion.Excluded:
                    CollectExcludedMetadata(p!);
                    if (key is null) _writer.WriteNullValue();
                    return;
            }

            var value = prop;
            var type = declaredType;
            if (p is not null)
            {
                // A cached prop: a hit is written without a loader; a miss resolves below, then is stored and written the same way.
                // (Kept in this method's existing await, not a second one: the walk runs it for every entry.)
                if (p.CacheKey is not null && FindCached(p) is { } hit)
                {
                    WriteCached(key, p, hit, parentWasResolved);
                    return;
                }

                bool resolved;
                (resolved, value) = await ResolveAsync(p);
                if (!resolved)
                {
                    if (key is null) _writer.WriteNullValue();
                    return;
                }

                if (p.CacheKey is not null)
                {
                    WriteCached(key, p, Store(p, value), parentWasResolved);
                    return;
                }

                // A loader returned a prop: unwrap it once so it takes part in exclusion and metadata.
                if (value is InertiaProp inner)
                {
                    p = inner;
                    if (!_isPartial && IsExcludedFromInitialResponse(p))
                    {
                        CollectExcludedMetadata(p);
                        if (key is null) _writer.WriteNullValue();
                        return;
                    }

                    (resolved, value) = await ResolveAsync(p);
                    if (!resolved)
                    {
                        if (key is null) _writer.WriteNullValue();
                        return;
                    }
                }

                type = p.ValueType;
                if (_isPartial && !parentWasResolved && !p.IsAlways && _request.Except is { } except
                    && LeadsToAny(except, Path) && IsArray(value, type)) return;
                if (_isPartial && _arrayPathLength == 0 && IsArray(value, type)) _arrayPathLength = _pathLength;
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
            _arrayPathLength = arrayMark;
        }
    }

    private CachedProp? FindCached(InertiaProp prop) =>
        _owner.PropCache.TryGetValue(new PropCacheKey(prop.CacheKey!), out CachedProp? cached) ? cached : null;

    // Serializes the loader's value once and keeps it for the prop's duration.
    private CachedProp Store(InertiaProp prop, object? value)
    {
        var cached = Serialize(prop, value);
        _owner.PropCache.Set(new PropCacheKey(prop.CacheKey!), cached, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = prop.CacheDuration,
            Size = cached.Json.Length, // required when the app's cache has a SizeLimit
        });
        return cached;
    }

    // Writes a cached prop's JSON with the same partial-reload rules as a resolved value (see WriteEntryAsync). The caller restores
    // the path marks.
    private void WriteCached(string? key, InertiaProp prop, CachedProp cached, bool parentWasResolved)
    {
        if (_isPartial && !parentWasResolved && !prop.IsAlways && _request.Except is { } except && LeadsToAny(except, Path) && cached.IsArray) return;
        if (_isPartial && _arrayPathLength == 0 && cached.IsArray) _arrayPathLength = _pathLength;
        CollectMetadata(prop, cached.Scroll);
        if (key is not null)
        {
            _writer.WritePropertyName(key);
        }

        _writer.WriteRawValue(cached.Json, skipInputValidation: true);
    }

    // HTML-safe JSON, so the bytes fit both JSON responses and the page <script>.
    private CachedProp Serialize(InertiaProp prop, object? value)
    {
        if (value is InertiaProp)
        {
            throw new InvalidOperationException($"The cached prop at '{new string(Path)}' resolved to an InertiaProp. Cache() needs a plain value.");
        }

        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, _owner.HtmlWriterOptions))
        {
            if (value is null or JsonElement or JsonNode || prop.ValueType == typeof(object))
            {
                _owner.WritePlainValue(writer, value);
            }
            else
            {
                JsonSerializer.Serialize(writer, value, _owner.SerializerOptions.GetTypeInfo(prop.ValueType));
            }
        }

        var json = buffer.WrittenSpan.ToArray();
        var scroll = prop.ScrollWrapper is null ? null : prop.ScrollMetadata ?? (value as IProvidesScrollMetadata)?.GetScrollMetadata();
        return new CachedProp(json, json is [(byte)'[', ..], scroll);
    }

    private bool IsCached(InertiaProp prop) => prop.CacheKey is not null && FindCached(prop) is not null;

    private async ValueTask WriteValueAsync(object? value, Type declaredType, bool parentWasResolved)
    {
        switch (value)
        {
            case InertiaPageWriter.PropertyValue member:
                JsonSerializer.Serialize(_writer, member.Value, member.TypeInfo);
                return;
            case null:
                _writer.WriteNullValue();
                return;
            case InertiaProp:
                throw new InvalidOperationException($"The prop at '{new string(Path)}' resolved to an InertiaProp more than once.");
            case JsonElement element:
                _owner.WriteElement(_writer, element);
                return;
            case JsonNode node:
                _owner.WriteNode(_writer, node);
                return;
            case InertiaPageWriter.LazyDotObject lazy:
                // Deviation 7: the lazy prop under dot keys resolves only now that its path is part of the response. Plain props
                // are never rescued, so it always resolves.
                var (_, source) = await ResolveAsync(lazy.Source);
                await WriteObjectAsync(lazy.Apply(source), parentWasResolved);
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
        var info = _owner.SerializerOptions.GetTypeInfo(type);
        if (_owner.ContainsProps(type))
        {
            if (info.Kind == JsonTypeInfoKind.Object)
            {
                var contract = _owner.ObjectContract(value, info);
                await WriteObjectAsync(value, contract.Info, parentWasResolved, contract.Discriminator, contract.DiscriminatorName);
                return;
            }

            if (info.Kind == JsonTypeInfoKind.Dictionary && value is IDictionary dictionary)
            {
                await WriteDictionaryAsync(dictionary, info, parentWasResolved);
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

    private async ValueTask WriteObjectAsync(object value, JsonTypeInfo info, bool parentWasResolved, object? discriminator, string? discriminatorName)
    {
        info.OnSerializing?.Invoke(value);
        _writer.WriteStartObject();
        if (discriminator is string text) _writer.WriteString(discriminatorName!, text);
        else if (discriminator is int number) _writer.WriteNumber(discriminatorName!, number);
        var entries = _owner.RentObjectEntries(value, info, out var count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                await WriteEntryAsync(entries[i].Key, -1, entries[i].Value, entries[i].Type, parentWasResolved);
            }
        }
        finally
        {
            InertiaPageWriter.ReturnObjectEntries(entries, count);
        }

        _writer.WriteEndObject();
        info.OnSerialized?.Invoke(value);
    }

    // A typed dictionary (Dictionary<TKey, TValue>) whose values can hold props: keyed like System.Text.Json, walked like an object.
    private async ValueTask WriteDictionaryAsync(IDictionary dictionary, JsonTypeInfo info, bool parentWasResolved)
    {
        var keys = _owner.DictionaryKeys(dictionary, info);
        var valueType = info.ElementType ?? typeof(object);
        _writer.WriteStartObject();
        var i = 0;
        foreach (DictionaryEntry entry in dictionary)
        {
            await WriteEntryAsync(keys[i++], -1, entry.Value, valueType, parentWasResolved);
        }

        _writer.WriteEndObject();
    }

    private async ValueTask WriteArrayAsync(IEnumerable items, Type elementType, bool parentWasResolved)
    {
        var previous = _arrayPathLength;
        if (_isPartial && _arrayPathLength == 0) _arrayPathLength = _pathLength;
        try
        {
            _writer.WriteStartArray();
            var index = 0;
            foreach (var item in items)
            {
                await WriteEntryAsync(null, index++, item, elementType, parentWasResolved || _isPartial);
            }
            _writer.WriteEndArray();
        }
        finally
        {
            _arrayPathLength = previous;
        }
    }

    private async ValueTask<(bool Resolved, object? Value)> ResolveAsync(InertiaProp prop)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return (true, _planner is not null && _planner.TryTake(prop, out var started) ? await started : await prop.ResolveAsync(_cancellationToken));
        }
        catch (Exception ex) when (prop.ShouldRescue
            && !(ex is OperationCanceledException && (_cancellationToken.IsCancellationRequested || _planner is { Failed: true })))
        {
            var path = new string(Path);
            _owner.Logger.LogError(ex, "Inertia prop '{Path}' failed to resolve and was rescued.", path);
            (_rescuedProps ??= []).Add(path);
            _owner.Metrics.RecordRescued();
            return (false, null);
        }
    }

    // A parallel loader failed: the render fails, so stop walking (WritePropsAsync reports that loader's failure).
    private void ThrowIfPlanFailed()
    {
        if (_planner is { Failed: true })
        {
            throw new OperationCanceledException("A parallel prop loader failed.");
        }
    }

    private enum Inclusion
    {
        /// <summary>Not part of this response (partial reload filtering): nothing is written.</summary>
        Filtered,

        /// <summary>Left out of the initial response (optional, deferred, or a once prop the client has): only its metadata is written.</summary>
        Excluded,

        /// <summary>Resolved and written.</summary>
        Resolved,
    }

    // Whether the prop (or plain value) at Path is part of this response, decided before any loader runs and without side effects,
    // so the parallel planner and the writer agree on which loaders run.
    private Inclusion Decide(InertiaProp? p, object? prop, Type declaredType, bool parentWasResolved)
    {
        // Always props and the children of resolved values bypass partial filtering.
        if (_isPartial && !parentWasResolved && p is not { IsAlways: true })
        {
            if (!PathMatchesPartialRequest())
            {
                return Inclusion.Filtered;
            }

            if (_request.Except is { } excluded && LeadsToAny(excluded, Path) && IsArray(prop, declaredType))
            {
                return Inclusion.Filtered;
            }
        }

        return p is not null && !_isPartial && IsExcludedFromInitialResponse(p) ? Inclusion.Excluded : Inclusion.Resolved;
    }

    // Optional and deferred props are never part of the initial response, nor are once props an Inertia client already has.
    private bool IsExcludedFromInitialResponse(InertiaProp prop) =>
        prop.Load != PropLoad.Eager || (_request.IsInertia && WasAlreadyLoadedByClient(prop));

    // Props left out of the initial response still report their metadata.
    private void CollectExcludedMetadata(InertiaProp prop)
    {
        if (prop.Load == PropLoad.Eager)
        {
            CollectOnce(prop);
            return;
        }

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
                ?? value as ScrollMetadata // a cached prop passes its stored metadata
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
        if (_arrayPathLength > 0) return true;
        var path = Path;
        return (_request.Only is not { } only || MatchesAny(only, path))
            && (_request.Except is not { } except || !MatchesAny(except, path));
    }

    private bool IsArray(object? value, Type declaredType)
    {
        if (value is null or string) return false;
        if (value is JsonElement element) return element.ValueKind == JsonValueKind.Array;
        if (value is JsonNode node) return node is JsonArray;
        if (value is InertiaProp prop) return prop.ValueType != typeof(object) && _owner.SerializerOptions.GetTypeInfo(prop.ValueType).Kind == JsonTypeInfoKind.Enumerable;
        if (value is InertiaPageWriter.PropertyValue member)
            return member.TypeInfo.Kind == JsonTypeInfoKind.Enumerable;
        return value is IEnumerable && _owner.SerializerOptions.GetTypeInfo(declaredType == typeof(object) ? value.GetType() : declaredType).Kind == JsonTypeInfoKind.Enumerable;
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

/// <summary>A <see cref="InertiaProp.Cache"/> key, in a key space of its own within the app's memory cache.</summary>
internal readonly record struct PropCacheKey(string Key);

/// <summary>A cached prop: its HTML-safe JSON, whether that is an array, and a scroll prop's paging metadata.</summary>
internal sealed record CachedProp(byte[] Json, bool IsArray, ScrollMetadata? Scroll);
