using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Inertia.Net.Benchmarks;

/// <summary>
/// Warm, synchronous allocation and time of preserving big integers in DOM values, reusing the output writer: 500 safe integer tokens,
/// and 100 rows with a string, a decimal, a safe and an unsafe integer property. The output hash shows the bytes did not change.
/// </summary>
internal static class BigIntegerDomProbe
{
    public static void Run()
    {
        var integers = "[" + string.Join(',', Enumerable.Range(0, 500)) + "]";
        var rows = "[" + string.Join(',', Enumerable.Range(0, 100).Select(i =>
            $$"""{"id":{{i}},"name":"Row {{i}}","price":{{i}}.99,"ratio":1e-{{i % 9 + 1}},"big":{{9007199254740993L + i}},"tags":["a","b"],"ok":true}""")) + "]";
        foreach (var (name, json, check) in new[] { ("bigint-dom-500", integers, true), ("bigint-dom-rows-100", rows, false) })
        {
            using var document = JsonDocument.Parse(json);
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
                if (check && Encoding.UTF8.GetString(output.WrittenSpan) != json) throw new InvalidOperationException("DOM numeric tokens changed.");
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{name} repetition={repetition} {allocated / iterations} B/render {elapsed.TotalMicroseconds / iterations:F3} us/render output={Convert.ToHexString(SHA256.HashData(output.WrittenSpan))[..16]}"));
            }
        }
    }
}
