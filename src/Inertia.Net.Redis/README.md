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
- Not verified under Native AOT.
