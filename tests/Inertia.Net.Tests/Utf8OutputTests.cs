using System.Buffers;
using System.Text;

namespace Inertia.Net.Tests;

public class Utf8OutputTests
{
    [Fact]
    public void Large_output_is_chunked_without_splitting_surrogate_pairs()
    {
        var text = new string('a', 4095) + "🌍" + new string('b', 100_000);
        var writer = new BoundedWriter();
        Utf8Output.Write(writer, text);
        Assert.Equal(text, Encoding.UTF8.GetString(writer.Buffer.WrittenSpan));
        Assert.InRange(writer.LargestHint, 1, Encoding.UTF8.GetMaxByteCount(4096));
    }

    private sealed class BoundedWriter : IBufferWriter<byte>
    {
        internal ArrayBufferWriter<byte> Buffer { get; } = new();
        internal int LargestHint;
        public void Advance(int count) => Buffer.Advance(count);
        public Memory<byte> GetMemory(int sizeHint = 0) { LargestHint = Math.Max(LargestHint, sizeHint); return Buffer.GetMemory(sizeHint); }
        public Span<byte> GetSpan(int sizeHint = 0) { LargestHint = Math.Max(LargestHint, sizeHint); return Buffer.GetSpan(sizeHint); }
    }
}
