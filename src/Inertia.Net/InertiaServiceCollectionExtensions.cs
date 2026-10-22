using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Registers Inertia.Net services.</summary>
public static class InertiaServiceCollectionExtensions
{
    /// <summary>Adds the Inertia.Net services and options.</summary>
    public static IServiceCollection AddInertia(this IServiceCollection services, Action<InertiaOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<InertiaOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InertiaOptions>, InertiaOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddMetrics();
        services.TryAddSingleton<InertiaMetrics>();
        services.TryAddSingleton<ViteAssets>();
        services.TryAddSingleton<IInertiaRootView, RootTemplateView>();
        services.AddMemoryCache(); // the default cache store keeps InertiaProp.Cache() values in it
        services.TryAddSingleton<IInertiaCacheStore, MemoryInertiaCacheStore>();
        services.TryAddSingleton<InertiaCaching>();
        services.TryAddSingleton<IInertiaCache, InertiaCache>();
        services.TryAddSingleton<InertiaPageWriter>();
        services.TryAddSingleton<PageComponentCheck>();
        // Ssr.Timeout is the only deadline. No redirects (a render POST must not be replayed elsewhere), no proxy (renderers are local or
        // on a private network), and pooled connections are recycled so a remote renderer's DNS changes are picked up. The default
        // primary handler is set first, so a handler the app configures for this client (before or after AddInertia) replaces it.
        services.AddHttpClient(SsrGateway.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.Configure<HttpClientFactoryOptions>(SsrGateway.HttpClientName, o => o.HttpMessageHandlerBuilderActions.Insert(0, static builder =>
            builder.PrimaryHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            }));
        services.TryAddSingleton<SsrGateway>();
        services.TryAddSingleton<IInertiaSsrRenderer>(static sp => sp.GetRequiredService<SsrGateway>());
        services.TryAddSingleton<SsrNodeProcess>();
        services.AddHostedService(sp => sp.GetRequiredService<SsrNodeProcess>()); // no-op unless o.Ssr.UseNodeProcess()
        services.AddHostedService<InertiaTypeScriptWriter>(); // no-op unless o.TypeScript.OutputPath is set, in Development
        services.TryAddSingleton<VersionProvider>();
        services.AddDataProtection();
        services.TryAddSingleton<IInertiaStateStore>(sp => sp.GetRequiredService<IOptions<InertiaOptions>>().Value.State.UsesSession
            ? ActivatorUtilities.CreateInstance<SessionInertiaStateStore>(sp)
            : ActivatorUtilities.CreateInstance<CookieInertiaStateStore>(sp));
        return services;
    }
}
