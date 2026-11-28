using Inertia.Net.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Inertia.Net;

/// <summary>Registers the PostgreSQL live backplane.</summary>
public static class InertiaPostgresBackplaneExtensions
{
    /// <summary>
    /// Carries live props between app instances and workers over PostgreSQL <c>LISTEN</c>/<c>NOTIFY</c>:
    /// <c>AddInertiaLive().AddSse().AddPostgresBackplane(o =&gt; o.ConnectionString = pg)</c> on every web instance, and
    /// <c>AddInertiaLive().AddPostgresBackplane(...)</c> in a worker that only publishes. Leave <c>ConnectionString</c> unset to use the
    /// registered <see cref="NpgsqlDataSource"/>. Inject <see cref="PostgresLiveBackplane"/> to publish inside a transaction.
    /// </summary>
    /// <param name="builder">The live-props builder.</param>
    /// <param name="configure">Sets the connection, channel and large-message handling.</param>
    public static IInertiaLiveBuilder AddPostgresBackplane(this IInertiaLiveBuilder builder, Action<InertiaPostgresBackplaneOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        var options = services.AddOptions<InertiaPostgresBackplaneOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.Validate(static o => IsValid(() => PostgresLiveBackplane.ValidChannel(o.Channel)), "InertiaPostgresBackplaneOptions.Channel must be a plain identifier (letters, digits, underscores; at most 63).")
            .Validate(static o => IsValid(() => PostgresLiveBackplane.ValidTableName(o.TableName, nameof(o.TableName))), "InertiaPostgresBackplaneOptions.TableName must be an identifier, optionally schema-qualified.")
            .Validate(static o => Enum.IsDefined(o.LargeMessages), "InertiaPostgresBackplaneOptions.LargeMessages is not a LargeMessageMode.")
            .Validate(static o => o.LargeMessageRetention > TimeSpan.Zero, "InertiaPostgresBackplaneOptions.LargeMessageRetention must be positive.")
            .Validate(static o => o.ReconnectDelayMax >= TimeSpan.FromSeconds(1), "InertiaPostgresBackplaneOptions.ReconnectDelayMax must be at least 1 second.")
            .ValidateOnStart();
        services.TryAddSingleton(static sp => new PostgresLiveBackplane(
            sp.GetRequiredService<IOptions<InertiaPostgresBackplaneOptions>>().Value, sp.GetService<NpgsqlDataSource>(), sp.GetRequiredService<ILoggerFactory>()));
        services.Replace(ServiceDescriptor.Singleton<IInertiaLiveBackplane>(static sp => sp.GetRequiredService<PostgresLiveBackplane>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PostgresLiveBackplaneService>());
        return builder;
    }

    private static bool IsValid(Action check)
    {
        try
        {
            check();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
