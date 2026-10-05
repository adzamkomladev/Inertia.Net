using System.Globalization;
using System.Numerics;
using System.Text.Json;
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
