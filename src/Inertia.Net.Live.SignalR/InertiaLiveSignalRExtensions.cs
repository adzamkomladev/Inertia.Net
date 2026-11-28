using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Inertia.Net.Live.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Registers and maps the SignalR live-props transport.</summary>
public static class InertiaLiveSignalRExtensions
{
    internal const string HubPolicy = "Inertia.Net.Live.SignalR.ChannelAuthorizer";

    // Records when the unkeyed service was supplied by this adapter. Apps may also register
    // that interface themselves, either before or after AddSignalR.
    private sealed class DefaultRegistration
    {
        public ILiveChannelAuthorizer? Policy { get; set; }
    }

    internal static readonly ServiceDescriptor DefaultAuthorizer = ServiceDescriptor.Scoped<ILiveChannelAuthorizer>(static sp =>
        sp.GetRequiredService<DefaultRegistration>().Policy = sp.GetRequiredService<IOptions<InertiaLiveSignalROptions>>()
            .Value.AuthorizerFactory?.Invoke(sp) ?? DenyingLiveChannelAuthorizer.Instance);

    /// <summary>
    /// Delivers live props over SignalR: registers the hub's services (<c>services.AddSignalR()</c>, additive with the app's own hubs), the
    /// JSON metadata the hub needs under Native AOT, this instance's local transport and the channel authorizer. Map the hub with
    /// <see cref="MapInertiaLiveHub"/>. With several instances or a separate worker, add an Inertia backplane (<c>.AddRedisBackplane()</c>,
    /// <c>.AddPostgresBackplane()</c>) instead of SignalR's own: registering both fails at startup, as every event would arrive twice.
    /// </summary>
    public static IInertiaLiveBuilder AddSignalR(this IInertiaLiveBuilder builder, Action<InertiaLiveSignalROptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        var options = services.AddOptions<InertiaLiveSignalROptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.Validate(o => o.MaxSubscriptionsPerConnection > 0,
            "InertiaLiveSignalROptions.MaxSubscriptionsPerConnection must be positive.")
            .Validate(o => o.MaxChannelNameLength > 0,
                "InertiaLiveSignalROptions.MaxChannelNameLength must be positive.")
            .ValidateOnStart();

        services.AddSignalR().AddJsonProtocol(o =>
        {
            var chain = o.PayloadSerializerOptions.TypeInfoResolverChain;
            if (chain.Contains(InertiaLiveJsonContext.Default))
            {
                return;
            }

            // An empty chain means reflection for the app's other hubs; adding a context alone would switch that off.
            var hadNone = chain.Count == 0 && o.PayloadSerializerOptions.TypeInfoResolver is null;
            chain.Insert(0, InertiaLiveJsonContext.Default);
            if (hadNone && JsonSerializer.IsReflectionEnabledByDefault)
            {
                chain.Add(ReflectionResolver());
            }
        });
        services.TryAddSingleton<SignalRLiveConnections>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInertiaLiveLocalTransport, SignalRLiveTransport>());
        services.TryAddSingleton<LiveOrigin>();
        services.TryAddScoped<DefaultRegistration>();
        services.TryAdd(DefaultAuthorizer);
        services.TryAddKeyedScoped<ILiveChannelAuthorizer>(HubPolicy, static (sp, _) =>
        {
            var registered = sp.GetRequiredService<ILiveChannelAuthorizer>();
            if (!ReferenceEquals(registered, sp.GetRequiredService<DefaultRegistration>().Policy) &&
                sp.GetRequiredService<IOptions<InertiaLiveSignalROptions>>().Value.AuthorizerFactory is { } configure)
            {
                return configure(sp);
            }

            // The unkeyed registration owns this instance, including disposal at the end of the invocation.
            return new DelegateLiveChannelAuthorizer(registered.AuthorizeAsync);
        });
        return builder;
    }

    // Trimmed and AOT apps turn IsReflectionEnabledByDefault off, and the trimmer removes this call with the branch.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only called when JsonSerializer.IsReflectionEnabledByDefault is on.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only called when JsonSerializer.IsReflectionEnabledByDefault is on.")]
    private static DefaultJsonTypeInfoResolver ReflectionResolver() => new();

    /// <summary>
    /// Maps <see cref="InertiaLiveHub"/> at <paramref name="pattern"/>. Connections are closed when their authentication expires, and a
    /// request whose <c>Origin</c> is neither the app's own nor in <see cref="InertiaLiveSignalROptions.AllowedOrigins"/> gets 403. That
    /// origin check stands in for CSRF protection, so <c>UseInertiaAntiforgery()</c> does not validate the hub's requests (the SignalR
    /// client sends no <c>X-XSRF-TOKEN</c> with its negotiate POST). Chain <c>RequireAuthorization()</c> to require a signed-in user.
    /// </summary>
    public static HubEndpointConventionBuilder MapInertiaLiveHub(this IEndpointRouteBuilder endpoints, string pattern = "/inertia-live")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<InertiaLiveSignalROptions>>();
        var origin = endpoints.ServiceProvider.GetRequiredService<LiveOrigin>();
        var builder = endpoints.MapHub<InertiaLiveHub>(pattern, o => o.CloseOnAuthenticationExpiration = true);
        builder.DisableAntiforgery();
        builder.Add(endpoint =>
        {
            var next = endpoint.RequestDelegate!;
            endpoint.RequestDelegate = context =>
            {
                if (origin.IsAllowed(context.Request, options.Value.AllowedOrigins))
                {
                    return next(context);
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        return builder;
    }
}
