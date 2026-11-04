namespace Inertia.Net.Tests;

public class CursorTests
{
    [Fact]
    public void Round_trips_supported_values()
    {
        RoundTrip(123); RoundTrip(123L); RoundTrip(Guid.NewGuid()); RoundTrip("é+/=");
        RoundTrip(DateTime.SpecifyKind(new DateTime(2025, 3, 2), DateTimeKind.Utc));
        RoundTrip(new DateTimeOffset(2025, 3, 2, 4, 5, 6, TimeSpan.FromHours(2)));
        RoundTrip(123.4567890123456789m); RoundTrip(1.25d); RoundTrip(true);
    }

    private static void RoundTrip<T>(T value)
    {
        var original = new Cursor<T>(CursorDirection.Before, value);
        var encoded = CursorCodec.Encode(original);
        Assert.Matches("^[A-Za-z0-9_-]+$", encoded);
        Assert.Equal(original, CursorCodec.Decode<T>(encoded));
    }

    [Theory]
    [InlineData("%%%")]
    [InlineData("a")]
    [InlineData("e30")]
    [InlineData("W10")]
    public void Malformed_input_is_ignored(string input) => Assert.Null(CursorCodec.Decode<int>(input));

    [Fact]
    public void Unsupported_type_is_rejected() => Assert.Throws<NotSupportedException>(() => CursorCodec.ValidateType<float>());
    [Fact]
    public void Date_time_kinds_offsets_and_decimal_precision_survive()
    {
        foreach (var kind in new[] { DateTimeKind.Utc, DateTimeKind.Local, DateTimeKind.Unspecified })
        {
            var value = DateTime.SpecifyKind(new DateTime(2026, 11, 4, 13, 14, 15).AddTicks(1234567), kind);
            var result = CursorCodec.Decode<DateTime>(CursorCodec.Encode(new Cursor<DateTime>(CursorDirection.After, value)))!.Value;
            Assert.Equal(value, result.Value);
            Assert.Equal(kind, result.Value.Kind);
            Assert.Equal(CursorDirection.After, result.Direction);
        }
        RoundTrip(new DateTimeOffset(2026, 11, 4, 13, 14, 15, TimeSpan.FromHours(-7)).AddTicks(1234567));
        RoundTrip(decimal.MaxValue);
        RoundTrip(decimal.MinValue);
        RoundTrip(long.MinValue);
        RoundTrip(int.MaxValue);
        RoundTrip(false);
        RoundTrip("");
        RoundTrip(new string('é', 10000));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[\"a\"]")]
    [InlineData("[\"a\",1,2]")]
    [InlineData("[\"x\",1]")]
    [InlineData("[1,1]")]
    [InlineData("[\"a\",\"one\"]")]
    [InlineData("[\"a\",null]")]
    [InlineData("[\"a\",1.5]")]
    [InlineData("[\"a\",999999999999]")]
    [InlineData("[\"a\",true]")]
    [InlineData("[\"a\",1] [\"a\",2]")]
    public void Foreign_or_malformed_payload_is_ignored(string json)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Null(CursorCodec.Decode<int>(encoded));
    }

    [Fact]
    public void Unsupported_type_errors_name_the_type()
    {
        Assert.Contains("System.Single", Assert.Throws<NotSupportedException>(() => CursorCodec.Encode(new Cursor<float>(CursorDirection.After, 1))).Message);
        Assert.Contains("System.Single", Assert.Throws<NotSupportedException>(() => CursorCodec.Decode<float>(null)).Message);
    }
    [Fact]
    public void Composite_cursor_retains_key_tie_breaker_and_direction()
    {
        var cursor = new Cursor<DateTimeOffset>(CursorDirection.Before, new DateTimeOffset(2026, 11, 4, 13, 14, 15, TimeSpan.FromHours(2)));
        var encoded = CursorCodec.Encode(cursor, long.MaxValue);
        Assert.Matches("^[A-Za-z0-9_-]+$", encoded);
        var decoded = CursorCodec.Decode<DateTimeOffset, long>(encoded)!.Value;
        Assert.Equal(cursor, decoded.Cursor);
        Assert.Equal(long.MaxValue, decoded.TieBreaker);
        Assert.Null(CursorCodec.Decode<DateTimeOffset>(encoded));
        Assert.Null(CursorCodec.Decode<DateTimeOffset, int>(encoded));
        Assert.Null(CursorCodec.Decode<DateTimeOffset, long>(CursorCodec.Encode(cursor)));
    }
    [Fact]
    public void Null_keys_are_rejected_with_guidance()
    {
        Assert.Contains("Keyset paging needs non-null keys", Assert.Throws<NotSupportedException>(() => CursorCodec.Encode(new Cursor<string>(CursorDirection.After, null!))).Message);
        Assert.Contains("System.Nullable", Assert.Throws<NotSupportedException>(() => CursorCodec.ValidateType<int?>()).Message);
    }
}
