using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Inertia.Net;

/// <summary>Converters that write integers beyond ±(2^53−1) as <c>{"$bigint":"digits"}</c> (option <c>PreserveBigIntegers</c>).</summary>
internal static class BigIntegerConverters
{
    private const long MaxSafeInteger = 9007199254740991L;

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
                    if (IsVerbatim(property.Value))
                    {
                        property.WriteTo(writer); // the name as it is, without a string for it
                        continue;
                    }

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
            case JsonValueKind.Number when IsUnsafeInteger(element, out var number):
                writer.WriteStartObject();
                writer.WriteString("$bigint", number.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    internal static void WriteUnwrapped(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.GetPropertyCount() == 1 && element.TryGetProperty("$bigint", out var marker)
                && marker.ValueKind == JsonValueKind.String
                && BigInteger.TryParse(marker.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            {
                writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture));
                return;
            }
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                WriteUnwrapped(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var value in element.EnumerateArray()) WriteUnwrapped(writer, value);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }

    // Whether WriteElement writes the value as it is: anything but an object, an array or an integer beyond ±(2^53−1).
    private static bool IsVerbatim(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object or JsonValueKind.Array => false,
        JsonValueKind.Number => !IsUnsafeInteger(element, out _),
        _ => true,
    };

    private static bool IsUnsafeInteger(JsonElement element, out BigInteger number)
    {
        number = default;
        if (element.TryGetInt64(out var safe) && safe >= -MaxSafeInteger && safe <= MaxSafeInteger)
        {
            return false;
        }

        // A fraction or an exponent is not an integer token: skip the parse (and the raw text string it used to need).
        var raw = System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(element);
        return raw.IndexOfAny((byte)'.', (byte)'e', (byte)'E') < 0
            && TryParse(raw, out number)
            && BigInteger.Abs(number) > MaxSafeInteger;
    }

    private static bool TryParse<T>(ReadOnlySpan<byte> utf8, out T number) where T : INumberBase<T> =>
        T.TryParse(utf8, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number!);

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
        private static readonly T MaxSafe = T.CreateTruncating(MaxSafeInteger);
        private static readonly T MinSafe = T.Zero - MaxSafe; // only compared for negative (signed) values

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadNumber(ref reader);

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
            ReadNumber(ref reader);

        private static T ReadNumber(ref Utf8JsonReader reader) => reader.ValueIsEscaped
            ? T.Parse(reader.GetString()!, CultureInfo.InvariantCulture)
            : T.Parse(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan, CultureInfo.InvariantCulture);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.ToString(null, CultureInfo.InvariantCulture));
    }
}
