using System.Security.Cryptography;
using System.Text;

namespace InertiaNetBench;

/// <summary>
/// The CPU-heavy work behind /compute. Same algorithm as compute.js (Node apps) and Compute.php (Laravel): 16 batches of n/16 records
/// from a xorshift32 generator, each sorted by (value, id), grouped into 64 buckets and hashed with SHA-256; the checksum is the SHA-256
/// of the 16 batch digests. Parallel runs the batches on the thread pool (Parallel.For); the result is the same either way.
/// </summary>
internal static class Compute
{
    private const int Batches = 16;

    public static string Run(int n, bool parallel)
    {
        var m = n / Batches;
        var digests = new string[Batches];
        if (parallel)
        {
            Parallel.For(0, Batches, b => digests[b] = Batch(b, m));
        }
        else
        {
            for (var b = 0; b < Batches; b++) digests[b] = Batch(b, m);
        }

        return Sha256(string.Concat(digests));
    }

    private readonly record struct Record(int Value, int Id, int Group);

    private static string Batch(int b, int m)
    {
        var s = unchecked((uint)(b + 1) * 2654435761u);
        uint Next()
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return s;
        }

        var records = new Record[m];
        for (var i = 0; i < m; i++)
        {
            var value = (int)(Next() % 1_000_000);
            records[i] = new Record(value, b * m + i, (int)(Next() % 64));
        }

        Array.Sort(records, static (x, y) => x.Value != y.Value ? x.Value.CompareTo(y.Value) : x.Id.CompareTo(y.Id));
        var counts = new int[64];
        var sums = new long[64];
        var text = new StringBuilder(m * 7);
        for (var i = 0; i < records.Length; i++)
        {
            var r = records[i];
            counts[r.Group]++;
            sums[r.Group] += r.Value;
            if (i > 0) text.Append(',');
            text.Append(r.Id);
        }

        text.Append('|');
        for (var g = 0; g < 64; g++)
        {
            if (g > 0) text.Append(';');
            text.Append(g).Append(':').Append(counts[g]).Append(':').Append(sums[g]);
        }

        return Sha256(text.ToString());
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
