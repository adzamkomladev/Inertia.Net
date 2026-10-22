using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>
/// The default <see cref="IInertiaCacheStore"/>. Props go in the app's <see cref="IMemoryCache"/>, in a key space of their own and sized
/// by their bytes (so a cache with a <c>SizeLimit</c> accepts them); renders go in a private cache bounded by
/// <see cref="SsrCacheOptions.MaxBytes"/>, whose least recently used entries are evicted first. Tags are kept in an index next to the
/// entries, so removing a tag frees its entries at once; a hit never reads the index.
/// </summary>
internal sealed class MemoryInertiaCacheStore : IInertiaCacheStore, IDisposable
{
    // MemoryCache adds a relative expiration to its clock's time, which overflows past DateTimeOffset.MaxValue; longer means never.
    private static readonly TimeSpan MaxExpiration = TimeSpan.FromDays(365 * 1000);

    private readonly IMemoryCache _props;
    private readonly MemoryCache _renders;
    private readonly PostEvictionDelegate _evicted;
    private readonly Lock _lock = new();

    // tag -> key -> the value stored with the tag. The value tells an entry from a later one under the same key.
    private readonly Dictionary<string, Dictionary<InertiaCacheKey, byte[]>> _tags = new(StringComparer.Ordinal);

    public MemoryInertiaCacheStore(IMemoryCache memoryCache, IOptions<InertiaOptions> options, TimeProvider timeProvider)
    {
        _props = memoryCache;
        _renders = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.Ssr.Cache.MaxBytes, Clock = new CacheClock(timeProvider) });
        _evicted = OnEvicted;
    }

    public ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken cancellationToken) =>
        new(Cache(key).TryGetValue(CacheKey(key), out byte[]? value) ? value : null);

    public ValueTask SetAsync(InertiaCacheKey key, byte[] value, InertiaCacheEntryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (options.Expiration <= TimeSpan.Zero)
        {
            return RemoveAsync(key, cancellationToken);
        }

        // Indexed first: a tag removed while the entry is added still finds it.
        var tags = options.Tags;
        lock (_lock)
        {
            foreach (var tag in tags)
            {
                if (!_tags.TryGetValue(tag, out var entries))
                {
                    _tags[tag] = entries = [];
                }

                entries[key] = value;
            }
        }

        using var entry = Cache(key).CreateEntry(CacheKey(key));
        entry.Value = value;
        entry.Size = value.Length;
        if (options.Expiration < MaxExpiration)
        {
            entry.AbsoluteExpirationRelativeToNow = options.Expiration;
        }

        entry.RegisterPostEvictionCallback(_evicted, tags);
        return default;
    }

    public ValueTask RemoveAsync(InertiaCacheKey key, CancellationToken cancellationToken)
    {
        Cache(key).Remove(CacheKey(key));
        return default;
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        KeyValuePair<InertiaCacheKey, byte[]>[] entries;
        lock (_lock)
        {
            if (!_tags.Remove(tag, out var tagged))
            {
                return default;
            }

            entries = [.. tagged];
        }

        foreach (var (key, value) in entries)
        {
            // Only the entry stored with the tag: a later value under the same key may not carry it.
            var cache = Cache(key);
            var cacheKey = CacheKey(key);
            if (cache.TryGetValue(cacheKey, out byte[]? current) && ReferenceEquals(current, value))
            {
                cache.Remove(cacheKey);
            }
        }

        return default;
    }

    public void Dispose() => _renders.Dispose();

    private IMemoryCache Cache(InertiaCacheKey key) => key.Kind == InertiaCacheEntryKind.Ssr ? _renders : _props;

    // Renders are keyed by their hash in the private cache; props by a key no app entry can equal.
    private static object CacheKey(InertiaCacheKey key) => key.Kind == InertiaCacheEntryKind.Ssr ? key.Value : new PropKey(key.Value);

    // Runs after an entry leaves its cache (expired, removed, replaced or refused for size): drops it from its tags.
    private void OnEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        var name = key is PropKey prop ? new InertiaCacheKey(InertiaCacheEntryKind.Prop, prop.Key) : new InertiaCacheKey(InertiaCacheEntryKind.Ssr, (string)key);
        lock (_lock)
        {
            foreach (var tag in (IReadOnlyList<string>)state!)
            {
                if (_tags.TryGetValue(tag, out var entries) && entries.TryGetValue(name, out var current) && ReferenceEquals(current, value))
                {
                    entries.Remove(name);
                    if (entries.Count == 0)
                    {
                        _tags.Remove(tag);
                    }
                }
            }
        }
    }

    /// <summary>A prop key in the app's memory cache: never equal to the app's own keys.</summary>
    private readonly record struct PropKey(string Key);

    // MemoryCacheOptions.Clock takes the older ISystemClock; this lets tests move the cache's time with a FakeTimeProvider.
    private sealed class CacheClock(TimeProvider time) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
