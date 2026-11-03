using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Inertia.Net;

/// <summary>Writes the broadcast payload <c>{"__inertia":{"event":"...","props":{...}}}</c>.</summary>
internal static class LiveEnvelope
{
    public static JsonElement Build(LiveEvent @event, Action<Utf8JsonWriter>? writeProps)
    {
        using var buffer = new PooledBufferWriter(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("__inertia");
            writer.WriteString("event", @event.Name);
            if (writeProps is not null)
            {
                writer.WritePropertyName("props");
                writeProps(writer);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    public static void ThrowIfInvalidProps(object? props, JsonTypeInfo info)
    {
        if (props is InertiaProps)
        {
            throw new ArgumentException("Published props are plain values keyed by prop path; use a dictionary or an object, not InertiaProps.", nameof(props));
        }

        if (props is not null && info.Kind is not (JsonTypeInfoKind.Object or JsonTypeInfoKind.Dictionary))
        {
            throw new ArgumentException($"Published props must serialize to a JSON object keyed by prop path, not '{info.Type}'.", nameof(props));
        }
    }
}
