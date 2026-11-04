using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Inertia.Net;

/// <summary>The direction in which a cursor page is fetched.</summary>
public enum CursorDirection
{
    /// <summary>Fetch items after the cursor.</summary>
    After,
    /// <summary>Fetch items before the cursor.</summary>
    Before,
}

/// <summary>A decoded cursor and its direction.</summary>
public readonly record struct Cursor<TKey>(CursorDirection Direction, TKey Value);

internal static class CursorCodec
{
    internal static void ValidateType<T>()
    {
        var type = typeof(T);
        if (Nullable.GetUnderlyingType(type) is not null)
            throw new NotSupportedException($"Cursor key type '{type}' is not supported. Keyset paging needs non-null keys: order by a non-null column or coalesce the expression, and add a unique tie-breaker.");
        if (type != typeof(int) && type != typeof(long) && type != typeof(Guid) && type != typeof(string)
            && type != typeof(DateTime) && type != typeof(DateTimeOffset) && type != typeof(decimal)
            && type != typeof(double) && type != typeof(bool))
            throw new NotSupportedException($"Cursor key type '{type}' is not supported.");
    }

    internal static string Encode<T>(Cursor<T> cursor)
    {
        ValidateType<T>();
        using var buffer = new PooledBufferWriter(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteStart(writer, cursor.Direction);
            Write(writer, cursor.Value);
            writer.WriteEndArray();
        }
        return Base64Url.EncodeToString(buffer.WrittenMemory.Span);
    }

    internal static string Encode<TKey, TTie>(Cursor<TKey> cursor, TTie tieBreaker)
    {
        ValidateType<TKey>();
        ValidateType<TTie>();
        using var buffer = new PooledBufferWriter(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteStart(writer, cursor.Direction);
            Write(writer, cursor.Value);
            Write(writer, tieBreaker);
            writer.WriteEndArray();
        }
        return Base64Url.EncodeToString(buffer.WrittenMemory.Span);
    }

    private static void WriteStart(Utf8JsonWriter writer, CursorDirection direction)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(direction switch
        {
            CursorDirection.After => "a",
            CursorDirection.Before => "b",
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        });
    }

    private static void Write<T>(Utf8JsonWriter writer, T value)
    {
        if (typeof(T) == typeof(string) && Unsafe.As<T, string>(ref value) is null)
            throw new NotSupportedException("Keyset paging needs non-null keys: order by a non-null column or coalesce the expression, and add a unique tie-breaker.");
        // The type checks make the reinterpretations safe and let the JIT specialize without boxing.
        if (typeof(T) == typeof(int)) writer.WriteNumberValue(Unsafe.As<T, int>(ref value));
        else if (typeof(T) == typeof(long)) writer.WriteNumberValue(Unsafe.As<T, long>(ref value));
        else if (typeof(T) == typeof(Guid)) writer.WriteStringValue(Unsafe.As<T, Guid>(ref value));
        else if (typeof(T) == typeof(string)) writer.WriteStringValue(Unsafe.As<T, string>(ref value));
        else if (typeof(T) == typeof(DateTime)) writer.WriteStringValue(Unsafe.As<T, DateTime>(ref value));
        else if (typeof(T) == typeof(DateTimeOffset)) writer.WriteStringValue(Unsafe.As<T, DateTimeOffset>(ref value));
        else if (typeof(T) == typeof(decimal)) writer.WriteNumberValue(Unsafe.As<T, decimal>(ref value));
        else if (typeof(T) == typeof(double)) writer.WriteNumberValue(Unsafe.As<T, double>(ref value));
        else if (typeof(T) == typeof(bool)) writer.WriteBooleanValue(Unsafe.As<T, bool>(ref value));
    }

    internal static Cursor<T>? Decode<T>(string? encoded)
    {
        ValidateType<T>();
        if (string.IsNullOrEmpty(encoded)) return null;
        byte[]? rented = null;
        var length = Base64Url.GetMaxDecodedLength(encoded.Length);
        Span<byte> bytes = length <= 1024 ? stackalloc byte[length] : rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            if (!Base64Url.TryDecodeFromChars(encoded, bytes, out var written)) return null;
            var reader = new Utf8JsonReader(bytes[..written]);
            if (!ReadStart(ref reader, out var direction) || !reader.Read()) return null;
            var value = Read<T>(ref reader);
            return reader.Read() && reader.TokenType == JsonTokenType.EndArray && !reader.Read()
                ? new Cursor<T>(direction, value) : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException or OverflowException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    internal static (Cursor<TKey> Cursor, TTie TieBreaker)? Decode<TKey, TTie>(string? encoded)
    {
        ValidateType<TKey>();
        ValidateType<TTie>();
        if (string.IsNullOrEmpty(encoded)) return null;
        byte[]? rented = null;
        var length = Base64Url.GetMaxDecodedLength(encoded.Length);
        Span<byte> bytes = length <= 1024 ? stackalloc byte[length] : rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            if (!Base64Url.TryDecodeFromChars(encoded, bytes, out var written)) return null;
            var reader = new Utf8JsonReader(bytes[..written]);
            if (!ReadStart(ref reader, out var direction) || !reader.Read()) return null;
            var value = Read<TKey>(ref reader);
            if (!reader.Read()) return null;
            var tieBreaker = Read<TTie>(ref reader);
            return reader.Read() && reader.TokenType == JsonTokenType.EndArray && !reader.Read()
                ? (new Cursor<TKey>(direction, value), tieBreaker) : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException or OverflowException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static bool ReadStart(ref Utf8JsonReader reader, out CursorDirection direction)
    {
        direction = CursorDirection.After;
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray || !reader.Read() || reader.TokenType != JsonTokenType.String)
            return false;
        if (reader.ValueTextEquals("a"u8)) return true;
        if (!reader.ValueTextEquals("b"u8)) return false;
        direction = CursorDirection.Before;
        return true;
    }

    private static T Read<T>(ref Utf8JsonReader reader)
    {
        if (typeof(T) == typeof(int)) { var value = reader.GetInt32(); return Unsafe.As<int, T>(ref value); }
        if (typeof(T) == typeof(long)) { var value = reader.GetInt64(); return Unsafe.As<long, T>(ref value); }
        if (typeof(T) == typeof(Guid)) { var value = reader.GetGuid(); return Unsafe.As<Guid, T>(ref value); }
        if (typeof(T) == typeof(string)) { var value = reader.GetString() ?? throw new FormatException(); return Unsafe.As<string, T>(ref value); }
        if (typeof(T) == typeof(DateTime)) { var value = reader.GetDateTime(); return Unsafe.As<DateTime, T>(ref value); }
        if (typeof(T) == typeof(DateTimeOffset)) { var value = reader.GetDateTimeOffset(); return Unsafe.As<DateTimeOffset, T>(ref value); }
        if (typeof(T) == typeof(decimal)) { var value = reader.GetDecimal(); return Unsafe.As<decimal, T>(ref value); }
        if (typeof(T) == typeof(double)) { var value = reader.GetDouble(); return Unsafe.As<double, T>(ref value); }
        if (typeof(T) == typeof(bool)) { var value = reader.GetBoolean(); return Unsafe.As<bool, T>(ref value); }
        throw new NotSupportedException($"Cursor key type '{typeof(T)}' is not supported.");
    }
}
