using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Inertia.Net;

/// <summary>Settings of the Redis <see cref="IInertiaCacheStore"/> (see <see cref="InertiaRedisServiceCollectionExtensions.AddInertiaRedisCache"/>).</summary>
public sealed class InertiaRedisCacheOptions
{
    /// <summary>
    /// The StackExchange.Redis configuration string, e.g. <c>localhost:6379</c>. Leave null to use the <see cref="IConnectionMultiplexer"/>
    /// registered in the services.
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// Put before every key, to keep apps (or cache versions) that share a server apart. Default <c>inertia.net:</c>. On Redis Cluster, use a
    /// hash tag such as <c>{myapp}:</c>: an entry, its tag index and its tag sets are changed together in one script, so they must share a slot.
    /// </summary>
    public string KeyPrefix { get; set; } = "inertia.net:";

    /// <summary>The Redis database number; -1 for the connection's default.</summary>
    public int Database { get; set; } = -1;
}

/// <summary>
/// Keeps cached props and SSR renders in Redis, so every app instance shares them. An entry is a string with the entry's expiration;
/// each tag is a set of entry keys that lives as long as its longest entry, and each entry has an index of its tags. Storing an entry
/// and removing one (or a batch of a tag's entries) are each a single script, so an entry is never left half stored or half removed.
/// Errors are not swallowed: while a page renders, Inertia.Net logs a failing store and renders without the cache, and
/// <see cref="IInertiaCache"/> reports a failed removal to its caller.
/// </summary>
internal sealed class RedisInertiaCacheStore : IInertiaCacheStore
{
    private const int RemoveBatch = 256;

    // KEYS: entry, tag index, tag sets. ARGV: value, entry ttl (ms), prefix, tag set ttl (ms), tags (one per tag set).
    private static readonly LuaScript Set = LuaScript.Prepare("""
        local entry, index = KEYS[1], KEYS[2]
        local member = string.sub(entry, string.len(ARGV[3]) + 1)
        for _, old in ipairs(redis.call('SMEMBERS', index)) do
          redis.call('SREM', ARGV[3] .. 'tag:' .. old, member)
        end
        redis.call('DEL', index)
        redis.call('SET', entry, ARGV[1], 'PX', ARGV[2])
        for i = 3, #KEYS do
          -- An entry that expires on its own stays in its tag sets; sweep two members per add to keep long-lived sets bounded.
          for _, old in ipairs(redis.call('SRANDMEMBER', KEYS[i], 2)) do
            if redis.call('EXISTS', ARGV[3] .. old) == 0 then redis.call('SREM', KEYS[i], old) end
          end
          redis.call('SADD', index, ARGV[i + 2])
          redis.call('SADD', KEYS[i], member)
          local ttl = redis.call('PTTL', KEYS[i])
          if ttl < tonumber(ARGV[4]) then redis.call('PEXPIRE', KEYS[i], ARGV[4]) end
        end
        if #KEYS > 2 then redis.call('PEXPIRE', index, ARGV[2]) end
        return 1
        """);

    // KEYS: entry, tag index. ARGV: prefix.
    private static readonly LuaScript Remove = LuaScript.Prepare("""
        local member = string.sub(KEYS[1], string.len(ARGV[1]) + 1)
        for _, tag in ipairs(redis.call('SMEMBERS', KEYS[2])) do
          redis.call('SREM', ARGV[1] .. 'tag:' .. tag, member)
        end
        return redis.call('UNLINK', KEYS[1], KEYS[2])
        """);

    // KEYS: tag set. ARGV: prefix, batch size, tag. Removes up to a batch of the tag's entries with their other memberships; returns how
    // many members it took from the set. A member whose entry's current index lacks the tag is obsolete (the entry expired with its index,
    // then the key was stored again under other tags): it is dropped from the set, and the live entry is left alone.
    private static readonly LuaScript RemoveTagged = LuaScript.Prepare("""
        local members = redis.call('SPOP', KEYS[1], ARGV[2])
        for _, member in ipairs(members) do
          local index = ARGV[1] .. 'tags:' .. member
          if redis.call('SISMEMBER', index, ARGV[3]) == 1 then
            for _, tag in ipairs(redis.call('SMEMBERS', index)) do
              redis.call('SREM', ARGV[1] .. 'tag:' .. tag, member)
            end
            redis.call('UNLINK', ARGV[1] .. member, index)
          end
        end
        return #members
        """);

    private readonly IConnectionMultiplexer _connection;
    private readonly InertiaRedisCacheOptions _options;

    public RedisInertiaCacheStore(IOptions<InertiaRedisCacheOptions> options, IServiceProvider services)
    {
        _options = options.Value;
        _connection = _options.Configuration is { } configuration
            ? ConnectionMultiplexer.Connect(configuration)
            : (IConnectionMultiplexer?)services.GetService(typeof(IConnectionMultiplexer))
              ?? throw new InvalidOperationException("Set InertiaRedisCacheOptions.Configuration or register an IConnectionMultiplexer.");
    }

    private IDatabase Database => _connection.GetDatabase(_options.Database);

    private string Prefix => _options.KeyPrefix;

    public async ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (byte[]?)await Database.StringGetAsync(Prefix + key);
    }

    public async ValueTask SetAsync(InertiaCacheKey key, byte[] value, InertiaCacheEntryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        var ttl = (long)Math.Ceiling(options.Expiration.TotalMilliseconds);
        if (ttl <= 0)
        {
            await RemoveAsync(key, cancellationToken);
            return;
        }

        var member = key.ToString();
        var tags = options.Tags;
        if (tags.Count > 1)
        {
            tags = tags.Distinct(StringComparer.Ordinal).ToArray();
        }

        var keys = new RedisKey[tags.Count + 2];
        var args = new RedisValue[tags.Count + 4];
        keys[0] = Prefix + member;
        keys[1] = Prefix + "tags:" + member;
        args[0] = value;
        args[1] = ttl;
        args[2] = Prefix;
        args[3] = ttl;
        for (var i = 0; i < tags.Count; i++)
        {
            keys[i + 2] = Prefix + "tag:" + tags[i];
            args[i + 4] = tags[i];
        }

        await Database.ScriptEvaluateAsync(Set.ExecutableScript, keys, args);
    }

    public async ValueTask RemoveAsync(InertiaCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var member = key.ToString();
        await Database.ScriptEvaluateAsync(Remove.ExecutableScript, [Prefix + member, Prefix + "tags:" + member], [Prefix]);
    }

    public async ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tag);
        RedisKey set = Prefix + "tag:" + tag;
        // In batches, so a tag on every entry (InertiaCacheTags.Props) does not block the server in one long script.
        while ((int)await Database.ScriptEvaluateAsync(RemoveTagged.ExecutableScript, [set], [Prefix, RemoveBatch, tag]) == RemoveBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
