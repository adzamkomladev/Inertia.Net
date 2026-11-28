# Inertia.Net.Redis

A Redis store for [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net)'s cache: cached props (`.Cache(...)`) and SSR renders are
shared by every app instance, and `IInertiaCache.RemoveByTagAsync` removes them everywhere while the tag indexes are intact.
Use a properly sized Redis instance with the `noeviction` policy when tag invalidation must be reliable. Redis eviction can discard a tag index while its cached value survives until expiration.

```csharp
builder.Services.AddInertia();
builder.Services.AddInertiaRedisCache(o =>
{
    o.Configuration = "localhost:6379"; // or leave it unset to use a registered IConnectionMultiplexer
    o.KeyPrefix = "myapp:v1:";          // apps (and cache versions) sharing a server need different prefixes
});
```

- Each entry is a Redis string with the entry's expiration. Each tag is a set of entry keys that lives as long as its longest
  entry, and each entry keeps an index of its tags. Storing or removing an entry is one script, so a removal racing a store
  leaves the entry either removed or whole and still tagged.
- On Redis Cluster, put a hash tag in the prefix (`{myapp}:`): an entry, its index and its tag sets change together in one script.
- Failures are not swallowed. While a page renders, Inertia.Net logs a failing store (rate-limited) and renders without the cache;
  `IInertiaCache` calls throw, so a failed invalidation is not silent.
- The cache store is not verified under Native AOT.

## Live backplane

The same package carries [live props](https://github.com/adzamkomladev/Inertia.Net#live-props) across instances and workers over
Redis pub/sub. It is independent of the cache store:

```csharp
builder.Services.AddInertiaLive().AddSse().AddRedisBackplane(o =>
{
    o.Configuration = "localhost:6379"; // or leave it unset to use a registered IConnectionMultiplexer
    o.Prefix = "myapp:";                // apps sharing a server need different prefixes
    // o.UseShardedPubSub = true; o.ShardCount = 16;  // Redis 7+ cluster: SPUBLISH/SSUBSCRIBE over 16 channels
});
```

- Pub/sub keeps no history. When the subscription connection drops, every instance closes its live connections and the clients
  reconnect and reload their live props.
- An event with values above 1 MiB goes out without values, and clients reload instead.
- A publish fails with the Redis error if Redis is down. The backplane is checked by the Native AOT smoke test.

See [Live props at scale](https://inertia-dotnet.temtem.dev/docs/live-props-at-scale).
