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
    /// <summary>
    /// Delivers live props over SignalR: registers the hub's services (<c>services.AddSignalR()</c>, additive with the app's own hubs), the
    /// JSON metadata the hub needs under Native AOT, the broadcaster and the channel authorizer. Map the hub with <see cref="MapInertiaLiveHub"/>.
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

        services.AddSignalR().AddJsonProtocol(o =>
        {
            if (!o.PayloadSerializerOptions.TypeInfoResolverChain.Contains(InertiaLiveJsonContext.Default))
            {
                o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, InertiaLiveJsonContext.Default);
            }
        });
        services.TryAddSingleton<IInertiaLiveBroadcaster, SignalRLiveBroadcaster>();
        services.TryAddScoped<ILiveChannelAuthorizer>(static sp =>
            sp.GetRequiredService<IOptions<InertiaLiveSignalROptions>>().Value.AuthorizerFactory?.Invoke(sp) ?? DenyingLiveChannelAuthorizer.Instance);
        return builder;
    }

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
        var builder = endpoints.MapHub<InertiaLiveHub>(pattern, o => o.CloseOnAuthenticationExpiration = true);
        builder.DisableAntiforgery();
        builder.Add(endpoint =>
        {
            var next = endpoint.RequestDelegate!;
            endpoint.RequestDelegate = context =>
            {
                if (LiveOrigin.IsAllowed(context.Request, options.Value.AllowedOrigins))
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
