using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Inertia.Net;

/// <summary>Converters that write integers beyond ±(2^53−1) as <c>{"$bigint":"digits"}</c> (option <c>PreserveBigIntegers</c>).</summary>
internal static class BigIntegerConverters
{
    public static void AddTo(IList<JsonConverter> converters)
    {
        converters.Add(new Converter<long>());
        converters.Add(new Converter<ulong>());
        converters.Add(new Converter<Int128>());
        converters.Add(new Converter<UInt128>());
        converters.Add(new Converter<BigInteger>());
        converters.Add(new ElementConverter());
        converters.Add(new NodeConverter<JsonNode>());
        converters.Add(new NodeConverter<JsonObject>());
        converters.Add(new NodeConverter<JsonArray>());
        converters.Add(new NodeConverter<JsonValue>());
    }

    internal static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteElement(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                var text = element.GetRawText();
                if (BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                    && BigInteger.Abs(number) > 9007199254740991L)
                {
                    writer.WriteStartObject();
                    writer.WriteString("$bigint", number.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndObject();
                }
                else element.WriteTo(writer);
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    internal static void WriteNode(Utf8JsonWriter writer, JsonNode node, JsonSerializerOptions options)
    {
        switch (node)
        {
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var (key, value) in obj)
                {
                    writer.WritePropertyName(key);
                    if (value is null) writer.WriteNullValue();
                    else WriteNode(writer, value, options);
                }
                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var value in array)
                {
                    if (value is null) writer.WriteNullValue();
                    else WriteNode(writer, value, options);
                }
                writer.WriteEndArray();
                break;
            case JsonValue value when value.TryGetValue<JsonElement>(out var element):
                WriteElement(writer, element);
                break;
            default:
                node.WriteTo(writer, options);
                break;
        }
    }

    private sealed class ElementConverter : JsonConverter<JsonElement>
    {
        public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => JsonElement.ParseValue(ref reader);
        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) => WriteElement(writer, value);
    }

    private sealed class NodeConverter<T> : JsonConverter<T> where T : JsonNode
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => (T?)JsonNode.Parse(ref reader);
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => WriteNode(writer, value, options);
    }

    private sealed class Converter<T> : JsonConverter<T>
        where T : IBinaryInteger<T>
    {
        private static readonly T MaxSafe = T.CreateTruncating(9007199254740991L);
        private static readonly T MinSafe = T.Zero - MaxSafe; // only compared for negative (signed) values

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            T.Parse(reader.ValueSpan, CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value > MaxSafe || (T.IsNegative(value) && value < MinSafe))
            {
                writer.WriteStartObject();
                writer.WriteString("$bigint", value.ToString(null, CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNumberValue(long.CreateTruncating(value));
            }
        }

        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            T.Parse(reader.ValueSpan, CultureInfo.InvariantCulture);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.ToString(null, CultureInfo.InvariantCulture));
    }
}
