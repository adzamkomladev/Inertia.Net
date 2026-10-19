using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inertia.Net;

/// <summary>
/// Reports what the SSR gateway already knows, without calling the SSR servers: healthy while SSR is disabled, goes through the Vite
/// dev server, or every server is usable; degraded while some servers' circuits are open or some managed processes are not ready; the
/// registration's failure status when no server can take a render. A server that has not received a render since it went down is
/// reported healthy until renders fail.
/// </summary>
public sealed class InertiaSsrHealthCheck : IHealthCheck
{
    private readonly SsrGateway _gateway;

    internal InertiaSsrHealthCheck(SsrGateway gateway) => _gateway = gateway;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(Check(context.Registration.FailureStatus));
    }

    internal HealthCheckResult Check(HealthStatus failureStatus)
    {
        if (_gateway.Options.Mode == SsrMode.Disabled)
        {
            return HealthCheckResult.Healthy("Inertia SSR is disabled.");
        }

        if (_gateway.DevServerRunning)
        {
            return HealthCheckResult.Healthy("Inertia SSR goes through the Vite dev server.");
        }

        var endpoints = _gateway.Endpoints;
        var data = new Dictionary<string, object>(endpoints.Count);
        var usable = 0;
        foreach (var endpoint in endpoints)
        {
            var ready = endpoint.Instance is not { } instance || _gateway.IsInstanceReady(instance);
            var state = endpoint.Breaker.State;
            data[endpoint.Name] = ready ? InertiaMetrics.StateName(state) : "not-ready";
            if (ready && state != SsrCircuitState.Open)
            {
                usable++;
            }
        }

        var servers = _gateway.IsManaged ? "managed Inertia SSR processes" : "Inertia SSR servers";
        return usable == endpoints.Count
            ? HealthCheckResult.Healthy($"All {endpoints.Count} {servers} can take renders.", data)
            : usable == 0
                ? new HealthCheckResult(failureStatus, $"None of the {endpoints.Count} {servers} can take renders.", data: data)
                : HealthCheckResult.Degraded($"{usable} of {endpoints.Count} {servers} can take renders.", data: data);
    }
}

/// <summary>Health check registration for the Inertia SSR server.</summary>
public static class InertiaSsrHealthChecksBuilderExtensions
{
    /// <summary>Adds <see cref="InertiaSsrHealthCheck"/>. Requires <see cref="InertiaServiceCollectionExtensions.AddInertia"/>.</summary>
    public static IHealthChecksBuilder AddInertiaSsr(this IHealthChecksBuilder builder, string name = "inertia-ssr", HealthStatus? failureStatus = null, IEnumerable<string>? tags = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(new HealthCheckRegistration(name, static sp => new InertiaSsrHealthCheck(sp.GetRequiredService<SsrGateway>()), failureStatus, tags, timeout));
    }
}
