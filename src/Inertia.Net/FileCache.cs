using System.Collections.Concurrent;

namespace Inertia.Net;

/// <summary>
/// Loads files once per path. With <c>reload</c> (Development), a changed last-write time is noticed by a stat at most once per
/// second. Without it, a loaded file is never touched again, but a missing file is re-checked at the same cadence, so a manifest
/// or template deployed after startup is picked up without a restart. At most <c>maxEntries</c> paths are cached.
/// </summary>
internal sealed class FileCache<T>(Func<string, T?> load, bool reload, TimeProvider time, int maxEntries = 256)
    where T : class
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Returns the loaded value, or null when the file does not exist. Load failures are not cached.</summary>
    public T? Get(string path)
    {
        var now = time.GetTimestamp();
        if (_entries.TryGetValue(path, out var entry))
        {
            if ((!reload && entry.Value is not null) || time.GetElapsedTime(entry.Checked, now) < Interval)
            {
                return entry.Value;
            }

            if (File.GetLastWriteTimeUtc(path) == entry.Stamp)
            {
                _entries[path] = entry with { Checked = now };
                return entry.Value;
            }
        }

        // The stamp is read before loading, so a write that lands during the load triggers another reload.
        var stamp = File.GetLastWriteTimeUtc(path);
        var value = File.Exists(path) ? load(path) : null;
        if (entry is not null || _entries.Count < maxEntries)
        {
            _entries[path] = new Entry(value, stamp, now);
        }

        return value;
    }

    private sealed record Entry(T? Value, DateTime Stamp, long Checked);
}
