using System.Buffers;

namespace Inertia.Net;

/// <summary>An <see cref="IBufferWriter{T}"/> over a pooled array. Dispose returns the array to the pool.</summary>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;

    // ArrayPool.Shared keeps arrays up to 1 GiB per core, so one very large page would pin its buffers for the life of the process.
    // Above this size buffers are allocated exactly and left to the GC: memory goes back after a large page, at the cost of more gen2 GCs.
    internal const int MaxPooledLength = 1024 * 1024;

    public PooledBufferWriter(int initialCapacity = 4096) => _buffer = Rent(initialCapacity);

    /// <summary>A pooled array of at least <paramref name="length"/> bytes, or an unpooled one above <see cref="MaxPooledLength"/>.</summary>
    internal static byte[] Rent(int length) =>
        length <= MaxPooledLength ? ArrayPool<byte>.Shared.Rent(length) : GC.AllocateUninitializedArray<byte>(length);

    /// <summary>Returns an array from <see cref="Rent"/> to the pool; a large one is left to the GC.</summary>
    internal static void Return(byte[] buffer)
    {
        if (buffer.Length is > 0 and <= MaxPooledLength)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    // A converter can write pre-serialized JSON directly, bypassing the JSON writer's encoder.
    // In valid JSON, a literal '<' can only occur inside a string. Escaping it preserves the value.
    public void EscapeHtmlScriptData()
    {
        var source = _buffer.AsSpan(0, _written);
        var next = source.IndexOf((byte)'<');
        if (next < 0) return;

        var length = checked(_written + 5 * source[next..].Count((byte)'<'));
        var escaped = Rent(length);
        var target = escaped.AsSpan();
        do
        {
            source[..next].CopyTo(target);
            "\\u003C"u8.CopyTo(target[next..]);
            target = target[(next + 6)..];
            source = source[(next + 1)..];
            next = source.IndexOf((byte)'<');
        } while (next >= 0);
        source.CopyTo(target);
        Return(_buffer);
        _buffer = escaped;
        _written = length;
    }

    public void Reset() => _written = 0;

    public void Advance(int count) => _written += count;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = [];
        _written = 0;
        Return(buffer);
    }

    private void EnsureCapacity(int sizeHint)
    {
        sizeHint = Math.Max(sizeHint, 1);
        if (_buffer.Length - _written >= sizeHint)
        {
            return;
        }

        var bigger = Rent((int)Math.Min(Math.Max(_buffer.Length * 2L, (long)_written + sizeHint), Array.MaxLength));
        _buffer.AsSpan(0, _written).CopyTo(bigger);
        Return(_buffer);
        _buffer = bigger;
    }
}
