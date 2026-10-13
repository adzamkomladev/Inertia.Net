using System.Buffers;
using System.Text;

namespace Inertia.Net;

internal static class Utf8Output
{
    internal static void Write(IBufferWriter<byte> writer, string text)
    {
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            var length = Math.Min(4096, remaining.Length);
            if (length < remaining.Length && char.IsHighSurrogate(remaining[length - 1])) length--;
            var chunk = remaining[..length];
            writer.Advance(Encoding.UTF8.GetBytes(chunk, writer.GetSpan(Encoding.UTF8.GetMaxByteCount(length))));
            remaining = remaining[length..];
        }
    }
}
