using System.Buffers;

namespace Inertia.Net.Tests;

public sealed class PooledBufferWriterTests
{
    [Fact]
    public void Growing_past_the_pooled_size_keeps_the_written_bytes()
    {
        using var writer = new PooledBufferWriter();
        var chunk = new byte[64 * 1024];
        for (var i = 0; i < 40; i++)
        {
            chunk.AsSpan().Fill((byte)i);
            writer.Write(chunk);
        }

        Assert.Equal(40 * chunk.Length, writer.WrittenMemory.Length);
        for (var i = 0; i < 40; i++)
        {
            Assert.All(writer.WrittenMemory.Slice(i * chunk.Length, chunk.Length).ToArray(), b => Assert.Equal((byte)i, b));
        }
    }

    [Fact]
    public void Only_arrays_up_to_the_pooled_size_come_from_the_pool()
    {
        Assert.Equal(PooledBufferWriter.MaxPooledLength + 1, PooledBufferWriter.Rent(PooledBufferWriter.MaxPooledLength + 1).Length); // exact, not pooled
        var pooled = PooledBufferWriter.Rent(PooledBufferWriter.MaxPooledLength);
        Assert.Equal(PooledBufferWriter.MaxPooledLength, pooled.Length);
        PooledBufferWriter.Return(pooled);
        PooledBufferWriter.Return(new byte[PooledBufferWriter.MaxPooledLength * 2]); // ignored, no throw
    }
}
