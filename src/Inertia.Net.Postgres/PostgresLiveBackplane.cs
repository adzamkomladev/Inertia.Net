using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Inertia.Net.Postgres;

/// <summary>
/// PostgreSQL <c>LISTEN</c>/<c>NOTIFY</c> as the live backplane. Inject it to publish inside your own transaction
/// (<see cref="PublishAsync(LiveMessage, DbConnection, DbTransaction?, CancellationToken)"/>): the event then goes out only if the
/// transaction commits.
/// </summary>
public sealed partial class PostgresLiveBackplane : IInertiaLiveBackplane, IAsyncDisposable
{
    /// <summary>The largest message sent inline: PostgreSQL rejects <c>NOTIFY</c> payloads of 8,000 bytes or more.</summary>
    public const int NotifyLimit = 7900;

    private readonly InertiaPostgresBackplaneOptions _options;
    private readonly NpgsqlDataSource _source;
    private readonly NpgsqlDataSource _listenSource;
    private readonly bool _ownsSource, _ownsListenSource;
    private readonly ILogger _logger;
    private readonly string _table;
    private readonly SemaphoreSlim _tableGate = new(1, 1);
    private volatile bool _tableReady;

    internal PostgresLiveBackplane(InertiaPostgresBackplaneOptions options, NpgsqlDataSource? registered, ILoggerFactory loggers)
    {
        _options = options;
        _logger = loggers.CreateLogger("Inertia.Net.Live");
        _table = ValidTableName(options.TableName, nameof(options.TableName));
        ValidChannel(options.Channel);
        if (options.ConnectionString is { } connectionString)
        {
            _source = new NpgsqlSlimDataSourceBuilder(connectionString).EnableTransportSecurity().Build();
            _ownsSource = true;
        }
        else
        {
            _source = registered ?? throw new InvalidOperationException(
                "Set InertiaPostgresBackplaneOptions.ConnectionString or register an NpgsqlDataSource.");
        }

        // One long-lived connection: unpooled when built here, so it never goes back to a pool with LISTEN active, and with TCP
        // keepalive, so a half-open connection fails (and reconnects with a resync) instead of waiting forever.
        if ((options.ListenConnectionString ?? options.ConnectionString) is { } listen)
        {
            var builder = new NpgsqlConnectionStringBuilder(listen) { Pooling = false };
            if (builder.KeepAlive == 0) builder.KeepAlive = 30;
            _listenSource = new NpgsqlSlimDataSourceBuilder(builder.ConnectionString).EnableTransportSecurity().Build();
            _ownsListenSource = true;
        }
        else
        {
            _listenSource = _source;
        }
    }

    /// <summary>
    /// <see cref="NotifyLimit"/> in <see cref="LargeMessageMode.Reload"/> mode (larger messages go out without values); null in
    /// <see cref="LargeMessageMode.Table"/> mode, which moves large messages through the table itself.
    /// </summary>
    public int? MaxMessageBytes => _options.LargeMessages == LargeMessageMode.Reload ? NotifyLimit : null;

    /// <summary>
    /// The SQL that creates the large-message table, for migrations when <see cref="InertiaPostgresBackplaneOptions.AutoCreateTable"/> is
    /// off (EF Core: <c>migrationBuilder.Sql(PostgresLiveBackplane.CreateTableSql())</c>). Safe to run more than once.
    /// </summary>
    /// <param name="tableName">The table, optionally schema-qualified.</param>
    /// <param name="durable">A logged table instead of <c>UNLOGGED</c>.</param>
    public static string CreateTableSql(string tableName = "inertia_live_messages", bool durable = false)
    {
        var table = ValidTableName(tableName, nameof(tableName));
        var index = (table.Contains('.', StringComparison.Ordinal) ? table[(table.IndexOf('.', StringComparison.Ordinal) + 1)..] : table) + "_created_at_idx";
        return $"""
            CREATE {(durable ? "" : "UNLOGGED ")}TABLE IF NOT EXISTS {table} (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                created_at timestamptz NOT NULL DEFAULT now(),
                body bytea NOT NULL
            );
            CREATE INDEX IF NOT EXISTS {index} ON {table} (created_at);
            """;
    }

#pragma warning disable RS0026 // The transactional overload is told apart by its connection; the interface's keeps its own optional token.
    /// <inheritdoc />
    public async ValueTask PublishAsync(LiveChannel channel, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        if (UsesTable(message.Length)) await EnsureTableAsync(cancellationToken); // before renting the publish connection
        await using var connection = await _source.OpenConnectionAsync(cancellationToken);
        await SendAsync(connection, null, message, cancellationToken);
    }

    /// <summary>
    /// Publishes <paramref name="message"/> (from <see cref="IInertiaLive.CreateMessage(LiveEvent, string?)"/>) inside
    /// <paramref name="transaction"/>: PostgreSQL delivers it only when the transaction commits, and never if it rolls back. Every
    /// instance delivers it on commit, the publishing one included. With EF Core, pass <c>db.Database.GetDbConnection()</c> and
    /// <c>tx.GetDbTransaction()</c>.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="connection">An Npgsql connection; opened (and closed again) when it is closed.</param>
    /// <param name="transaction">The transaction to publish in, or null to publish at once.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    public async ValueTask PublishAsync(LiveMessage message, DbConnection connection, DbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (connection is not NpgsqlConnection npgsql)
        {
            throw new ArgumentException("The PostgreSQL backplane publishes through an NpgsqlConnection.", nameof(connection));
        }

        // A transaction of another provider or connection would let the notification autocommit; Npgsql itself refuses a finished one.
        NpgsqlTransaction? npgsqlTransaction = null;
        if (transaction is not null)
        {
            npgsqlTransaction = transaction as NpgsqlTransaction;
            if (npgsqlTransaction is null || !ReferenceEquals(npgsqlTransaction.Connection, npgsql))
            {
                throw new ArgumentException("The transaction must be an active NpgsqlTransaction of the given connection.", nameof(transaction));
            }
        }

        // A unique origin per message: PostgreSQL folds identical notifications of one transaction into one, which would drop the
        // last of 1 → 2 → 1. Every instance, the publishing one included, delivers it when PostgreSQL does (after the commit).
        var origin = "tx-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var data = LiveWire.Write(message, origin);
        if (data.Length > LiveWire.MaxMessageBytes || (data.Length > NotifyLimit && _options.LargeMessages == LargeMessageMode.Reload))
        {
            data = LiveWire.Write(LiveWire.WithoutValues(message), origin);
        }

        if (UsesTable(data.Length)) await EnsureTableAsync(cancellationToken);
        var opened = npgsql.State != System.Data.ConnectionState.Open;
        if (opened) await npgsql.OpenAsync(cancellationToken);
        try
        {
            await SendAsync(npgsql, npgsqlTransaction, data, cancellationToken);
        }
        finally
        {
            if (opened) await npgsql.CloseAsync();
        }
    }

#pragma warning restore RS0026

    private bool UsesTable(int length) => length > NotifyLimit && _options.LargeMessages == LargeMessageMode.Table;

    private async Task SendAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.Add(new NpgsqlParameter<string>("channel", _options.Channel));
        if (!UsesTable(data.Length))
        {
            command.CommandText = "SELECT pg_notify(@channel, @payload)";
            command.Parameters.Add(new NpgsqlParameter<string>("payload", Encoding.UTF8.GetString(data.Span)));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        // The body goes in the table, and the notification carries the event without values plus the row id, in one round trip
        // (and in the caller's transaction, so a rollback removes both).
        if (!LiveWire.TryRead(data.Span, out var message, out var origin, out _))
        {
            throw new ArgumentException("Not a live wire message.", nameof(data));
        }

        command.CommandText = $"""
            WITH m AS (INSERT INTO {_table} (body) VALUES (@body) RETURNING id)
            SELECT pg_notify(@channel, jsonb_set(@ref::jsonb, '{"{ref}"}', to_jsonb(m.id))::text) FROM m
            """;
        command.Parameters.Add(new NpgsqlParameter<byte[]>("body", NpgsqlDbType.Bytea) { TypedValue = data.ToArray() });
        command.Parameters.Add(new NpgsqlParameter<string>("ref", Encoding.UTF8.GetString(LiveWire.Write(LiveWire.WithoutValues(message), origin, reference: 0))));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _tableReady = false;
            throw new InvalidOperationException(
                $"The live message table {_table} does not exist. Run PostgresLiveBackplane.CreateTableSql(\"{_options.TableName}\") from a migration, set AutoCreateTable, or use LargeMessageMode.Reload.",
                exception);
        }
    }

    internal async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableReady || !_options.AutoCreateTable)
        {
            return;
        }

        await _tableGate.WaitAsync(cancellationToken);
        try
        {
            if (_tableReady) return;
            await using var connection = await _source.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(CreateTableSql(_options.TableName, _options.DurableLargeMessages), connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _tableReady = true;
        }
        finally
        {
            _tableGate.Release();
        }
    }

    /// <summary>Deletes stored messages older than the retention, in batches of 1,000. Safe to run on every instance at once.</summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        await using var connection = await _source.OpenConnectionAsync(cancellationToken);
        while (true)
        {
            await using var command = new NpgsqlCommand(
                $"DELETE FROM {_table} WHERE id IN (SELECT id FROM {_table} WHERE created_at < now() - make_interval(secs => @age) LIMIT 1000)", connection);
            command.Parameters.Add(new NpgsqlParameter<double>("age", _options.LargeMessageRetention.TotalSeconds));
            int deleted;
            try
            {
                deleted = await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                return total; // nothing stored yet, or the table is managed elsewhere
            }

            total += deleted;
            if (deleted < 1000) return total;
        }
    }

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> SubscribeAsync(Func<ReadOnlyMemory<byte>, ValueTask> deliver, Action missed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        ArgumentNullException.ThrowIfNull(missed);
        var listener = new Listener(this, deliver, missed);
        try
        {
            await listener.StartAsync(cancellationToken);
        }
        catch
        {
            await listener.DisposeAsync(); // a cancelled or failed start must not leave the listen loop running
            throw;
        }

        return listener;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_ownsListenSource) await _listenSource.DisposeAsync();
        if (_ownsSource) await _source.DisposeAsync();
        _tableGate.Dispose();
    }

    private async Task DispatchAsync(string payload, Func<ReadOnlyMemory<byte>, ValueTask> deliver, CancellationToken cancellationToken)
    {
        var data = Encoding.UTF8.GetBytes(payload);
        if (payload.Contains("\"ref\"", StringComparison.Ordinal) && LiveWire.TryRead(data, out _, out _, out var reference) && reference is { } id)
        {
            try
            {
                await using var connection = await _source.OpenConnectionAsync(cancellationToken);
                await using var command = new NpgsqlCommand($"SELECT body FROM {_table} WHERE id = @id", connection);
                command.Parameters.Add(new NpgsqlParameter<long>("id", id));
                // A missing row (swept, lost in a failover) is not an error: the reference itself goes out, without values.
                if (await command.ExecuteScalarAsync(cancellationToken) is byte[] body) data = body;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReadFailed(_logger, exception, id);
            }
        }

        await deliver(data);
    }

    private sealed class Listener(PostgresLiveBackplane backplane, Func<ReadOnlyMemory<byte>, ValueTask> deliver, Action missed) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        // The listen loop only queues notifications, so it keeps up with the server; one reader keeps them in order. A null item is a
        // gap, queued behind the messages before it, so their (older) values cannot overwrite what the clients reload after the gap.
        private const int Capacity = 10_000;
        private readonly LiveBackplaneInbox<string> _inbox = new(Capacity);
        private readonly TaskCompletionSource _firstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _listen, _dispatch;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _listen = Task.Run(() => ListenAsync(_stopping.Token), CancellationToken.None);
            _dispatch = Task.Run(() => DispatchAllAsync(_stopping.Token), CancellationToken.None);
            // Wait for the first LISTEN, so events published right after startup arrive; a failed first attempt keeps retrying.
            await _firstAttempt.Task.WaitAsync(cancellationToken);
        }

        private async Task ListenAsync(CancellationToken stopping)
        {
            var options = backplane._options;
            var delay = TimeSpan.FromSeconds(1);
            var attempts = 0;
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    await using var connection = await backplane._listenSource.OpenConnectionAsync(stopping);
                    connection.Notification += (_, e) =>
                    {
                        if (e.Channel == options.Channel) _inbox.Enqueue(e.Payload);
                    };
                    await using (var command = new NpgsqlCommand($"LISTEN \"{options.Channel}\"", connection))
                    {
                        await command.ExecuteNonQueryAsync(stopping);
                    }

                    // Any LISTEN after the first attempt follows a gap: messages sent meanwhile are lost.
                    if (attempts++ > 0)
                    {
                        LogListening(backplane._logger, options.Channel);
                        _inbox.Enqueue(null);
                    }

                    _firstAttempt.TrySetResult();
                    delay = TimeSpan.FromSeconds(1);
                    while (true) await connection.WaitAsync(stopping);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (!stopping.IsCancellationRequested)
                {
                    attempts++;
                    LogListenFailed(backplane._logger, exception, delay.TotalSeconds);
                    _firstAttempt.TrySetResult();
                    try
                    {
                        await Task.Delay(delay, stopping);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.ReconnectDelayMax.Ticks));
                }
            }
        }

        private async Task DispatchAllAsync(CancellationToken stopping)
        {
            try
            {
                await foreach (var payload in _inbox.Reader.ReadAllAsync(stopping))
                {
                    try
                    {
                        if (payload is null) missed();
                        else await backplane.DispatchAsync(payload, deliver, stopping);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !stopping.IsCancellationRequested)
                    {
                        LogDeliverFailed(backplane._logger, exception);
                    }
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                // Stopping.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            if (_listen is not null) await _listen;
            if (_dispatch is not null) await _dispatch;
            _stopping.Dispose();
        }
    }

    internal static string ValidTableName(string? name, string parameter) =>
        name is not null && TableNamePattern().IsMatch(name)
            ? name
            : throw new ArgumentException($"'{name}' is not a valid table name: use letters, digits and underscores, optionally schema-qualified (schema.table).", parameter);

    internal static string ValidChannel(string? name) =>
        name is not null && ChannelPattern().IsMatch(name)
            ? name
            : throw new ArgumentException($"'{name}' is not a valid channel name: use letters, digits and underscores (at most 63).", nameof(name));

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,62}(\.[A-Za-z_][A-Za-z0-9_]{0,62})?$")]
    private static partial Regex TableNamePattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex ChannelPattern();

    [LoggerMessage(20, LogLevel.Warning, "The PostgreSQL live backplane LISTEN connection failed; retrying in {Delay} s.")]
    private static partial void LogListenFailed(ILogger logger, Exception exception, double delay);

    [LoggerMessage(21, LogLevel.Information, "The PostgreSQL live backplane is listening on {Channel} again.")]
    private static partial void LogListening(ILogger logger, string channel);

    [LoggerMessage(22, LogLevel.Warning, "Reading stored live message {Id} failed; it goes out without values.")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, long id);

    [LoggerMessage(23, LogLevel.Error, "Handling a message from the PostgreSQL live backplane failed.")]
    private static partial void LogDeliverFailed(ILogger logger, Exception exception);

    [LoggerMessage(24, LogLevel.Warning, "Creating the live message table failed; large messages fail until it exists.")]
    internal static partial void LogCreateTableFailed(ILogger logger, Exception exception);

    [LoggerMessage(25, LogLevel.Warning, "Sweeping old live messages failed.")]
    internal static partial void LogSweepFailed(ILogger logger, Exception exception);

    internal ILogger Logger => _logger;

    internal InertiaPostgresBackplaneOptions Options => _options;
}

/// <summary>Creates the large-message table at startup and sweeps old rows every minute.</summary>
internal sealed class PostgresLiveBackplaneService(PostgresLiveBackplane backplane) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (backplane.Options.LargeMessages == LargeMessageMode.Table)
        {
            try
            {
                await backplane.EnsureTableAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                PostgresLiveBackplane.LogCreateTableFailed(backplane.Logger, exception); // retried on the first large publish
            }
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (backplane.Options.LargeMessages != LargeMessageMode.Table) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await backplane.SweepAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                PostgresLiveBackplane.LogSweepFailed(backplane.Logger, exception);
            }
        }
    }
}
