using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Reports healthy while the SSR server answers <c>GET {Ssr.Url}/health</c> with a 2xx status.</summary>
public sealed class InertiaSsrHealthCheck(IHttpClientFactory httpClients, IOptionsMonitor<InertiaOptions> options) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ssr = options.CurrentValue.Ssr;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ssr.Timeout);
        try
        {
            using var response = await httpClients.CreateClient(SsrGateway.HttpClientName).GetAsync($"{ssr.Url.TrimEnd('/')}/health", timeout.Token);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("The Inertia SSR server is up.")
                : new HealthCheckResult(context.Registration.FailureStatus, $"The Inertia SSR server responded with HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The Inertia SSR server is unreachable.", ex);
        }
    }
}

/// <summary>Health check registration for the Inertia SSR server.</summary>
public static class InertiaSsrHealthChecksBuilderExtensions
{
    /// <summary>Adds <see cref="InertiaSsrHealthCheck"/>. Requires <see cref="InertiaServiceCollectionExtensions.AddInertia"/>.</summary>
    public static IHealthChecksBuilder AddInertiaSsr(this IHealthChecksBuilder builder, string name = "inertia-ssr", HealthStatus? failureStatus = null, IEnumerable<string>? tags = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCheck<InertiaSsrHealthCheck>(name, failureStatus, tags, timeout);
    }
}
