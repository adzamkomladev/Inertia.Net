using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inertia.Net;

/// <summary>Registers the Redis cache store.</summary>
public static class InertiaRedisServiceCollectionExtensions
{
    /// <summary>
    /// Keeps cached props (<see cref="InertiaProp.Cache(string, TimeSpan)"/>) and SSR renders in Redis, shared by every app instance:
    /// <c>services.AddInertiaRedisCache(o =&gt; o.Configuration = "localhost:6379")</c>, or leave <c>Configuration</c> unset to use the
    /// registered <c>IConnectionMultiplexer</c>. Call it before or after <c>AddInertia()</c>; it replaces the in-memory store.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configure">Sets the connection and key prefix.</param>
    public static IServiceCollection AddInertiaRedisCache(this IServiceCollection services, Action<InertiaRedisCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<InertiaRedisCacheOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.Replace(ServiceDescriptor.Singleton<IInertiaCacheStore, RedisInertiaCacheStore>());
        return services;
    }
}
