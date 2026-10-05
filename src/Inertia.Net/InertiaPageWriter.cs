using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
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

    private readonly ConcurrentDictionary<Type, bool> _containsProps = new();
    private readonly JsonWriterOptions _jsonWriterOptions;
    private readonly JsonWriterOptions _htmlWriterOptions;

    public InertiaPageWriter(IOptions<InertiaOptions> options, IOptions<HttpJsonOptions> jsonOptions, TimeProvider timeProvider, ILogger<InertiaPageWriter> logger)
    {
        Options = options.Value;
        TimeProvider = timeProvider;
        Logger = logger;

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
            var sharedPropKeys = Options.ExposeSharedPropKeys ? new List<string>() : null;
            var entries = await BuildPropsAsync(httpContext, feature, props, request, sharedPropKeys);

            using var writer = new Utf8JsonWriter(buffer, htmlSafe ? _htmlWriterOptions : _jsonWriterOptions);
            using var propsWriter = new PropsWriter(this, writer, request, component, httpContext.RequestAborted);

            writer.WriteStartObject();
            writer.WriteString("component", component);
            writer.WritePropertyName("props");
            await propsWriter.WritePropsAsync(entries);
            writer.WriteString("url", GetUrl(httpContext.Request));
            writer.WriteString("version", GetVersion(httpContext));
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
        if (property.Get is null || property.IsExtensionData)
        {
            return false;
        }

        value = property.Get(target);
        if (property.ShouldSerialize is { } shouldSerialize && !shouldSerialize(target, value))
        {
            return false;
        }

        return value is not null
            || SerializerOptions.DefaultIgnoreCondition is not (JsonIgnoreCondition.WhenWritingNull or JsonIgnoreCondition.WhenWritingDefault);
    }

    private static string GetUrl(HttpRequest request)
    {
        var url = string.Concat(request.PathBase.ToUriComponent(), request.Path.ToUriComponent(), request.QueryString.ToUriComponent());
        return url.Length == 0 ? "/" : url;
    }

    // ponytail: Phase 2a replaces this with the precomputed VersionProvider.
    private string GetVersion(HttpContext httpContext) =>
        Options.VersionResolver?.Invoke(httpContext) ?? Options.Version ?? "";

    // errors (always), then option shares, then request shares, then page props; later keys override earlier ones in place.
    private async ValueTask<List<PropEntry>> BuildPropsAsync(HttpContext httpContext, InertiaFeature? feature, object? props, InertiaRequest request, List<string>? sharedPropKeys)
    {
        var entries = new List<PropEntry>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        var hasDotKey = false;

        void Set(string key, object? value, Type type, bool shared)
        {
            if (positions.TryGetValue(key, out var position))
            {
                entries[position] = new PropEntry(key, value, type);
            }
            else
            {
                positions[key] = entries.Count;
                entries.Add(new PropEntry(key, value, type));
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

        Set("errors", Inertia.Always(ErrorsResolver.Resolve(feature?.Errors, Options.WithAllErrors, request.ErrorBag)), typeof(object), shared: true);

        foreach (var (key, value) in Options.SharedProps)
        {
            Set(key, value is Func<HttpContext, object?> factory ? factory(httpContext) : value, typeof(object), shared: true);
        }

        foreach (var (key, value) in feature?.SharedProps ?? [])
        {
            Set(key, value, typeof(object), shared: true);
        }

        switch (props)
        {
            case null:
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

                foreach (var property in info.Properties)
                {
                    if (TryGetPropertyValue(property, props, out var value))
                    {
                        Set(property.Name, value, property.PropertyType, shared: false);
                    }
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
        if (typeof(InertiaProp).IsAssignableFrom(type)
            || typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(type)
            || typeof(IDictionary<string, object?>).IsAssignableFrom(type))
        {
            return true;
        }

        if (type == typeof(object) || type == typeof(string) || type.IsPrimitive || !visiting.Add(type))
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
