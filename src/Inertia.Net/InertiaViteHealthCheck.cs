using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inertia.Net;

/// <summary>Readiness check registration for published Vite files.</summary>
public static class InertiaViteHealthChecksBuilderExtensions
{
    /// <summary>Checks local Vite files reachable from the given manifest entries once per host. Register on a readiness probe.</summary>
    public static IHealthChecksBuilder AddInertiaVite(this IHealthChecksBuilder builder, IEnumerable<string> entries,
        string? mount = null, string? name = null, IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(entries);
        var entryKeys = entries.Distinct(StringComparer.Ordinal).ToArray();
        if (entryKeys.Length == 0 || entryKeys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty manifest entry is required.", nameof(entries));
        }

        var key = new object();
        builder.Services.AddKeyedSingleton<IHealthCheck>(key, (services, _) => new ViteHealthCheck(
            string.IsNullOrEmpty(mount) ? services.GetRequiredService<ViteAssets>() : services.GetRequiredKeyedService<ViteAssets>(mount),
            entryKeys));
        return builder.Add(new HealthCheckRegistration(name ?? (string.IsNullOrEmpty(mount) ? "inertia-vite" : "inertia-vite-" + mount),
            services => services.GetRequiredKeyedService<IHealthCheck>(key), failureStatus: null, tags));
    }

    private sealed class ViteHealthCheck(ViteAssets assets, string[] entries) : IHealthCheck
    {
        private readonly Lazy<HealthCheckResult> _result = new(() => Check(assets, entries));

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result.Value);

        private static HealthCheckResult Check(ViteAssets assets, string[] entries)
        {
            try
            {
                assets.CheckPublishedFiles(entries);
                return HealthCheckResult.Healthy("The published Inertia Vite assets are present.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                return HealthCheckResult.Unhealthy(ex.Message);
            }
        }
    }
}
