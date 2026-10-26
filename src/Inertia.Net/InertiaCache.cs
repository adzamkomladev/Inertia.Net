using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Inertia.Net;

/// <summary>
/// The store behind cached props (<see cref="InertiaProp.Cache(string, TimeSpan)"/>) and the SSR response cache. The default keeps props
/// in the app's <c>IMemoryCache</c> and renders in a private cache bounded by <see cref="SsrCacheOptions.MaxBytes"/>, per app instance.
/// Register your own singleton (before or after <c>AddInertia()</c>) to keep the entries elsewhere, such as a cache every instance shares.
/// </summary>
/// <remarks>
/// Values are bytes written by Inertia.Net that carry their own fresh and stale times, so a store only keeps each one for
/// <see cref="InertiaCacheEntryOptions.Expiration"/>. A key's <see cref="InertiaCacheKey.ToString"/> is its flat form for stores keyed by
/// strings (<c>inertia:prop:{key}</c>, <c>inertia:ssr:{hash}</c>), and every entry carries <see cref="InertiaCacheTags.Props"/> or
/// <see cref="InertiaCacheTags.Ssr"/> next to its own tags. While a page renders, a store that throws is logged and counts as a miss;
/// <see cref="IInertiaCache"/> lets the exception through.
/// </remarks>
public interface IInertiaCacheStore
{
    /// <summary>The value stored under <paramref name="key"/>, or null.</summary>
    ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken cancellationToken);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>, replacing any value. Inertia.Net never changes the array afterwards.</summary>
    ValueTask SetAsync(InertiaCacheKey key, byte[] value, InertiaCacheEntryOptions options, CancellationToken cancellationToken);

    /// <summary>Removes the value stored under <paramref name="key"/>, if any.</summary>
    ValueTask RemoveAsync(InertiaCacheKey key, CancellationToken cancellationToken);

    /// <summary>Removes every value stored with <paramref name="tag"/>.</summary>
    ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken);
}

/// <summary>What a cache entry holds.</summary>
public enum InertiaCacheEntryKind
{
    /// <summary>A prop value cached with <see cref="InertiaProp.Cache(string, TimeSpan)"/>.</summary>
    Prop,

    /// <summary>A server-side render (<see cref="SsrCacheOptions"/>).</summary>
    Ssr,
}

/// <summary>
/// The key of a cache entry: its kind and, for a prop, the key given to <see cref="InertiaProp.Cache(string, TimeSpan)"/>, for a render the
/// SHA-256 of the page JSON in hex. Kept apart so a lookup builds no string; <see cref="ToString"/> gives the flat form.
/// </summary>
/// <param name="Kind">What the entry holds.</param>
/// <param name="Value">The key within its kind.</param>
public readonly record struct InertiaCacheKey(InertiaCacheEntryKind Kind, string Value)
{
    /// <summary><c>inertia:prop:{Value}</c> or <c>inertia:ssr:{Value}</c>.</summary>
    public override string ToString() => (Kind == InertiaCacheEntryKind.Ssr ? "inertia:ssr:" : "inertia:prop:") + Value;
}

/// <summary>How long an <see cref="IInertiaCacheStore"/> keeps a value, and the tags it can be removed by.</summary>
public readonly struct InertiaCacheEntryOptions
{
    private readonly IReadOnlyList<string>? _tags;

    /// <summary>Creates the options.</summary>
    public InertiaCacheEntryOptions(TimeSpan expiration, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        Expiration = expiration;
        _tags = tags;
    }

    /// <summary>How long to keep the value from now: its fresh time plus its stale time.</summary>
    public TimeSpan Expiration { get; }

    /// <summary>The value's tags, including <see cref="InertiaCacheTags.Props"/> or <see cref="InertiaCacheTags.Ssr"/>.</summary>
    public IReadOnlyList<string> Tags => _tags ?? [];
}

/// <summary>The tags every cache entry carries, by kind.</summary>
public static class InertiaCacheTags
{
    /// <summary>Carried by every cached prop.</summary>
    public const string Props = "inertia:props";

    /// <summary>Carried by every cached SSR render.</summary>
    public const string Ssr = "inertia:ssr";
}

/// <summary>
/// Removes and replaces cached props and SSR renders, in the configured <see cref="IInertiaCacheStore"/>. Inject it where the data
/// changes, e.g. after a save: <c>await cache.RemoveByTagAsync($"orders:{userId}")</c>.
/// </summary>
public interface IInertiaCache
{
    /// <summary>Removes the prop value cached under <paramref name="key"/> (the key given to <see cref="InertiaProp.Cache(string, TimeSpan)"/>).</summary>
    ValueTask RemovePropAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes every cached prop that carries <paramref name="tag"/>.</summary>
    ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>Removes every cached prop.</summary>
    ValueTask ClearPropsAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes every cached SSR render.</summary>
    ValueTask ClearSsrAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Caches <paramref name="value"/> under <paramref name="key"/> as if a loader had returned it, e.g. the new value right after a save.
    /// It is serialized like a prop of type <typeparamref name="T"/> (a scroll prop's paging metadata comes from
    /// <see cref="IProvidesScrollMetadata"/>).
    /// </summary>
    /// <param name="key">The prop's cache key.</param>
    /// <param name="value">The value.</param>
    /// <param name="duration">How long the value is fresh.</param>
    /// <param name="staleFor">How long after that it is still served while a refresh runs; see <see cref="InertiaProp.Cache(string, TimeSpan, TimeSpan?, IEnumerable{string}?)"/>.</param>
    /// <param name="tags">Tags to remove the value by.</param>
    /// <param name="cancellationToken">Cancels the store call.</param>
    ValueTask SetPropAsync<T>(string key, T value, TimeSpan duration, TimeSpan? staleFor = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// A prop's <see cref="InertiaProp.Cache(string, TimeSpan)"/> settings, as small as those of 1.4 unless the prop has a stale time or tags
/// (<see cref="Extended"/>): one object per cached prop of every render.
/// </summary>
internal class PropCaching
{
    private static readonly string[] DefaultTags = [InertiaCacheTags.Props];

    private protected PropCaching(string key, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        Key = key;
        Duration = duration;
    }

    public string Key { get; }

    public TimeSpan Duration { get; }

    public virtual TimeSpan StaleFor => TimeSpan.Zero;

    /// <summary>The tags, ending with <see cref="InertiaCacheTags.Props"/>.</summary>
    public virtual string[] Tags => DefaultTags;

    public InertiaCacheKey StoreKey => new(InertiaCacheEntryKind.Prop, Key);

    /// <summary>How long the store keeps the value: <see cref="Duration"/> plus <see cref="StaleFor"/>.</summary>
    public TimeSpan Expiration => InertiaCaching.Expiration(Duration, StaleFor);

    public static PropCaching Create(string key, TimeSpan duration, TimeSpan? staleFor, IEnumerable<string>? tags) =>
        staleFor is null && tags is null ? new PropCaching(key, duration) : new Extended(key, duration, staleFor ?? TimeSpan.Zero, tags);

    private sealed class Extended : PropCaching
    {
        private readonly string[] _tags;

        public Extended(string key, TimeSpan duration, TimeSpan staleFor, IEnumerable<string>? tags)
            : base(key, duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(staleFor, TimeSpan.Zero, nameof(staleFor));
            StaleFor = staleFor;
            _tags = tags is null ? DefaultTags : InertiaCaching.WithTag(tags, InertiaCacheTags.Props);
        }

        public override TimeSpan StaleFor { get; }

        public override string[] Tags => _tags;
    }
}

/// <summary>A response's <see cref="InertiaResult.WithSsrCache(TimeSpan)"/> settings; a null stale time follows <see cref="SsrCacheOptions.StaleFor"/>.</summary>
internal sealed record SsrCaching(TimeSpan Duration, TimeSpan? StaleFor);

/// <summary>A cached prop as the walk writes it: its HTML-safe JSON, whether that is an array, a scroll prop's paging metadata.</summary>
internal readonly record struct CachedProp(ReadOnlyMemory<byte> Json, bool IsArray, ScrollMetadata? Scroll, bool IsStale);

/// <summary>A cached SSR render.</summary>
internal readonly record struct CachedRender(ReadOnlyMemory<byte> Head, ReadOnlyMemory<byte> Body, bool IsStale);

/// <summary>
/// The rendering side of the caches: keys, the store calls (a failing store is logged and counts as a miss) and the refreshes in flight,
/// so a stale entry is refreshed once per key and app instance however many requests read it meanwhile.
/// </summary>
internal sealed partial class InertiaCaching
{
    internal static readonly string[] SsrTags = [InertiaCacheTags.Ssr];
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<InertiaCacheKey, byte> _refreshing = new();
    private readonly ConcurrentDictionary<InertiaCacheKey, TaskCompletionSource<byte[]?>> _filling = new();
    private int _refreshCount;
    private long _nextFailureLog;
    private int _suppressedFailures;

    public InertiaCaching(IInertiaCacheStore store, TimeProvider timeProvider, ILogger<InertiaCaching> logger)
    {
        Store = store;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IInertiaCacheStore Store { get; }

    /// <summary>The key of a render: the SHA-256 of the page JSON in hex (one string, no hash array).</summary>
    internal static InertiaCacheKey SsrKey(ReadOnlySpan<byte> pageJson)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(pageJson, hash);
        return new(InertiaCacheEntryKind.Ssr, Convert.ToHexString(hash));
    }

    /// <summary>The tags followed by <paramref name="tag"/>, in one array.</summary>
    internal static string[] WithTag(IEnumerable<string> tags, string tag)
    {
        string[] all;
        if (tags is ICollection<string> collection)
        {
            all = new string[collection.Count + 1];
            collection.CopyTo(all, 0);
        }
        else
        {
            all = [.. tags, tag];
        }

        all[^1] = tag;
        for (var i = 0; i < all.Length - 1; i++)
        {
            ArgumentException.ThrowIfNullOrEmpty(all[i], nameof(tags));
        }

        return all;
    }

    /// <summary>How long a store keeps an entry: its fresh time plus its stale time.</summary>
    internal static TimeSpan Expiration(TimeSpan duration, TimeSpan staleFor) =>
        staleFor > TimeSpan.MaxValue - duration ? TimeSpan.MaxValue : duration + staleFor;

    /// <summary>The fresh-until and expires-at times, in Unix milliseconds, of an entry stored now.</summary>
    internal (long FreshUntil, long ExpiresAt) Times(TimeSpan duration, TimeSpan expiration)
    {
        var now = Now();
        return (Add(now, duration), Add(now, expiration));
    }

    private static long Add(long now, TimeSpan span) => span.TotalMilliseconds >= long.MaxValue - now ? long.MaxValue : now + (long)span.TotalMilliseconds;

    public async ValueTask<CachedProp?> GetPropAsync(PropCaching caching, CancellationToken cancellationToken)
    {
        var value = await GetAsync(caching.StoreKey, cancellationToken);
        return value is not null && CacheEntry.TryReadProp(value, Now(), out var prop) ? prop : null;
    }

    public async ValueTask<CachedRender?> GetRenderAsync(InertiaCacheKey key, CancellationToken cancellationToken)
    {
        var value = await GetAsync(key, cancellationToken);
        return value is not null && CacheEntry.TryReadRender(value, Now(), out var render) ? render : null;
    }

    /// <summary>Claims a missing entry, or returns the current fill whose successful bytes are shared even if storage fails.</summary>
    public bool TryBeginFill(InertiaCacheKey key, out TaskCompletionSource<byte[]?> completion)
    {
        var candidate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        completion = _filling.GetOrAdd(key, candidate);
        return ReferenceEquals(completion, candidate);
    }

    public void EndFill(InertiaCacheKey key, TaskCompletionSource<byte[]?> completion)
    {
        _filling.TryRemove(key, out _);
        completion.TrySetResult(null);
    }

    /// <summary>Stores an entry while rendering: a failing store is logged and the page is still written. Ends a refresh of the key.</summary>
    public async ValueTask SetAsync(InertiaCacheKey key, byte[] value, TimeSpan expiration, string[] tags, CancellationToken cancellationToken)
    {
        try
        {
            await Store.SetAsync(key, value, new InertiaCacheEntryOptions(expiration, tags), cancellationToken);
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            LogStoreFailure(ex, "store", key);
        }
        finally
        {
            EndRefresh(key);
        }
    }

    /// <summary>Whether a refresh of the entry is in flight.</summary>
    public bool IsRefreshing(InertiaCacheKey key) => Volatile.Read(ref _refreshCount) != 0 && _refreshing.ContainsKey(key);

    /// <summary>Claims the refresh of a stale entry; false while another request's refresh of it is in flight.</summary>
    public bool TryBeginRefresh(InertiaCacheKey key)
    {
        if (!_refreshing.TryAdd(key, 0))
        {
            return false;
        }

        Interlocked.Increment(ref _refreshCount);
        return true;
    }

    // Called for every stored entry: the count spares the dictionary (whose IsEmpty takes all its locks) while nothing refreshes.
    public void EndRefresh(InertiaCacheKey key)
    {
        if (Volatile.Read(ref _refreshCount) != 0 && _refreshing.TryRemove(key, out _))
        {
            Interlocked.Decrement(ref _refreshCount);
        }
    }

    public void LogRefreshFailure(Exception exception, string what) => LogRefreshFailed(_logger, exception, what);

    private long Now() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    private async ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken cancellationToken)
    {
        try
        {
            return await Store.GetAsync(key, cancellationToken);
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            LogStoreFailure(ex, "read", key);
            return null;
        }
    }

    private static bool IsCancellation(Exception ex, CancellationToken cancellationToken) =>
        ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

    // At most one message per interval, so a store that is down does not log once per cached prop of every page.
    private void LogStoreFailure(Exception exception, string operation, InertiaCacheKey key)
    {
        var now = _timeProvider.GetTimestamp();
        var next = Volatile.Read(ref _nextFailureLog);
        var due = now + (long)(FailureLogInterval.TotalSeconds * _timeProvider.TimestampFrequency);
        if (now < next || Interlocked.CompareExchange(ref _nextFailureLog, due, next) != next)
        {
            Interlocked.Increment(ref _suppressedFailures);
            return;
        }

        LogStoreFailed(_logger, exception, operation, key.ToString(), Interlocked.Exchange(ref _suppressedFailures, 0));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Inertia cache store failed to {Operation} '{Key}'; rendering without the cache ({Suppressed} similar failures not logged since the last message)")]
    private static partial void LogStoreFailed(ILogger logger, Exception exception, string operation, string key, int suppressed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refreshing the stale Inertia cache entry for {What} failed; the stale entry is kept until it expires")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, string what);
}

/// <summary>
/// The bytes Inertia.Net keeps in an <see cref="IInertiaCacheStore"/>. A header of 18 bytes: the kind (and format version), flags, then the
/// fresh-until and expires-at times in Unix milliseconds (little-endian). A prop follows with its scroll metadata when it has some
/// (a 4-byte length and JSON) and its JSON; a render with its head length (4 bytes), head and body. Anything else reads as a miss, so a
/// shared store can outlive a format change.
/// </summary>
internal static class CacheEntry
{
    private const byte PropKind = 1, RenderKind = 2;
    private const byte IsArrayFlag = 1, HasScrollFlag = 2;
    private const int HeaderSize = 18, LengthSize = 4;

    /// <summary>Reserves the header and writes the scroll metadata of a prop entry, whose length it returns; the value's JSON goes next.</summary>
    public static int BeginProp(PooledBufferWriter buffer, ScrollMetadata? scroll)
    {
        buffer.GetSpan(HeaderSize + LengthSize);
        buffer.Advance(scroll is null ? HeaderSize : HeaderSize + LengthSize);
        if (scroll is null)
        {
            return 0;
        }

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("pageName", scroll.PageName);
            WritePage(writer, "previousPage", scroll.PreviousPage);
            WritePage(writer, "nextPage", scroll.NextPage);
            WritePage(writer, "currentPage", scroll.CurrentPage);
            writer.WriteEndObject();
        }

        return buffer.WrittenMemory.Length - HeaderSize - LengthSize;
    }

    /// <summary>The finished prop entry, and the prop as the walk writes it.</summary>
    public static byte[] EndProp(PooledBufferWriter buffer, ScrollMetadata? scroll, int scrollLength, long freshUntil, long expiresAt, out CachedProp prop)
    {
        var entry = buffer.WrittenMemory.ToArray();
        var jsonStart = HeaderSize;
        if (scroll is not null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(HeaderSize), scrollLength);
            jsonStart += LengthSize + scrollLength;
        }

        var isArray = entry.Length > jsonStart && entry[jsonStart] == (byte)'[';
        WriteHeader(entry, PropKind, (byte)((isArray ? IsArrayFlag : 0) | (scroll is null ? 0 : HasScrollFlag)), freshUntil, expiresAt);
        prop = new CachedProp(entry.AsMemory(jsonStart), isArray, scroll, IsStale: false);
        return entry;
    }

    public static bool TryReadProp(byte[] entry, long now, out CachedProp prop)
    {
        prop = default;
        if (!TryReadHeader(entry, PropKind, now, out var flags, out var stale))
        {
            return false;
        }

        var jsonStart = HeaderSize;
        ScrollMetadata? scroll = null;
        if ((flags & HasScrollFlag) != 0)
        {
            if (entry.Length < HeaderSize + LengthSize)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(HeaderSize));
            if (length < 0 || length > entry.Length - HeaderSize - LengthSize || !TryReadScroll(entry.AsSpan(HeaderSize + LengthSize, length), out scroll))
            {
                return false;
            }

            jsonStart += LengthSize + length;
        }

        if (entry.Length == jsonStart)
        {
            return false;
        }

        prop = new CachedProp(entry.AsMemory(jsonStart), (flags & IsArrayFlag) != 0, scroll, stale);
        return true;
    }

    public static byte[] CreateRender(ReadOnlySpan<byte> head, ReadOnlySpan<byte> body, long freshUntil, long expiresAt, out CachedRender render)
    {
        var entry = new byte[HeaderSize + LengthSize + head.Length + body.Length];
        WriteHeader(entry, RenderKind, 0, freshUntil, expiresAt);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(HeaderSize), head.Length);
        head.CopyTo(entry.AsSpan(HeaderSize + LengthSize));
        body.CopyTo(entry.AsSpan(HeaderSize + LengthSize + head.Length));
        render = new CachedRender(entry.AsMemory(HeaderSize + LengthSize, head.Length), entry.AsMemory(HeaderSize + LengthSize + head.Length), IsStale: false);
        return entry;
    }

    public static bool TryReadRender(byte[] entry, long now, out CachedRender render)
    {
        render = default;
        if (!TryReadHeader(entry, RenderKind, now, out _, out var stale) || entry.Length < HeaderSize + LengthSize)
        {
            return false;
        }

        var headLength = BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(HeaderSize));
        if (headLength < 0 || headLength > entry.Length - HeaderSize - LengthSize)
        {
            return false;
        }

        var bodyStart = HeaderSize + LengthSize + headLength;
        render = new CachedRender(entry.AsMemory(HeaderSize + LengthSize, headLength), entry.AsMemory(bodyStart), stale);
        return true;
    }

    private static void WriteHeader(Span<byte> entry, byte kind, byte flags, long freshUntil, long expiresAt)
    {
        entry[0] = kind;
        entry[1] = flags;
        BinaryPrimitives.WriteInt64LittleEndian(entry[2..], freshUntil);
        BinaryPrimitives.WriteInt64LittleEndian(entry[10..], expiresAt);
    }

    // The entry's own expiry decides, whatever the store's clock does: an entry past it is a miss, one past its fresh time is stale.
    private static bool TryReadHeader(byte[] entry, byte kind, long now, out byte flags, out bool stale)
    {
        flags = 0;
        stale = false;
        if (entry.Length < HeaderSize || entry[0] != kind)
        {
            return false;
        }

        var expiresAt = BinaryPrimitives.ReadInt64LittleEndian(entry.AsSpan(10));
        if (now >= expiresAt)
        {
            return false;
        }

        flags = entry[1];
        stale = now >= BinaryPrimitives.ReadInt64LittleEndian(entry.AsSpan(2));
        return true;
    }

    private static void WritePage(Utf8JsonWriter writer, string name, object? page)
    {
        switch (page)
        {
            case null:
                writer.WriteNull(name);
                break;
            case int number:
                writer.WriteNumber(name, number);
                break;
            case long number:
                writer.WriteNumber(name, number);
                break;
            case string text:
                writer.WriteString(name, text);
                break;
            default:
                throw new InvalidOperationException($"Scroll page values must be int, long, string or null, not '{page.GetType()}'.");
        }
    }

    private static bool TryReadScroll(ReadOnlySpan<byte> json, out ScrollMetadata? scroll)
    {
        scroll = null;
        string? pageName = null;
        object? previous = null, next = null, current = null;
        try
        {
            var reader = new Utf8JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                object? value = reader.TokenType switch
                {
                    JsonTokenType.String => reader.GetString(),
                    JsonTokenType.Number => reader.TryGetInt32(out var number) ? number : reader.GetInt64(),
                    _ => null,
                };
                switch (name)
                {
                    case "pageName": pageName = value as string; break;
                    case "previousPage": previous = value; break;
                    case "nextPage": next = value; break;
                    case "currentPage": current = value; break;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        if (pageName is null)
        {
            return false;
        }

        scroll = new ScrollMetadata(pageName, previous, next, current);
        return true;
    }
}

/// <summary>The <see cref="IInertiaCache"/> service.</summary>
internal sealed class InertiaCache(InertiaCaching caching, InertiaPageWriter pages) : IInertiaCache
{
    public ValueTask RemovePropAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return caching.Store.RemoveAsync(new InertiaCacheKey(InertiaCacheEntryKind.Prop, key), cancellationToken);
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        return caching.Store.RemoveByTagAsync(tag, cancellationToken);
    }

    public ValueTask ClearPropsAsync(CancellationToken cancellationToken = default) => caching.Store.RemoveByTagAsync(InertiaCacheTags.Props, cancellationToken);

    public ValueTask ClearSsrAsync(CancellationToken cancellationToken = default) => caching.Store.RemoveByTagAsync(InertiaCacheTags.Ssr, cancellationToken);

    public ValueTask SetPropAsync<T>(string key, T value, TimeSpan duration, TimeSpan? staleFor = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        var settings = PropCaching.Create(key, duration, staleFor, tags);
        var entry = pages.CreateCacheEntry(typeof(T), value, (value as IProvidesScrollMetadata)?.GetScrollMetadata(), settings, key, out _);
        return caching.Store.SetAsync(settings.StoreKey, entry, new InertiaCacheEntryOptions(settings.Expiration, settings.Tags), cancellationToken);
    }
}
