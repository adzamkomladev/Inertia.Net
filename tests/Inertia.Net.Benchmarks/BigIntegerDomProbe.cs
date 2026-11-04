using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Inertia.Net.Benchmarks;

/// <summary>Warm, synchronous allocation and time of preserving 500 safe integer DOM tokens, reusing the output writer.</summary>
internal static class BigIntegerDomProbe
{
    public static void Run()
    {
        var expected = "[" + string.Join(',', Enumerable.Range(0, 500)) + "]";
        using var document = JsonDocument.Parse(expected);
        var output = new ArrayBufferWriter<byte>(4096);
        using var writer = new Utf8JsonWriter(output);
        void Write()
        {
            output.Clear();
            writer.Reset(output);
            BigIntegerConverters.WriteElement(writer, document.RootElement);
            writer.Flush();
        }

        for (var i = 0; i < 1000; i++) Write();
        const int iterations = 5000;
        for (var repetition = 1; repetition <= 3; repetition++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++) Write();
            var elapsed = Stopwatch.GetElapsedTime(started);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (Encoding.UTF8.GetString(output.WrittenSpan) != expected) throw new InvalidOperationException("DOM numeric tokens changed.");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"bigint-dom-500 repetition={repetition} {allocated / iterations} B/render {elapsed.TotalMicroseconds / iterations:F3} us/render"));
        }
    }
}
