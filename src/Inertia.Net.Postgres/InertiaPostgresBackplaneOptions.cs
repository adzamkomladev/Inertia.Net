using Npgsql;

namespace Inertia.Net.Postgres;

/// <summary>What the PostgreSQL backplane does with a message over the 8,000-byte <c>NOTIFY</c> limit.</summary>
public enum LargeMessageMode
{
    /// <summary>Stores the message in <see cref="InertiaPostgresBackplaneOptions.TableName"/> and notifies a reference to it, so values are never dropped.</summary>
    Table,

    /// <summary>Sends the event without values (clients reload the prop) and never touches a table: for roles that cannot create one.</summary>
    Reload,
}

/// <summary>Settings of the PostgreSQL live backplane (see <c>AddPostgresBackplane()</c>).</summary>
public sealed class InertiaPostgresBackplaneOptions
{
    /// <summary>The connection string publishes (and table reads) use. Null uses the <see cref="NpgsqlDataSource"/> registered in the services.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The connection string of the one dedicated <c>LISTEN</c> connection per process; defaults to <see cref="ConnectionString"/>.
    /// <c>LISTEN</c> needs a session: behind PgBouncer in transaction mode (or a managed pooler such as Supabase's, Neon's or RDS Proxy),
    /// point this at the database directly or at a session-mode pool. Publishing works through any pool.
    /// </summary>
    public string? ListenConnectionString { get; set; }

    /// <summary>The notification channel, one per app sharing a database. Default <c>inertia_live</c>; must be a plain identifier.</summary>
    public string Channel { get; set; } = "inertia_live";

    /// <summary>What happens to a message over the <c>NOTIFY</c> limit. Default <see cref="LargeMessageMode.Table"/>.</summary>
    public LargeMessageMode LargeMessages { get; set; } = LargeMessageMode.Table;

    /// <summary>The table large messages go through. Default <c>inertia_live_messages</c>; a schema-qualified name (<c>live.messages</c>) is allowed.</summary>
    public string TableName { get; set; } = "inertia_live_messages";

    /// <summary>
    /// Creates the table at startup (<c>CREATE UNLOGGED TABLE IF NOT EXISTS</c>). Default true. Set false when migrations manage the
    /// schema, and run <see cref="PostgresLiveBackplane.CreateTableSql"/> from one.
    /// </summary>
    public bool AutoCreateTable { get; set; } = true;

    /// <summary>
    /// Makes the table a logged one. Default false: live props need no crash safety, and an <c>UNLOGGED</c> table skips the WAL. After a
    /// crash or replica promotion its rows are gone, and those events go out without values (clients reload the prop).
    /// </summary>
    public bool DurableLargeMessages { get; set; }

    /// <summary>How long a stored message is kept before the sweep (every minute, on every instance) deletes it. Default 5 minutes.</summary>
    public TimeSpan LargeMessageRetention { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The cap of the listen reconnect backoff (1 second, doubling). Default 30 seconds.</summary>
    public TimeSpan ReconnectDelayMax { get; set; } = TimeSpan.FromSeconds(30);
}
