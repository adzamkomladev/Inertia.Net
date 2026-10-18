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
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Inertia.Net;

/// <summary>Writes the page object (protocol §1) into a pooled buffer. Singleton; holds no per-request state.</summary>
internal sealed class InertiaPageWriter
{
    private static readonly JavaScriptEncoder HtmlSafeEncoder = CreateHtmlSafeEncoder();

    // The errors prop when there are none (the common case). Never mutated: the props walk only reads it.
    private static readonly InertiaProp NoErrors = Inertia.Always(new InertiaProps());

    private readonly ConcurrentDictionary<Type, bool> _containsProps = new();
    private readonly ConcurrentDictionary<(JsonPropertyInfo Property, JsonNumberHandling? Handling), JsonTypeInfo> _propertySerializers = new();
    private readonly ConcurrentDictionary<Type, object> _defaultValues = new();
    private readonly JsonWriterOptions _jsonWriterOptions;
    private readonly JsonWriterOptions _htmlWriterOptions;

    private readonly VersionProvider _versionProvider;

    public InertiaPageWriter(IOptions<InertiaOptions> options, IOptions<HttpJsonOptions> jsonOptions, TimeProvider timeProvider, ILogger<InertiaPageWriter> logger, VersionProvider versionProvider)
    {
        Options = options.Value;
        TimeProvider = timeProvider;
        Logger = logger;
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
    }

    public InertiaOptions Options { get; }

    public JsonSerializerOptions SerializerOptions { get; }

    public TimeProvider TimeProvider { get; }

    public ILogger Logger { get; }

    /// <summary>
    /// Resolves the props and writes the whole page object. With <paramref name="htmlSafe"/> the JSON can be embedded
    /// in a <c>&lt;script&gt;</c> element as-is. The caller disposes the returned buffer.
    /// </summary>
    public async ValueTask<PooledBufferWriter> WritePageAsync(HttpContext httpContext, string component, object? props, InertiaRequest request, bool htmlSafe)
    {
        var buffer = new PooledBufferWriter();
        try
        {
            httpContext.RequestAborted.ThrowIfCancellationRequested();
            var rootInfo = props is not null and not IEnumerable<KeyValuePair<string, object?>>
                ? SerializerOptions.GetTypeInfo(props.GetType()) : null;
            var feature = httpContext.Features.Get<InertiaFeature>();
            var sharedPropKeys = Options.ExposeSharedPropKeys ? new List<string>(Options.SharedProps.Count + 1 + (feature?.SharedProps?.Count ?? 0)) : null;
            var entries = BuildProps(httpContext, feature, props, request, sharedPropKeys, out var hasDotKey);
            if (hasDotKey)
            {
                entries = await UnpackDotPropsAsync(entries, httpContext.RequestAborted);
            }

            using var writer = new Utf8JsonWriter(buffer, htmlSafe ? _htmlWriterOptions : _jsonWriterOptions);
            using var propsWriter = new PropsWriter(this, writer, request, component, httpContext.RequestAborted);

            writer.WriteStartObject();
            writer.WriteString("component", component);
            writer.WritePropertyName("props");
            await propsWriter.WritePropsAsync(entries);
            rootInfo?.OnSerialized?.Invoke(props!);
            feature = httpContext.Features.Get<InertiaFeature>();
            writer.WriteString("url", GetUrl(httpContext.Request));
            writer.WriteString("version", _versionProvider.GetVersion(httpContext));
            propsWriter.WriteMetadata(sharedPropKeys);

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
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
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
        _propertySerializers.GetOrAdd((property, handling), key =>
        {
            var member = key.Property;
            var options = new JsonSerializerOptions(SerializerOptions)
            {
                NumberHandling = key.Handling ?? SerializerOptions.NumberHandling,
            };
            if (member.CustomConverter is { } converter)
            {
                options.Converters.Insert(0, converter);
            }
            options.MakeReadOnly();
            return options.GetTypeInfo(member.PropertyType);
        });

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

    private static string GetUrl(HttpRequest request)
    {
        var url = string.Concat(request.PathBase.ToUriComponent(), request.Path.ToUriComponent(), request.QueryString.ToUriComponent());
        return url.Length == 0 ? "/" : url;
    }

    // errors (always), then option shares, then request shares, then page props; later keys override earlier ones in place.
    // Synchronous, so the Set local function captures its locals in a stack struct instead of a heap closure.
    private List<PropEntry> BuildProps(HttpContext httpContext, InertiaFeature? feature, object? props, InertiaRequest request, List<string>? sharedPropKeys, out bool dotKeys)
    {
        var propCount = props switch
        {
            ICollection<KeyValuePair<string, object?>> collection => collection.Count,
            IReadOnlyCollection<KeyValuePair<string, object?>> collection => collection.Count,
            _ => 16,
        };
        var sharedCapacity = 1 + Options.SharedProps.Count + (feature?.SharedProps?.Count ?? 0);
        var entries = new List<PropEntry>(sharedCapacity + propCount);

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

            var dot = key.IndexOf('.', StringComparison.Ordinal);
            hasDotKey |= dot >= 0;
            if (shared && sharedPropKeys is not null)
            {
                var topLevel = dot >= 0 ? key[..dot] : key;
                if (!sharedPropKeys.Contains(topLevel))
                {
                    sharedPropKeys.Add(topLevel);
                }
            }
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

        dotKeys = hasDotKey;
        return entries;
    }

    // Top-level "a.b.c" keys become nested objects (Laravel unpackDotProps). Nested dictionaries are copied, never mutated.
    private static async ValueTask<List<PropEntry>> UnpackDotPropsAsync(List<PropEntry> entries, CancellationToken cancellationToken)
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
            InertiaProps parent;
            if (positions.TryGetValue(segments[0], out var position))
            {
                parent = await ToOwnedObjectAsync(result[position].Value, owned, cancellationToken);
                result[position] = new PropEntry(segments[0], parent, typeof(object));
            }
            else
            {
                parent = await ToOwnedObjectAsync(null, owned, cancellationToken);
                positions[segments[0]] = result.Count;
                result.Add(new PropEntry(segments[0], parent, typeof(object)));
            }

            for (var i = 1; i < segments.Length - 1; i++)
            {
                parent.TryGetValue(segments[i], out var existing);
                var child = await ToOwnedObjectAsync(existing, owned, cancellationToken);
                parent[segments[i]] = child;
                parent = child;
            }

            parent[segments[^1]] = entry.Value;
        }

        return result;
    }

    // Like Laravel's ensurePathIsTraversable + Arr::set: plain lazy props are resolved (they are closures there),
    // dictionaries are copied, anything else is replaced by an empty object.
    private static async ValueTask<InertiaProps> ToOwnedObjectAsync(object? value, HashSet<object> owned, CancellationToken cancellationToken)
    {
        if (value is InertiaProps props && owned.Contains(props))
        {
            return props;
        }

        if (value is InertiaProp { Load: PropLoad.Eager, IsAlways: false, IsOnce: false, IsMerge: false, ShouldRescue: false } plain)
        {
            cancellationToken.ThrowIfCancellationRequested();
            value = await plain.ResolveAsync(cancellationToken);
        }

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
        foreach (DictionaryEntry entry in dictionary)
        {
            if (probe is null)
            {
                keys[i++] = Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                continue;
            }

            probe.Clear();
            probe.Add(entry.Key, defaultValue);
            var reader = new Utf8JsonReader(JsonSerializer.SerializeToUtf8Bytes(probe, info));
            while (reader.Read() && reader.TokenType != JsonTokenType.PropertyName)
            {
            }

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
