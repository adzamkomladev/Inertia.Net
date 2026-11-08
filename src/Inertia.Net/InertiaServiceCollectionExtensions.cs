using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Registers Inertia.Net services.</summary>
public static class InertiaServiceCollectionExtensions
{
#pragma warning disable RS0027 // the shipped overload keeps its optional parameter; the named overload is new
    /// <summary>Adds the Inertia.Net services and the options of the default mount.</summary>
    public static IServiceCollection AddInertia(this IServiceCollection services, Action<InertiaOptions>? configure = null) =>
        AddInertia(services, Options.DefaultName, configure);
#pragma warning restore RS0027

    /// <summary>
    /// Adds the Inertia.Net services and the options of the mount <paramref name="name"/>: its own shared props, asset version,
    /// state cookie (default <c>.Inertia.State.{name}</c>), root view, Vite manifest and SSR. Select the mount with
    /// <c>app.UseInertia(name)</c>, usually in an <c>app.Map("/path", ...)</c> branch. A keyed <see cref="IInertiaRootView"/>
    /// registered under <paramref name="name"/> replaces the mount's root template. <c>""</c> is the default mount.
    /// Calling it again for the same name adds to that mount's configuration.
    /// </summary>
    public static IServiceCollection AddInertia(this IServiceCollection services, string name, Action<InertiaOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        var options = services.AddOptions<InertiaOptions>(name);
        if (GetMounts(services).Names.Add(name) && name.Length > 0)
        {
            options.Configure(o =>
            {
                o.Mount = name;
                o.State.CookieName = InertiaStateOptions.DefaultCookieName + "." + name;
                o.State.PreviousUrlCookieName = InertiaStateOptions.DefaultPreviousUrlCookieName + "." + name;
            });
            AddMount(services, name);
        }

        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InertiaOptions>, InertiaOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddMetrics();
        services.TryAddSingleton<InertiaMetrics>();
        services.TryAddSingleton<InertiaRefreshQueue>();
        services.AddHostedService(sp => sp.GetRequiredService<InertiaRefreshQueue>());
        services.TryAddSingleton<ViteAssets>();
        services.TryAddSingleton<IInertiaRootView, RootTemplateView>();
        services.AddMemoryCache(); // the default cache store keeps InertiaProp.Cache() values in it
        services.TryAddSingleton<IInertiaCacheStore, MemoryInertiaCacheStore>();
        services.TryAddSingleton<InertiaCaching>();
        services.TryAddSingleton<IInertiaCache, InertiaCache>();
        services.TryAddSingleton<InertiaPageWriter>();
        services.TryAddSingleton<PageComponentCheck>();
        // Ssr.Timeout bounds the render; Ssr.ConnectTimeout also bounds connection establishment. No redirects (a render POST must not be replayed elsewhere), no proxy (renderers are local or
        // on a private network), and pooled connections are recycled so a remote renderer's DNS changes are picked up. The default
        // primary handler is set first, so a handler the app configures for this client (before or after AddInertia) replaces it.
        services.AddHttpClient(SsrGateway.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.Configure<HttpClientFactoryOptions>(SsrGateway.HttpClientName, o => o.HttpMessageHandlerBuilderActions.Insert(0, static builder =>
            builder.PrimaryHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectTimeout = builder.Services.GetRequiredService<IOptions<InertiaOptions>>().Value.Ssr.ConnectTimeout,
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

    private static InertiaMounts GetMounts(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(InertiaMounts) && !d.IsKeyedService)?.ImplementationInstance is InertiaMounts mounts)
        {
            return mounts;
        }

        mounts = new InertiaMounts();
        services.AddSingleton(mounts);
        return mounts;
    }

    // The per-mount services of a named mount, keyed by its name; the default mount keeps the unkeyed registrations above.
    private static void AddMount(IServiceCollection services, string name)
    {
        services.AddKeyedSingleton(name, (sp, _) => new ViteAssets(MountOptions(sp, name), sp.GetRequiredService<IHostEnvironment>(), sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedSingleton(name, (sp, _) => new VersionProvider(MountOptions(sp, name), sp.GetRequiredKeyedService<ViteAssets>(name)));
        services.AddKeyedSingleton(name, (sp, _) => new PageComponentCheck(MountOptions(sp, name), sp.GetRequiredService<IHostEnvironment>(), sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedSingleton(name, (sp, _) => ActivatorUtilities.CreateInstance<SsrNodeProcess>(sp, MountOptions(sp, name), sp.GetRequiredKeyedService<ViteAssets>(name)));
        services.AddKeyedSingleton(name, (sp, _) => ActivatorUtilities.CreateInstance<SsrGateway>(sp,
            MountOptions(sp, name), sp.GetRequiredKeyedService<ViteAssets>(name), sp.GetRequiredKeyedService<SsrNodeProcess>(name)));
        services.AddKeyedSingleton<IInertiaSsrRenderer>(name, (sp, _) => sp.GetRequiredKeyedService<SsrGateway>(name));
        services.AddKeyedSingleton(name, (sp, _) => ActivatorUtilities.CreateInstance<InertiaPageWriter>(sp,
            MountOptions(sp, name), sp.GetRequiredKeyedService<VersionProvider>(name), sp.GetRequiredKeyedService<PageComponentCheck>(name)));
        services.TryAddKeyedSingleton<IInertiaRootView>(name, (sp, _) => new RootTemplateView(sp.GetRequiredService<IHostEnvironment>(),
            sp.GetRequiredKeyedService<ViteAssets>(name), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredKeyedService<IInertiaSsrRenderer>(name)));
        services.TryAddKeyedSingleton<IInertiaStateStore>(name, (sp, _) => MountOptions(sp, name).Value.State.UsesSession
            ? ActivatorUtilities.CreateInstance<SessionInertiaStateStore>(sp, MountOptions(sp, name))
            : ActivatorUtilities.CreateInstance<CookieInertiaStateStore>(sp, MountOptions(sp, name)));
        // Not AddHostedService: it skips a second hosted service of the same type, and each mount needs its own.
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredKeyedService<SsrNodeProcess>(name));
        services.AddSingleton<IHostedService>(sp => ActivatorUtilities.CreateInstance<InertiaTypeScriptWriter>(sp,
            MountOptions(sp, name), sp.GetRequiredKeyedService<InertiaPageWriter>(name)));
    }

    private static IOptions<InertiaOptions> MountOptions(IServiceProvider services, string name) =>
        Options.Create(services.GetRequiredService<IOptionsMonitor<InertiaOptions>>().Get(name));

#pragma warning disable RS0027 // the shipped overload keeps its optional parameter; the named overload is new
    /// <summary>
    /// Registers Inertia error page options and an exception handler. Also call <c>AddInertia()</c>.
    /// Register application exception handlers first if they should take precedence.
    /// </summary>
    public static IServiceCollection AddInertiaErrorPages(this IServiceCollection services, Action<InertiaErrorPagesOptions>? configure = null) =>
        AddInertiaErrorPages(services, Options.DefaultName, configure);
#pragma warning restore RS0027

    /// <summary>
    /// Registers the error pages of the mount <paramref name="name"/> (see <c>AddInertia(name, ...)</c>): its requests render
    /// these components. A named mount without its own error pages leaves its errors to the pipeline. Also call
    /// <c>app.UseInertiaErrorPages()</c> in the mount's branch, before <c>UseInertia(name)</c>, or once before the branch.
    /// </summary>
    public static IServiceCollection AddInertiaErrorPages(this IServiceCollection services, string name, Action<InertiaErrorPagesOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        var options = services.AddOptions<InertiaErrorPagesOptions>(name);
        if (configure is not null) options.Configure(configure);
        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InertiaErrorPagesOptions>, InertiaErrorPagesOptionsValidator>());
        if (services.FirstOrDefault(d => d.ServiceType == typeof(InertiaErrorPagesRegistration) && !d.IsKeyedService)?.ImplementationInstance is InertiaErrorPagesRegistration registration)
        {
            registration.Mounts.Add(name);
            return services;
        }

        services.AddSingleton(new InertiaErrorPagesRegistration { Mounts = { name } });
        // The built-in middleware needs a fallback even when every registered IExceptionHandler declines.
        services.AddProblemDetails();
        services.AddExceptionHandler<InertiaExceptionHandler>();
        return services;
    }

    /// <summary>
    /// Adds <see cref="IInertiaLive"/>, which publishes live-prop events. Chain a transport (<c>.AddSignalR()</c> from
    /// <c>Inertia.Net.Live.SignalR</c>) or register an <see cref="IInertiaLiveBroadcaster"/>. Safe to call more than once.
    /// </summary>
    public static IInertiaLiveBuilder AddInertiaLive(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IInertiaLive, InertiaLive>();
        return new InertiaLiveBuilder(services);
    }
}

internal sealed class InertiaErrorPagesRegistration
{
    public HashSet<string> Mounts { get; } = new(StringComparer.Ordinal);
}

/// <summary>The mount names registered with <c>AddInertia</c>; <c>""</c> is the default mount.</summary>
internal sealed class InertiaMounts
{
    public HashSet<string> Names { get; } = new(StringComparer.Ordinal);
}
