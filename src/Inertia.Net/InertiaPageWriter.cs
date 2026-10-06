using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<JsonPropertyInfo, JsonTypeInfo> _propertySerializers = new();
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

        serializerOptions.MakeReadOnly();
        SerializerOptions = serializerOptions;
        _jsonWriterOptions = new JsonWriterOptions { Encoder = serializerOptions.Encoder, Indented = serializerOptions.WriteIndented };
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
            var feature = httpContext.Features.Get<InertiaFeature>();
            var sharedPropKeys = Options.ExposeSharedPropKeys ? new List<string>(Options.SharedProps.Count + 1 + (feature?.SharedProps?.Count ?? 0)) : null;
            var entries = await BuildPropsAsync(httpContext, feature, props, request, sharedPropKeys);

            using var writer = new Utf8JsonWriter(buffer, htmlSafe ? _htmlWriterOptions : _jsonWriterOptions);
            using var propsWriter = new PropsWriter(this, writer, request, component, httpContext.RequestAborted);

            writer.WriteStartObject();
            writer.WriteString("component", component);
            writer.WritePropertyName("props");
            await propsWriter.WritePropsAsync(entries);
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

        return property.IsExtensionData || value is not null
            || SerializerOptions.DefaultIgnoreCondition is not (JsonIgnoreCondition.WhenWritingNull or JsonIgnoreCondition.WhenWritingDefault);
    }

    // Keep the member's converter/number handling using the application's existing type resolver.
    internal readonly record struct PropertyValue(JsonPropertyInfo Property, object? Value);

    public JsonTypeInfo PropertyTypeInfo(JsonPropertyInfo property) =>
        _propertySerializers.GetOrAdd(property, member =>
        {
            var options = new JsonSerializerOptions(SerializerOptions)
            {
                NumberHandling = member.NumberHandling ?? SerializerOptions.NumberHandling,
            };
            if (member.CustomConverter is { } converter)
            {
                options.Converters.Insert(0, converter);
            }
            options.MakeReadOnly();
            return options.GetTypeInfo(member.PropertyType);
        });

    public IEnumerable<PropEntry> ObjectEntries(object target, JsonTypeInfo info)
    {
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
                    foreach (var entry in objects) yield return new(entry.Key, entry.Value, typeof(object));
                }
                else if (value is IEnumerable<KeyValuePair<string, JsonElement>> elements)
                {
                    foreach (var entry in elements) yield return new(entry.Key, entry.Value, typeof(JsonElement));
                }
                else if (value is JsonObject nodes)
                {
                    foreach (var entry in nodes) yield return new(entry.Key, entry.Value, typeof(JsonNode));
                }
            }
            else
            {
                yield return new(property.Name,
                    value is not InertiaProp && (property.CustomConverter is not null || property.NumberHandling is not null)
                        ? new PropertyValue(property, value) : value,
                    property.PropertyType);
            }
        }
    }

    private static string GetUrl(HttpRequest request)
    {
        var url = string.Concat(request.PathBase.ToUriComponent(), request.Path.ToUriComponent(), request.QueryString.ToUriComponent());
        return url.Length == 0 ? "/" : url;
    }

    // errors (always), then option shares, then request shares, then page props; later keys override earlier ones in place.
    private async ValueTask<List<PropEntry>> BuildPropsAsync(HttpContext httpContext, InertiaFeature? feature, object? props, InertiaRequest request, List<string>? sharedPropKeys)
    {
        // Sized up front: one allocation instead of a grow-and-copy chain.
        var entries = new List<PropEntry>(1 + Options.SharedProps.Count + (feature?.SharedProps?.Count ?? 0) + props switch
        {
            ICollection<KeyValuePair<string, object?>> collection => collection.Count,
            IReadOnlyCollection<KeyValuePair<string, object?>> collection => collection.Count,
            _ => 16,
        });

        // Page props come from one dictionary or object, so their keys are unique: a key can only collide with an earlier
        // errors/shared entry. Those come first, so a linear scan of that prefix replaces a key-to-position dictionary.
        // ponytail: O(shared x props) string compares; a dictionary pays off only with dozens of shared props.
        var sharedCount = 0;
        var hasDotKey = false;

        void Set(string key, object? value, Type type, bool shared)
        {
            var position = -1;
            for (var i = 0; i < sharedCount; i++)
            {
                if (string.Equals(entries[i].Key, key, StringComparison.Ordinal))
                {
                    position = i;
                    break;
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

        var errors = feature?.Errors is { Count: > 0 } bags ? Inertia.Always(ErrorsResolver.Resolve(bags, Options.WithAllErrors, request.ErrorBag)) : NoErrors;
        Set("errors", errors, typeof(object), shared: true);

        foreach (var (key, value) in Options.SharedProps)
        {
            Set(key, value is Func<HttpContext, object?> factory ? factory(httpContext) : value, typeof(object), shared: true);
        }

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

                foreach (var entry in ObjectEntries(props, info))
                {
                    Set(entry.Key, entry.Value, entry.Type, shared: false);
                }

                break;
        }

        return hasDotKey ? await UnpackDotPropsAsync(entries, httpContext.RequestAborted) : entries;
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

    private void WritePlainValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            default:
                JsonSerializer.Serialize(writer, value, SerializerOptions.GetTypeInfo(value.GetType()));
                break;
        }
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
            JsonTypeInfoKind.Object => info.Properties.Any(p => ComputeContainsProps(p.PropertyType, visiting)),
            JsonTypeInfoKind.Enumerable => info.ElementType is { } elementType && ComputeContainsProps(elementType, visiting),
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
