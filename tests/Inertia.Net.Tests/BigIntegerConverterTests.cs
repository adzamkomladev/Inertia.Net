using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Inertia.Net.Tests;

public sealed class BigIntegerConverterTests
{
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions();
        BigIntegerConverters.AddTo(options.Converters);
        return options;
    }

    [Fact]
    public void Multi_segment_integer_values_preserve_every_supported_integer_type()
    {
        Assert.Equal(long.MinValue, Read<long>("-9223372036854775808", 7));
        Assert.Equal(ulong.MaxValue, Read<ulong>("18446744073709551615", 7));
        Assert.Equal(Int128.MinValue, Read<Int128>("-170141183460469231731687303715884105728", 7));
        Assert.Equal(UInt128.MaxValue, Read<UInt128>("340282366920938463463374607431768211455", 7));
        const string big = "1000000000000000000000000000000000000000000000000000000000000";
        Assert.Equal(BigInteger.Parse(big, CultureInfo.InvariantCulture), Read<BigInteger>(big, 7));
    }

    [Fact]
    public void Multi_segment_dictionary_keys_preserve_every_supported_integer_type()
    {
        AssertKey(long.MinValue);
        AssertKey(ulong.MaxValue);
        AssertKey(Int128.MinValue);
        AssertKey(UInt128.MaxValue);
        AssertKey(BigInteger.Pow(10, 60));

        static void AssertKey<T>(T expected) where T : notnull, IFormattable
        {
            var json = $$"""{"{{expected.ToString(null, CultureInfo.InvariantCulture)}}":7}""";
            var value = Read<Dictionary<T, int>>(json, 6);
            Assert.Equal(7, value[expected]);
            Assert.Equal(json, JsonSerializer.Serialize(value, Options()));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Escaped_numeric_dictionary_keys_are_decoded_for_contiguous_and_segmented_names(int split)
    {
        const string json = """{"\u0031\u0032":7}""";
        Assert.Equal(7, Read<Dictionary<long, int>>(json, split)[12L]);
        Assert.Equal(7, Read<Dictionary<ulong, int>>(json, split)[12UL]);
        Assert.Equal(7, Read<Dictionary<Int128, int>>(json, split)[12]);
        Assert.Equal(7, Read<Dictionary<UInt128, int>>(json, split)[12]);
        Assert.Equal(7, Read<Dictionary<BigInteger, int>>(json, split)[12]);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0", "-0")]
    [InlineData("9007199254740991", "9007199254740991")]
    [InlineData("-9007199254740991", "-9007199254740991")]
    [InlineData("9007199254740992", "{\"$bigint\":\"9007199254740992\"}")]
    [InlineData("-9007199254740992", "{\"$bigint\":\"-9007199254740992\"}")]
    [InlineData("18446744073709551615", "{\"$bigint\":\"18446744073709551615\"}")]
    [InlineData("340282366920938463463374607431768211455", "{\"$bigint\":\"340282366920938463463374607431768211455\"}")]
    [InlineData("1.00", "1.00")]
    [InlineData("1e20", "1e20")]
    [InlineData("1E+20", "1E+20")]
    [InlineData("9007199254740993.0", "9007199254740993.0")]
    public void Dom_integer_boundaries_and_noninteger_token_spelling_are_preserved(string input, string expected)
    {
        using var document = JsonDocument.Parse(input);
        var options = Options();
        Assert.Equal(expected, JsonSerializer.Serialize(document.RootElement, options));
        Assert.Equal(expected, JsonSerializer.Serialize(JsonNode.Parse(input), options));
    }

    private static T Read<T>(string json, int split)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var head = new Segment(bytes.AsMemory(0, split));
        var tail = head.Append(bytes.AsMemory(split));
        var sequence = new ReadOnlySequence<byte>(head, 0, tail, tail.Memory.Length);
        var reader = new Utf8JsonReader(sequence, isFinalBlock: true, state: default);
        return JsonSerializer.Deserialize<T>(ref reader, Options())!;
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }

    }
}
