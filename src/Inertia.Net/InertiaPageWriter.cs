using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ObjectPool;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Inertia.Net;

/// <summary>Writes the page object (protocol §1) into a pooled buffer. Singleton; holds no per-request state.</summary>
internal sealed class InertiaPageWriter
{
    private static readonly JavaScriptEncoder HtmlSafeEncoder = CreateHtmlSafeEncoder();

    // The errors prop when there are none (the common case). Never mutated: the props walk only reads it.
    private static readonly InertiaProp NoErrors = Inertia.Always(new InertiaProps());

    private static readonly ObjectPool<List<PropEntry>> EntryLists = new DefaultObjectPool<List<PropEntry>>(new EntryListPolicy());

    private sealed class EntryListPolicy : PooledObjectPolicy<List<PropEntry>>
    {
        public override List<PropEntry> Create() => [];

        public override bool Return(List<PropEntry> entries)
        {
            entries.Clear();
            // Do not retain unusually large pages in the pool.
            return entries.Capacity <= 1024;
        }
    }

    private readonly ConcurrentDictionary<Type, bool> _containsProps = new();
    private readonly ConcurrentDictionary<(JsonPropertyInfo Property, JsonNumberHandling? Handling), JsonTypeInfo> _propertySerializers = new();
    private readonly ConcurrentDictionary<(JsonConverter? Converter, JsonNumberHandling? Handling), JsonSerializerOptions> _memberOptions = new();
    private readonly ConcurrentDictionary<Type, object> _defaultValues = new();
    private const int MinPageCapacity = 4096, MaxPageCapacityHint = 8 * 1024 * 1024, MaxComponentHints = 1024;
    private readonly ConcurrentDictionary<string, ComponentHints> _hints = new(StringComparer.Ordinal);
    private int _hintCount;
    private readonly ObjectPool<PropsWriter> _propsWriters;
    private readonly JsonWriterOptions _jsonWriterOptions;
    private readonly JsonWriterOptions _htmlWriterOptions;

    private readonly VersionProvider _versionProvider;

    public InertiaPageWriter(
        IOptions<InertiaOptions> options,
        IOptions<HttpJsonOptions> jsonOptions,
        TimeProvider timeProvider,
        ILogger<InertiaPageWriter> logger,
        VersionProvider versionProvider,
        InertiaMetrics metrics,
        InertiaCaching caching,
        PageComponentCheck pages)
    {
        Options = options.Value;
        TimeProvider = timeProvider;
        Logger = logger;
        Metrics = metrics;
        Caching = caching;
        Pages = pages.Enabled ? pages : null;
        _versionProvider = versionProvider;

        var source = jsonOptions.Value.SerializerOptions;
        var serializerOptions = new JsonSerializerOptions(source)
        {
            TypeInfoResolver = source.TypeInfoResolver is null
                ? InertiaJsonContext.Default
                : JsonTypeInfoResolver.Combine(source.TypeInfoResolver, InertiaJsonContext.Default),
        };
        if (Options.PreserveBigIntegers)
        {
            BigIntegerConverters.AddTo(serializerOptions.Converters);
        }

        // Declared property names (and enum names) are pre-encoded with the options encoder, not the writer's. The default encoder
        // escapes '<', '>' and '&'; a custom one such as UnsafeRelaxedJsonEscaping does not, so a name could close the page
        // <script>. The HTML-safe encoder replaces it; like the relaxed one, it leaves non-ASCII text unescaped.
        if (source.Encoder is { } encoder && !ReferenceEquals(encoder, JavaScriptEncoder.Default))
        {
            serializerOptions.Encoder = HtmlSafeEncoder;
        }

        serializerOptions.MakeReadOnly();
        SerializerOptions = serializerOptions;
        _jsonWriterOptions = new JsonWriterOptions { Encoder = source.Encoder, Indented = serializerOptions.WriteIndented };
        _htmlWriterOptions = new JsonWriterOptions { Encoder = HtmlSafeEncoder };
        _propsWriters = new DefaultObjectPool<PropsWriter>(new PropsWriterPolicy(this));
    }

    public InertiaOptions Options { get; }

    public JsonSerializerOptions SerializerOptions { get; }

    public TimeProvider TimeProvider { get; }

    public ILogger Logger { get; }

    public InertiaMetrics Metrics { get; }

    /// <summary>The store access for <see cref="InertiaProp.Cache(string, TimeSpan)"/> values.</summary>
    public InertiaCaching Caching { get; }

    /// <summary>The Development pages check, or null while it is off (no per-render lookup).</summary>
    public PageComponentCheck? Pages { get; }

    /// <summary>Writer options whose output is valid JSON and safe inside a <c>&lt;script&gt;</c> element.</summary>
    public JsonWriterOptions HtmlWriterOptions => _htmlWriterOptions;

    /// <summary>Writer options for JSON responses (the app's encoder and indentation).</summary>
    public JsonWriterOptions JsonWriterOptions => _jsonWriterOptions;

    /// <summary>
    /// Resolves the props and writes the whole page object. With <paramref name="htmlSafe"/> the JSON can be embedded
    /// in a <c>&lt;script&gt;</c> element as-is. The caller disposes the returned buffer.
    /// </summary>
    public async ValueTask<PooledBufferWriter> WritePageAsync(HttpContext httpContext, string component, object? props, InertiaRequest request, bool htmlSafe)
    {
        _hints.TryGetValue(component, out var hints);
        var buffer = new PooledBufferWriter(hints?.PageCapacity ?? MinPageCapacity);
        var pooledEntries = EntryLists.Get();
        var propsWriter = _propsWriters.Get();
        try
        {
            httpContext.RequestAborted.ThrowIfCancellationRequested();
            var rootInfo = props is not null and not IEnumerable<KeyValuePair<string, object?>>
                ? SerializerOptions.GetTypeInfo(props.GetType()) : null;
            var feature = httpContext.Features.Get<InertiaFeature>();
            var entries = BuildProps(httpContext, feature, props, request, pooledEntries, out var sharedCount, out var hasDotKey);
            if (hasDotKey)
            {
                entries = UnpackDotProps(entries);
            }

            // A component runs the planning walk until a render shows it has no Parallel() props, and again once a render meets one.
            var plan = InertiaProp.ParallelUsed && hints?.UsesParallel != false;
            var writer = propsWriter.Begin(buffer, htmlSafe, request, component, httpContext.RequestAborted, plan);

            writer.WriteStartObject();
            writer.WriteString("component", component);
            writer.WritePropertyName("props");
            await propsWriter.WritePropsAsync(entries);
            propsWriter.ScheduleRefreshes(httpContext);
            rootInfo?.OnSerialized?.Invoke(props!);
            feature = httpContext.Features.Get<InertiaFeature>();
            if (Options.UrlResolver is { } resolveUrl)
            {
                writer.WriteString("url", resolveUrl(httpContext));
            }
            else
            {
                writer.WritePropertyName("url");
                WriteUrl(writer, httpContext.Request);
            }

            writer.WriteString("version", _versionProvider.GetVersion(httpContext));
            propsWriter.WriteMetadata(pooledEntries, Options.ExposeSharedPropKeys ? sharedCount : 0);

            if (Options.PreserveBigIntegers)
            {
                writer.WriteBoolean("preserveBigIntegers", true);
            }

            if (feature?.HistoryCleared == true)
            {
                writer.WriteBoolean("clearHistory", true);
            }

            if (feature?.HistoryEncrypted ?? Options.EncryptHistory)
            {
                writer.WriteBoolean("encryptHistory", true);
            }

            if (feature?.FlashData is { Count: > 0 } flash)
            {
                writer.WriteStartObject("flash");
                foreach (var (key, value) in flash)
                {
                    writer.WritePropertyName(key);
                    WritePlainValue(writer, value);
                }

                writer.WriteEndObject();
            }

            if (feature?.FragmentPreserved == true)
            {
                writer.WriteBoolean("preserveFragment", true);
            }

            writer.WriteEndObject();
            writer.Flush();
            RecordHints(component, hints, buffer.WrittenMemory.Length, propsWriter.SawParallel);
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
        finally
        {
            _propsWriters.Return(propsWriter);
            EntryLists.Return(pooledEntries);
        }
    }

    /// <summary>The initial page buffer size for <paramref name="component"/>: the recent page size plus headroom, at least 4 KiB.</summary>
    internal int InitialCapacity(string component) => _hints.TryGetValue(component, out var hints) ? hints.PageCapacity : MinPageCapacity;

    /// <summary>Whether renders of <paramref name="component"/> run the planning walk; null before its first render.</summary>
    internal bool? UsesParallel(string component) => _hints.TryGetValue(component, out var hints) ? hints.UsesParallel : null;

    // The page buffer grows to a larger page at once (so a large page is not copied through every doubling of a 4 KiB buffer on each
    // render) and shrinks back gradually, so one unusually large render does not pin a large buffer for the component. A component that
    // met a Parallel() prop keeps planning: a partial reload that filters the prop out does not turn it off. Bounded per component
    // count and size; components are developer-chosen names. A race between two renders only loses one of their updates.
    private void RecordHints(string component, ComponentHints? hints, int written, bool sawParallel)
    {
        var target = (int)Math.Clamp(written + (long)written / 8, MinPageCapacity, MaxPageCapacityHint);
        if (hints is null)
        {
            if (Volatile.Read(ref _hintCount) < MaxComponentHints && _hints.TryAdd(component, new ComponentHints(target, sawParallel)))
            {
                Interlocked.Increment(ref _hintCount);
            }

            return;
        }

        var capacity = hints.PageCapacity;
        var next = target >= capacity ? target : capacity - (capacity - target) / 4;
        if (next != capacity)
        {
            hints.PageCapacity = next;
        }

        if (sawParallel && !hints.UsesParallel)
        {
            hints.UsesParallel = true;
        }
    }

    // The url as segments of one JSON string, so path base, path and query are not concatenated first.
    private static void WriteUrl(Utf8JsonWriter writer, HttpRequest request)
    {
        var pathBase = request.PathBase.ToUriComponent();
        var path = request.Path.ToUriComponent();
        var query = request.QueryString.ToUriComponent();
        if (pathBase.Length == 0 && path.Length == 0 && query.Length == 0)
        {
            writer.WriteStringValue("/");
            return;
        }

        writer.WriteStringValueSegment(pathBase, isFinalSegment: false);
        writer.WriteStringValueSegment(path, isFinalSegment: false);
        writer.WriteStringValueSegment(query, isFinalSegment: true);
    }

    /// <summary>What earlier renders of a component showed: how large its page is, and whether it has Parallel() props.</summary>
    private sealed class ComponentHints(int pageCapacity, bool usesParallel)
    {
        public volatile int PageCapacity = pageCapacity;

        public volatile bool UsesParallel = usesParallel;
    }

    private sealed class PropsWriterPolicy(InertiaPageWriter owner) : PooledObjectPolicy<PropsWriter>
    {
        public override PropsWriter Create() => new(owner);

        public override bool Return(PropsWriter writer)
        {
            writer.Clear();
            return true;
        }
    }

    /// <summary>Whether values of <paramref name="type"/> can hold props and must be walked instead of serialized in one call.</summary>
    public bool ContainsProps(Type type) =>
        _containsProps.GetOrAdd(type, static (t, self) => self.ComputeContainsProps(t, []), this);

    /// <summary>Reads a POCO property the way System.Text.Json would write it.</summary>
    public bool TryGetPropertyValue(JsonPropertyInfo property, object target, out object? value)
    {
        value = null;
        if (property.Get is null)
        {
            return false;
        }

        value = property.Get(target);
        if (property.ShouldSerialize is { } shouldSerialize)
        {
            return shouldSerialize(target, value);
        }

        if (property.IsExtensionData) return true;
        return SerializerOptions.DefaultIgnoreCondition switch
        {
            JsonIgnoreCondition.WhenWritingNull => value is not null,
            JsonIgnoreCondition.WhenWritingDefault => value is not null && (!property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) is not null
                || !value.Equals(_defaultValues.GetOrAdd(property.PropertyType, CreateDefaultValue))),
            _ => true,
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Only boxed value types already present in registered JSON metadata are used; creating their zero value does not call a constructor.")]
    private static object CreateDefaultValue(Type type) => RuntimeHelpers.GetUninitializedObject(type);

    // Keep the member's converter/number handling using the application's existing type resolver.
    internal readonly record struct PropertyValue(JsonTypeInfo TypeInfo, object? Value);

    private JsonTypeInfo PropertyTypeInfo(JsonPropertyInfo property, JsonNumberHandling? handling) =>
        _propertySerializers.GetOrAdd((property, handling),
            static (key, self) => self.MemberOptions(key.Property.CustomConverter, key.Handling).GetTypeInfo(key.Property.PropertyType), this);

    // One options instance (with its own metadata cache) per converter instance and number handling, not per member: every member
    // that only differs by [JsonNumberHandling] shares one. Converters are compared by instance, since two instances of one converter
    // type can be configured differently (custom JsonConverterAttribute subclasses).
    private JsonSerializerOptions MemberOptions(JsonConverter? converter, JsonNumberHandling? handling) =>
        _memberOptions.GetOrAdd((converter, handling), static (key, self) =>
        {
            var options = new JsonSerializerOptions(self.SerializerOptions)
            {
                NumberHandling = key.Handling ?? self.SerializerOptions.NumberHandling,
            };
            if (key.Converter is { } converter)
            {
                options.Converters.Insert(0, converter);
            }

            options.MakeReadOnly();
            return options;
        }, this);

    /// <summary>The number of member serializer options created (for tests).</summary>
    internal int MemberOptionsCount => _memberOptions.Count;

    /// <summary>
    /// The members of a typed object as System.Text.Json would write them, in a pooled array (no enumerator per object).
    /// Hand the array back with <see cref="ReturnObjectEntries"/>.
    /// </summary>
    public PropEntry[] RentObjectEntries(object target, JsonTypeInfo info, out int count)
    {
        var entries = ArrayPool<PropEntry>.Shared.Rent(Math.Max(info.Properties.Count, 1));
        count = 0;
        for (var i = 0; i < info.Properties.Count; i++)
        {
            var property = info.Properties[i];
            if (!TryGetPropertyValue(property, target, out var value))
            {
                continue;
            }
            if (property.IsExtensionData)
            {
                if (value is null)
                {
                    continue;
                }
                // Extension keys are literal JSON names: DictionaryKeyPolicy must not rename them.
                if (value is IEnumerable<KeyValuePair<string, object?>> objects)
                {
                    foreach (var entry in objects) Add(ref entries, ref count, new(entry.Key, entry.Value, typeof(object)));
                }
                else if (value is IEnumerable<KeyValuePair<string, JsonElement>> elements)
                {
                    foreach (var entry in elements) Add(ref entries, ref count, new(entry.Key, entry.Value, typeof(JsonElement)));
                }
                else if (value is JsonObject nodes)
                {
                    foreach (var entry in nodes) Add(ref entries, ref count, new(entry.Key, entry.Value, typeof(JsonNode)));
                }
            }
            else
            {
                var handling = property.NumberHandling ?? info.NumberHandling;
                Add(ref entries, ref count, new(property.Name,
                    value is not InertiaProp && (property.CustomConverter is not null || (handling is not null && !ContainsProps(property.PropertyType)))
                        ? new PropertyValue(PropertyTypeInfo(property, handling), value) : value,
                    property.PropertyType));
            }
        }

        return entries;

        static void Add(ref PropEntry[] entries, ref int count, PropEntry entry)
        {
            if (count == entries.Length)
            {
                var bigger = ArrayPool<PropEntry>.Shared.Rent(entries.Length * 2);
                entries.AsSpan(0, count).CopyTo(bigger);
                ReturnObjectEntries(entries, count);
                entries = bigger;
            }

            entries[count++] = entry;
        }
    }

    /// <summary>Clears the references in an array from <see cref="RentObjectEntries"/> and returns it to the pool.</summary>
    public static void ReturnObjectEntries(PropEntry[] entries, int count)
    {
        entries.AsSpan(0, count).Clear();
        ArrayPool<PropEntry>.Shared.Return(entries);
    }

    // errors (always), then option shares, then request shares, then page props; later keys override earlier ones in place, so the
    // first sharedProps entries are the shared ones. Synchronous, so the Set local function captures its locals in a stack struct
    // instead of a heap closure.
    private List<PropEntry> BuildProps(HttpContext httpContext, InertiaFeature? feature, object? props, InertiaRequest request, List<PropEntry> entries, out int sharedProps, out bool dotKeys)
    {
        var propCount = props switch
        {
            ICollection<KeyValuePair<string, object?>> collection => collection.Count,
            IReadOnlyCollection<KeyValuePair<string, object?>> collection => collection.Count,
            _ => 16,
        };
        var sharedCapacity = 1 + Options.SharedProps.Count + (feature?.SharedProps?.Count ?? 0);
        entries.EnsureCapacity(sharedCapacity + propCount);

        // Page props come from one dictionary or object, so their keys are unique: a key can only collide with an earlier
        // errors/shared entry. Measurements favor indexing only for large pages with dozens of shared keys.
        Dictionary<string, int>? sharedPositions = sharedCapacity >= 32 && propCount >= 200 ? new(sharedCapacity, StringComparer.Ordinal) : null;
        var sharedCount = 0;
        var hasDotKey = false;

        void Set(string key, object? value, Type type, bool shared)
        {
            var position = -1;
            if (sharedPositions is not null)
            {
                if (!sharedPositions.TryGetValue(key, out position)) position = -1;
            }
            else
            {
                for (var i = 0; i < sharedCount; i++)
                {
                    if (string.Equals(entries[i].Key, key, StringComparison.Ordinal))
                    {
                        position = i;
                        break;
                    }
                }
            }

            if (position >= 0)
            {
                entries[position] = new PropEntry(key, value, type);
            }
            else
            {
                entries.Add(new PropEntry(key, value, type));
                if (shared)
                {
                    sharedPositions?.Add(key, entries.Count - 1);
                    sharedCount++;
                }
            }

            hasDotKey |= key.Contains('.', StringComparison.Ordinal);
        }

        Set("errors", NoErrors, typeof(object), shared: true);

        foreach (var (key, value) in Options.SharedProps)
        {
            httpContext.RequestAborted.ThrowIfCancellationRequested();
            Set(key, value is Func<HttpContext, object?> factory ? factory(httpContext) : value, typeof(object), shared: true);
        }

        feature = httpContext.Features.Get<InertiaFeature>();
        if (ReferenceEquals(entries[0].Value, NoErrors) && feature?.Errors is { Count: > 0 } bags)
            Set("errors", Inertia.Always(ErrorsResolver.Resolve(bags, Options.WithAllErrors, request.ErrorBag)), typeof(object), shared: true);
        if (feature?.SharedProps is { } requestShared)
        {
            foreach (var (key, value) in requestShared)
            {
                Set(key, value, typeof(object), shared: true);
            }
        }

        switch (props)
        {
            case null:
                break;
            case Dictionary<string, object?> dictionary: // InertiaProps: struct enumerator, no boxing
                foreach (var (key, value) in dictionary)
                {
                    Set(key, value, typeof(object), shared: false);
                }

                break;
            case IReadOnlyDictionary<string, object?> dictionary:
                foreach (var (key, value) in dictionary)
                {
                    Set(key, value, typeof(object), shared: false);
                }

                break;
            case IDictionary<string, object?> dictionary:
                foreach (var (key, value) in dictionary)
                {
                    Set(key, value, typeof(object), shared: false);
                }

                break;
            default:
                var info = SerializerOptions.GetTypeInfo(props.GetType());
                if (info.Kind != JsonTypeInfoKind.Object)
                {
                    throw new InvalidOperationException($"Page props must be a dictionary or an object with properties, not '{info.Type}'.");
                }

                info.OnSerializing?.Invoke(props);
                var members = RentObjectEntries(props, info, out var count);
                try
                {
                    for (var i = 0; i < count; i++)
                    {
                        Set(members[i].Key, members[i].Value, members[i].Type, shared: false);
                    }
                }
                finally
                {
                    ReturnObjectEntries(members, count);
                }

                break;
        }

        sharedProps = sharedCount;
        dotKeys = hasDotKey;
        return entries;
    }

    // Top-level "a.b.c" keys become nested objects (Laravel unpackDotProps). Nested dictionaries are copied, never mutated.
    private static List<PropEntry> UnpackDotProps(List<PropEntry> entries)
    {
        var result = new List<PropEntry>(entries.Count);
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!entry.Key.Contains('.', StringComparison.Ordinal))
            {
                positions[entry.Key] = result.Count;
                result.Add(entry);
            }
        }

        var owned = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var entry in entries)
        {
            if (!entry.Key.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            var segments = entry.Key.Split('.');
            if (!positions.TryGetValue(segments[0], out var position))
            {
                positions[segments[0]] = position = result.Count;
                result.Add(new PropEntry(segments[0], null, typeof(object)));
            }

            var top = ToTraversable(result[position].Value, owned);
            result[position] = new PropEntry(segments[0], top, typeof(object));
            Assign(top, segments, 1, entry.Value, owned);
        }

        return result;
    }

    // Laravel's Arr::set from segments[start], copying the objects on the way. Below a lazy object the rest of the path is
    // replayed once that object is resolved.
    private static void Assign(object container, string[] segments, int start, object? value, HashSet<object> owned)
    {
        for (var i = start; ; i++)
        {
            if (container is LazyDotObject lazy)
            {
                lazy.Add(segments, i, value);
                return;
            }

            var parent = (InertiaProps)container;
            if (i == segments.Length - 1)
            {
                parent[segments[i]] = value;
                return;
            }

            parent.TryGetValue(segments[i], out var existing);
            container = ToTraversable(existing, owned);
            parent[segments[i]] = container;
        }
    }

    // Laravel's ensurePathIsTraversable: owned objects are reused, plain lazy props (closures there) become lazy objects that
    // resolve only when their path is part of the response (deviation 7), dictionaries are copied, anything else is replaced by an
    // empty object.
    private static object ToTraversable(object? value, HashSet<object> owned)
    {
        if (value is LazyDotObject || (value is InertiaProps props && owned.Contains(props)))
        {
            return value;
        }

        return value is InertiaProp { Load: PropLoad.Eager, IsAlways: false, IsOnce: false, IsMerge: false, ShouldRescue: false } plain
            ? new LazyDotObject(plain)
            : CopyObject(value, owned);
    }

    private static InertiaProps CopyObject(object? value, HashSet<object> owned)
    {
        var copy = new InertiaProps();
        owned.Add(copy);
        switch (value)
        {
            case IReadOnlyDictionary<string, object?> dictionary:
                foreach (var (key, item) in dictionary)
                {
                    copy[key] = item;
                }

                break;
            case IDictionary<string, object?> dictionary:
                foreach (var (key, item) in dictionary)
                {
                    copy[key] = item;
                }

                break;
        }

        return copy;
    }

    /// <summary>
    /// A plain lazy prop with dot keys below it. <see cref="PropsWriter"/> resolves <see cref="Source"/> only when this path is part of
    /// the response, then <see cref="Apply"/> sets the dot keys on a copy of its value, in the order they were unpacked.
    /// </summary>
    internal sealed class LazyDotObject(InertiaProp source)
    {
        private readonly List<(string[] Segments, int Start, object? Value)> _assignments = [];

        public InertiaProp Source { get; } = source;

        public void Add(string[] segments, int start, object? value) => _assignments.Add((segments, start, value));

        public InertiaProps Apply(object? value)
        {
            var owned = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var root = CopyObject(value, owned);
            foreach (var (segments, start, item) in _assignments)
            {
                Assign(root, segments, start, item, owned);
            }

            return root;
        }
    }

    /// <summary>
    /// A cached prop's store entry (<see cref="CacheEntry"/>), and the prop as the walk writes it. The JSON is HTML-safe, so the same bytes
    /// fit JSON responses and the page <c>&lt;script&gt;</c>.
    /// </summary>
    internal byte[] CreateCacheEntry(Type valueType, object? value, ScrollMetadata? scroll, PropCaching caching, ReadOnlySpan<char> path, out CachedProp prop)
    {
        if (value is InertiaProp)
        {
            throw new InvalidOperationException($"The cached prop at '{new string(path)}' resolved to an InertiaProp. Cache() needs a plain value.");
        }

        using var buffer = new PooledBufferWriter(256);
        var scrollLength = CacheEntry.BeginProp(buffer, scroll);
        using (var writer = new Utf8JsonWriter(buffer, _htmlWriterOptions))
        {
            if (value is null or JsonElement or JsonNode || valueType == typeof(object))
            {
                WritePlainValue(writer, value);
            }
            else
            {
                JsonSerializer.Serialize(writer, value, SerializerOptions.GetTypeInfo(valueType));
            }
        }

        var (freshUntil, expiresAt) = Caching.Times(caching.Duration, caching.Expiration);
        return CacheEntry.EndProp(buffer, scroll, scrollLength, freshUntil, expiresAt, out prop);
    }

    internal readonly record struct RescuedCachedProp(Exception Exception);

    // The claim lasts through storage, so a parallel loader publishes its value before another request can load the same key.
    internal async ValueTask<object?> LoadCachedPropAsync(InertiaProp prop, CancellationToken cancellationToken)
    {
        var caching = prop.Caching!;
        TaskCompletionSource<byte[]?> completion;
        while (!Caching.TryBeginFill(caching.StoreKey, out completion))
        {
            var shared = await completion.Task.WaitAsync(cancellationToken);
            if (shared is not null && CacheEntry.TryReadProp(shared, TimeProvider.GetUtcNow().ToUnixTimeMilliseconds(), out var filled)) return filled;
            if (await Caching.GetPropAsync(caching, cancellationToken) is { } hit) return hit;
        }

        try
        {
            // Another fill may have finished between the caller's lookup and this claim.
            if (await Caching.GetPropAsync(caching, cancellationToken) is { } hit) return hit;
            cancellationToken.ThrowIfCancellationRequested();
            object? value;
            try
            {
                value = await prop.ResolveAsync(cancellationToken);
            }
            catch (Exception ex) when (prop.ShouldRescue && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                return new RescuedCachedProp(ex);
            }

            var entry = CreateCacheEntry(prop.ValueType, value, PropsWriter.ScrollOf(prop, value), caching, caching.Key, out var stored);
            await Caching.SetAsync(caching.StoreKey, entry, caching.Expiration, caching.Tags, cancellationToken);
            completion.SetResult(entry);
            return stored;
        }
        finally
        {
            Caching.EndFill(caching.StoreKey, completion);
        }
    }

    internal void WritePlainValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement element:
                WriteElement(writer, element);
                break;
            case JsonNode node:
                WriteNode(writer, node);
                break;
            default:
                JsonSerializer.Serialize(writer, value, SerializerOptions.GetTypeInfo(value.GetType()));
                break;
        }
    }

    internal void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        if (Options.PreserveBigIntegers) BigIntegerConverters.WriteElement(writer, element);
        else element.WriteTo(writer);
    }

    internal void WriteNode(Utf8JsonWriter writer, JsonNode node)
    {
        if (Options.PreserveBigIntegers) BigIntegerConverters.WriteNode(writer, node, SerializerOptions);
        else node.WriteTo(writer, SerializerOptions);
    }

    internal (JsonTypeInfo Info, object? Discriminator, string? DiscriminatorName) ObjectContract(object value, JsonTypeInfo info)
    {
        var runtime = value.GetType();
        if (runtime == info.Type || info.PolymorphismOptions is not { } polymorphism) return (info, null, null);
        JsonDerivedType? selected = null;
        foreach (var derived in polymorphism.DerivedTypes)
        {
            if (derived.DerivedType == runtime)
                return (SerializerOptions.GetTypeInfo(runtime), derived.TypeDiscriminator, polymorphism.TypeDiscriminatorPropertyName);
        }
        switch (polymorphism.UnknownDerivedTypeHandling)
        {
            case JsonUnknownDerivedTypeHandling.FailSerialization:
                throw new NotSupportedException($"Runtime type '{runtime}' is not registered for polymorphic type '{info.Type}'.");
            case JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor:
                foreach (var candidate in polymorphism.DerivedTypes)
                {
                    if (!candidate.DerivedType.IsAssignableFrom(runtime)) continue;
                    if (polymorphism.DerivedTypes.Any(other => other.DerivedType != candidate.DerivedType
                        && candidate.DerivedType.IsAssignableFrom(other.DerivedType) && other.DerivedType.IsAssignableFrom(runtime))) continue;
                    if (selected is not null) throw new NotSupportedException($"Runtime type '{runtime}' has ambiguous registered polymorphic ancestors.");
                    selected = candidate;
                }
                break;
        }
        return selected is { } ancestor
            ? (SerializerOptions.GetTypeInfo(ancestor.DerivedType), ancestor.TypeDiscriminator, polymorphism.TypeDiscriminatorPropertyName)
            : (info, null, null);
    }

    /// <summary>
    /// The JSON property names of a typed dictionary's keys, in enumeration order, as System.Text.Json writes them: string keys
    /// through <see cref="JsonSerializerOptions.DictionaryKeyPolicy"/>, other keys through their dictionary key converter.
    /// </summary>
    internal string[] DictionaryKeys(IDictionary dictionary, JsonTypeInfo info)
    {
        var keys = new string[dictionary.Count];
        var i = 0;
        if (info.KeyType == typeof(string))
        {
            var policy = SerializerOptions.DictionaryKeyPolicy;
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = (string)entry.Key;
                keys[i++] = policy?.ConvertName(key) ?? key;
            }

            return keys;
        }

        // Non-string keys (numbers, enums, Guids, ...) are formatted by the serializer itself: each key is written through a
        // one-entry dictionary of the same type, so converters, enum names and the key policy all match a plain serialization.
        var probe = info.CreateObject?.Invoke() as IDictionary;
        var defaultValue = info.ElementType is { IsValueType: true } valueType && Nullable.GetUnderlyingType(valueType) is null
            ? _defaultValues.GetOrAdd(valueType, CreateDefaultValue) : null;
        using var buffer = new PooledBufferWriter(256);
        using var writer = new Utf8JsonWriter(buffer);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (probe is null)
            {
                keys[i++] = Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                continue;
            }

            probe.Clear();
            probe.Add(entry.Key, defaultValue);
            buffer.Reset();
            writer.Reset(buffer);
            JsonSerializer.Serialize(writer, probe, info);
            writer.Flush();
            // The probe is {"key":value}: the second token is the key.
            var reader = new Utf8JsonReader(buffer.WrittenMemory.Span);
            reader.Read();
            reader.Read();
            keys[i++] = reader.GetString() ?? "";
        }

        return keys;
    }

    private bool ComputeContainsProps(Type type, HashSet<Type> visiting)
    {
        // object: the slot may hold a prop at runtime (object[], List<object>, an object property), so it is walked and
        // each value is then handled by its runtime type.
        if (type == typeof(object)
            || typeof(InertiaProp).IsAssignableFrom(type)
            || typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(type)
            || typeof(IDictionary<string, object?>).IsAssignableFrom(type))
        {
            return true;
        }

        if (type == typeof(string) || type.IsPrimitive || !visiting.Add(type))
        {
            return false;
        }

        JsonTypeInfo info;
        try
        {
            info = SerializerOptions.GetTypeInfo(type);
        }
        catch (NotSupportedException)
        {
            return false;
        }

        return info.Kind switch
        {
            JsonTypeInfoKind.Object => info.Properties.Any(p => ComputeContainsProps(p.PropertyType, visiting))
                || (info.PolymorphismOptions?.DerivedTypes.Any(d => ComputeContainsProps(d.DerivedType, visiting)) ?? false),
            JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary => info.ElementType is { } elementType && ComputeContainsProps(elementType, visiting),
            _ => false,
        };
    }

    // JSON safe to embed in <script>: '<', '>', '&', '/', quotes, '+', '`' and U+2028/2029 are \u-escaped, so "</script>" and "<!--" cannot appear.
    private static JavaScriptEncoder CreateHtmlSafeEncoder()
    {
        var settings = new TextEncoderSettings(UnicodeRanges.All);
        settings.ForbidCharacters('<', '>', '&', '/', '\'', '"', '+', '`', (char)0x2028, (char)0x2029);
        return JavaScriptEncoder.Create(settings);
    }
}
