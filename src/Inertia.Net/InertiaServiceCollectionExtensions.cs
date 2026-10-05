using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ViteAssets>();
        services.TryAddSingleton<IInertiaRootView, RootTemplateView>();
        services.TryAddSingleton<InertiaPageWriter>();
        services.AddHttpClient(SsrGateway.HttpClientName);
        services.TryAddSingleton<IInertiaSsrRenderer, SsrGateway>();
        services.TryAddSingleton<VersionProvider>();
        services.AddDataProtection();
        services.TryAddSingleton<IInertiaStateStore>(sp => sp.GetRequiredService<IOptions<InertiaOptions>>().Value.State.UsesSession
            ? ActivatorUtilities.CreateInstance<SessionInertiaStateStore>(sp)
            : ActivatorUtilities.CreateInstance<CookieInertiaStateStore>(sp));
        return services;
    }
}
