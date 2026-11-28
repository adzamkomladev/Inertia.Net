using System.Threading.Channels;
using Inertia.Net.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
#if EFCORE
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
#endif

namespace Inertia.Net.Postgres.Tests;

/// <summary>
/// The PostgreSQL live backplane against a real server: set <c>POSTGRES_URL</c> (an Npgsql connection string, e.g.
/// <c>Host=localhost;Username=postgres;Password=postgres</c>); without it the tests are skipped. Each test uses its own channel and table.
/// </summary>
public sealed class PostgresBackplaneTests : IAsyncLifetime
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("POSTGRES_URL");
    private static readonly LiveEvent StatsChanged = new("StatsChanged", LiveChannel.Public("stats"));
    private readonly string _id = Guid.NewGuid().ToString("N")[..12];
    private readonly List<ServiceProvider> _hosts = [];

    private string Channel => "t_" + _id;

    private string Table => "t_live_" + _id;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingTransport : IInertiaLiveLocalTransport
    {
        public Channel<LiveMessage> Delivered { get; } = System.Threading.Channels.Channel.CreateUnbounded<LiveMessage>();

        public Channel<bool> Resyncs { get; } = System.Threading.Channels.Channel.CreateUnbounded<bool>();

        public ValueTask DeliverAsync(LiveMessage message, CancellationToken cancellationToken = default)
        {
            Delivered.Writer.TryWrite(message);
            return default;
        }

        public ValueTask ResyncAsync(CancellationToken cancellationToken = default)
        {
            Resyncs.Writer.TryWrite(true);
            return default;
        }

        public async Task<LiveMessage> NextAsync(int seconds = 10)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            return await Delivered.Reader.ReadAsync(timeout.Token);
        }

        public async Task NothingAsync()
        {
            await Task.Delay(300, Ct);
            Assert.False(Delivered.Reader.TryRead(out var message), $"Unexpected {message?.Event}");
        }
    }

    private async Task<ServiceProvider> HostAsync(RecordingTransport? transport, Action<InertiaPostgresBackplaneOptions>? configure = null)
    {
        Assert.SkipWhen(Url is null, "POSTGRES_URL is not set");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        if (transport is not null) services.AddSingleton<IInertiaLiveLocalTransport>(transport);
        services.AddInertiaLive().AddPostgresBackplane(o =>
        {
            o.ConnectionString = Url;
            o.Channel = Channel;
            o.TableName = Table;
            configure?.Invoke(o);
        });
        var provider = services.BuildServiceProvider();
        _hosts.Add(provider);
        foreach (var hosted in provider.GetServices<IHostedService>()) await hosted.StartAsync(Ct);
        return provider;
    }

    public ValueTask InitializeAsync() => default;

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            foreach (var hosted in host.GetServices<IHostedService>()) await hosted.StopAsync(CancellationToken.None);
            await host.DisposeAsync();
        }

        if (Url is not null) await ExecAsync($"DROP TABLE IF EXISTS {Table}");
    }

    private static async Task<object?> ExecAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Url);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private static IInertiaLive Live(IServiceProvider host) => host.GetRequiredService<IInertiaLive>();

    private static int? Value(LiveMessage message, string name = "n") =>
        message.Payload.GetProperty("__inertia").TryGetProperty("props", out var props) ? props.GetProperty(name).GetInt32() : null;

    private static bool HasValues(LiveMessage message) => message.Payload.GetProperty("__inertia").TryGetProperty("props", out _);

    private static Dictionary<string, int> N(int n) => new() { ["n"] = n };

    [Fact]
    public async Task One_event_from_a_publish_only_process_reaches_both_web_hosts_and_the_publisher_gets_its_own_once()
    {
        var (a, b) = (new RecordingTransport(), new RecordingTransport());
        var hostA = await HostAsync(a);
        await HostAsync(b);
        var worker = await HostAsync(null);

        await Live(worker).PublishAsync(StatsChanged, N(1), cancellationToken: Ct);
        Assert.Equal(1, Value(await a.NextAsync()));
        Assert.Equal(1, Value(await b.NextAsync()));

        await Live(hostA).PublishAsync(StatsChanged, N(2), cancellationToken: Ct);
        Assert.Equal(2, Value(await a.NextAsync()));
        Assert.Equal(2, Value(await b.NextAsync()));
        await a.NothingAsync();
    }

    [Fact]
    public async Task A_commit_delivers_in_order_a_rollback_delivers_nothing_and_nothing_arrives_before_the_commit()
    {
        var transport = new RecordingTransport();
        var host = await HostAsync(transport);
        var backplane = host.GetRequiredService<PostgresLiveBackplane>();
        await using var connection = new NpgsqlConnection(Url);
        await connection.OpenAsync(Ct);

        await using (var rollback = await connection.BeginTransactionAsync(Ct))
        {
            await backplane.PublishAsync(Live(host).CreateMessage(StatsChanged, N(0)), connection, rollback, Ct);
            await rollback.RollbackAsync(Ct);
        }

        await using (var commit = await connection.BeginTransactionAsync(Ct))
        {
            await backplane.PublishAsync(Live(host).CreateMessage(StatsChanged, N(1)), connection, commit, Ct);
            await backplane.PublishAsync(Live(host).CreateMessage(StatsChanged, N(2)), connection, commit, Ct);
            await transport.NothingAsync(); // not even locally, before the commit
            await commit.CommitAsync(Ct);
        }

        Assert.Equal(1, Value(await transport.NextAsync()));
        Assert.Equal(2, Value(await transport.NextAsync()));
        await transport.NothingAsync(); // exactly once each, and the rolled-back one never
    }

[Fact]
    public async Task Identical_publishes_in_one_transaction_are_all_delivered_so_the_last_value_wins()
    {
        var transport = new RecordingTransport();
        var host = await HostAsync(transport);
        var backplane = host.GetRequiredService<PostgresLiveBackplane>();
        await using var connection = new NpgsqlConnection(Url);
        await connection.OpenAsync(Ct);
        await using (var tx = await connection.BeginTransactionAsync(Ct))
        {
            // PostgreSQL folds identical notifications of one transaction; 1 → 2 → 1 must not end at 2.
            foreach (var n in new[] { 1, 2, 1 }) await backplane.PublishAsync(Live(host).CreateMessage(StatsChanged, N(n)), connection, tx, Ct);
            await tx.CommitAsync(Ct);
        }

        Assert.Equal(1, Value(await transport.NextAsync()));
        Assert.Equal(2, Value(await transport.NextAsync()));
        Assert.Equal(1, Value(await transport.NextAsync()));
    }

    [Fact]
    public async Task A_transaction_of_another_connection_or_one_already_finished_is_refused()
    {
        var host = await HostAsync(new RecordingTransport());
        var backplane = host.GetRequiredService<PostgresLiveBackplane>();
        await using var a = new NpgsqlConnection(Url);
        await using var b = new NpgsqlConnection(Url);
        await a.OpenAsync(Ct);
        await b.OpenAsync(Ct);
        var message = Live(host).CreateMessage(StatsChanged);

        await using (var other = await b.BeginTransactionAsync(Ct))
        {
            await Assert.ThrowsAsync<ArgumentException>(async () => await backplane.PublishAsync(message, a, other, Ct));
        }

        var finished = await a.BeginTransactionAsync(Ct);
        await finished.CommitAsync(Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await backplane.PublishAsync(message, a, finished, Ct)); // Npgsql: already completed
        await finished.DisposeAsync();
    }

#if EFCORE
    private sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options);

    [Fact]
    public async Task A_transactional_publish_works_through_EF_Core()
    {
        var transport = new RecordingTransport();
        var host = await HostAsync(transport);
        var backplane = host.GetRequiredService<PostgresLiveBackplane>();
        await using var db = new TestDb(new DbContextOptionsBuilder<TestDb>().UseNpgsql(Url).Options);
        await using (var tx = await db.Database.BeginTransactionAsync(Ct))
        {
            await backplane.PublishAsync(Live(host).CreateMessage(StatsChanged, N(7)), db.Database.GetDbConnection(), tx.GetDbTransaction(), Ct);
            await transport.NothingAsync();
            await tx.CommitAsync(Ct);
        }

        Assert.Equal(7, Value(await transport.NextAsync()));
    }
#endif

    [Fact]
    public async Task A_100_KB_payload_round_trips_through_the_table_and_a_rolled_back_one_leaves_no_row()
    {
        var transport = new RecordingTransport();
        var host = await HostAsync(transport);
        var publisher = await HostAsync(null);
        var big = new string('x', 100_000);

        await Live(publisher).PublishAsync(StatsChanged, new Dictionary<string, string> { ["big"] = big }, cancellationToken: Ct);
        var message = await transport.NextAsync();
        Assert.Equal(big, message.Payload.GetProperty("__inertia").GetProperty("props").GetProperty("big").GetString());
        Assert.Equal(1L, await ExecAsync($"SELECT count(*) FROM {Table}"));

        await using var connection = new NpgsqlConnection(Url);
        await connection.OpenAsync(Ct);
        await using (var tx = await connection.BeginTransactionAsync(Ct))
        {
            await host.GetRequiredService<PostgresLiveBackplane>().PublishAsync(
                Live(host).CreateMessage(StatsChanged, new Dictionary<string, string> { ["big"] = big }), connection, tx, Ct);
            await tx.RollbackAsync(Ct);
        }

        Assert.Equal(1L, await ExecAsync($"SELECT count(*) FROM {Table}"));
        await transport.NothingAsync();
    }

    [Fact]
    public async Task The_sweep_deletes_old_rows_only_and_two_instances_can_sweep_at_once()
    {
        var hostA = await HostAsync(null);
        var hostB = await HostAsync(null);
        await ExecAsync($"INSERT INTO {Table} (created_at, body) SELECT now() - interval '1 hour', '\\x00' FROM generate_series(1, 2500)");
        await ExecAsync($"INSERT INTO {Table} (body) VALUES ('\\x01')");

        var a = hostA.GetRequiredService<PostgresLiveBackplane>();
        var b = hostB.GetRequiredService<PostgresLiveBackplane>();
        var deleted = await Task.WhenAll(a.SweepAsync(Ct), b.SweepAsync(Ct));

        Assert.Equal(2500, deleted.Sum());
        Assert.Equal(1L, await ExecAsync($"SELECT count(*) FROM {Table}"));
    }

    [Fact]
    public async Task A_row_deleted_before_the_listener_reads_it_arrives_without_values()
    {
        var transport = new RecordingTransport();
        var host = await HostAsync(transport);
        var message = LiveWire.WithoutValues(Live(host).CreateMessage(StatsChanged));
        // A reference to a row that is not there, as after a sweep or a failover.
        await ExecAsync($"SELECT pg_notify('{Channel}', '{System.Text.Encoding.UTF8.GetString(LiveWire.Write(message, "elsewhere", reference: 999_999))}')");

        var received = await transport.NextAsync();
        Assert.Equal("StatsChanged", received.Event);
        Assert.False(HasValues(received));
    }

    [Fact]
    public async Task Reload_mode_sends_oversize_messages_without_values_and_never_creates_the_table()
    {
        var transport = new RecordingTransport();
        await HostAsync(transport);
        var publisher = await HostAsync(null, o => o.LargeMessages = LargeMessageMode.Reload);

        await Live(publisher).PublishAsync(StatsChanged, new Dictionary<string, string> { ["big"] = new('x', 10_000) }, cancellationToken: Ct);

        Assert.False(HasValues(await transport.NextAsync()));
        var publisherOnly = await HostAsync(null, o => (o.LargeMessages, o.TableName) = (LargeMessageMode.Reload, Table + "_reload"));
        await Live(publisherOnly).PublishAsync(StatsChanged, new Dictionary<string, string> { ["big"] = new('x', 10_000) }, cancellationToken: Ct);
        Assert.Equal(true, await ExecAsync($"SELECT to_regclass('{Table}_reload') IS NULL"));
    }

    [Fact]
    public async Task Without_AutoCreateTable_a_large_publish_points_at_CreateTableSql_until_it_is_run()
    {
        var publisher = await HostAsync(null, o => o.AutoCreateTable = false);
        var large = new Dictionary<string, string> { ["big"] = new('x', 10_000) };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Live(publisher).PublishAsync(StatsChanged, large, cancellationToken: Ct).AsTask());
        Assert.Contains("CreateTableSql", error.Message, StringComparison.Ordinal);

        await ExecAsync(PostgresLiveBackplane.CreateTableSql(Table));
        await ExecAsync(PostgresLiveBackplane.CreateTableSql(Table)); // safe twice
        await Live(publisher).PublishAsync(StatsChanged, large, cancellationToken: Ct);
        Assert.Equal(1L, await ExecAsync($"SELECT count(*) FROM {Table}"));
        Assert.Equal("u", await ExecAsync($"SELECT relpersistence::text FROM pg_class WHERE oid = '{Table}'::regclass"));
    }

    [Theory]
    [InlineData("Channel", "bad-channel")]
    [InlineData("Channel", "x\"; DROP TABLE y; --")]
    [InlineData("TableName", "a.b.c")]
    [InlineData("TableName", "t; DROP TABLE y")]
    public async Task TableName_and_Channel_reject_non_identifiers_at_startup(string option, string value)
    {
        Assert.SkipWhen(Url is null, "POSTGRES_URL is not set");
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddInertiaLive().AddPostgresBackplane(o =>
        {
            o.ConnectionString = Url;
            if (option == "Channel") o.Channel = value;
            else o.TableName = value;
        });
        using var host = builder.Build();
        await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync(Ct));
        Assert.Throws<ArgumentException>(() => PostgresLiveBackplane.CreateTableSql(value));
    }

    [Fact]
    public async Task Terminating_the_listen_connection_fires_a_gap_and_the_next_event_arrives()
    {
        var transport = new RecordingTransport();
        var application = "inertia-listen-" + _id;
        await HostAsync(transport, o => o.ListenConnectionString = Url + ";Application Name=" + application);
        var publisher = await HostAsync(null);

        Assert.Equal(true, await ExecAsync($"SELECT bool_and(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE application_name = '{application}'"));
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await transport.Resyncs.Reader.ReadAsync(timeout.Token);
        }

        await Live(publisher).PublishAsync(StatsChanged, N(5), cancellationToken: Ct);
        Assert.Equal(5, Value(await transport.NextAsync()));
    }

    [Fact]
    public async Task ListenConnectionString_is_used_for_listening_and_ConnectionString_for_publishing()
    {
        var transport = new RecordingTransport();
        await HostAsync(transport, o =>
        {
            o.ConnectionString = Url + ";Application Name=publish-" + _id;
            o.ListenConnectionString = Url + ";Application Name=listen-" + _id;
        });

        Assert.Equal(1L, await ExecAsync($"SELECT count(*) FROM pg_stat_activity WHERE application_name = 'listen-{_id}' AND query ILIKE 'LISTEN%'"));
        Assert.Equal(0L, await ExecAsync($"SELECT count(*) FROM pg_stat_activity WHERE application_name = 'publish-{_id}' AND query ILIKE 'LISTEN%'"));
    }

    [Fact]
    public async Task Quotes_backslashes_and_unicode_arrive_unchanged()
    {
        var transport = new RecordingTransport();
        var host = await HostAsync(transport);
        var text = "it's \"quoted\" \\ back\\slash '); DROP TABLE x; -- Ünïcødé ✓ 🚀";

        await Live(host).PublishAsync(StatsChanged, new Dictionary<string, string> { ["t"] = text }, cancellationToken: Ct);
        var publisher = await HostAsync(null);
        await Live(publisher).PublishAsync(StatsChanged, new Dictionary<string, string> { ["t"] = text }, cancellationToken: Ct);

        foreach (var _ in new[] { 1, 2 })
        {
            var message = await transport.NextAsync();
            Assert.Equal(text, message.Payload.GetProperty("__inertia").GetProperty("props").GetProperty("t").GetString());
        }
    }

    [Fact]
    public void CreateTableSql_is_unlogged_by_default_and_logged_when_durable()
    {
        Assert.Contains("CREATE UNLOGGED TABLE IF NOT EXISTS live.messages", PostgresLiveBackplane.CreateTableSql("live.messages"), StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS inertia_live_messages", PostgresLiveBackplane.CreateTableSql(durable: true), StringComparison.Ordinal);
    }
}
