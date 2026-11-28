# Inertia.Net.Postgres

A PostgreSQL backplane for [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net) [live props](https://github.com/adzamkomladev/Inertia.Net#live-props).
It carries every published event to every app instance and worker over `LISTEN`/`NOTIFY`, so you can run several instances with
the database you already have. It can publish inside your transaction, so clients hear about a change only once it commits.
Native AOT compatible.

```sh
dotnet add package Inertia.Net.Postgres
```

## Setup

```csharp
// Web app: a transport, then the backplane.
builder.Services.AddInertiaLive().AddSse().AddPostgresBackplane(o =>
    o.ConnectionString = builder.Configuration.GetConnectionString("Db"));

// Worker that only publishes: no transport.
builder.Services.AddInertiaLive().AddPostgresBackplane(o => o.ConnectionString = connectionString);
```

`.AddSignalR()` works the same way. Leave `ConnectionString` unset to use a registered `NpgsqlDataSource`; the `LISTEN`
connection then stays rented from its pool, so allow one extra connection, and enable `Keepalive` there.

## Publish on commit

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);
order.Status = OrderStatus.Shipped;
await db.SaveChangesAsync(ct);
await postgres.PublishAsync(live.CreateMessage(OrderEvents.Updated(order.Id)), db.Database.GetDbConnection(), tx.GetDbTransaction(), ct);
await tx.CommitAsync(ct);
```

`postgres` is the `PostgresLiveBackplane` singleton. PostgreSQL delivers the notification when the transaction commits, and drops it
on rollback, and each message carries a unique id so PostgreSQL never folds two of them. The connection must be an
`NpgsqlConnection` and the transaction an active `NpgsqlTransaction` of it; a closed connection is opened and closed for the call.

## Options

| Option | Default | Meaning |
|---|---|---|
| `ConnectionString` | none | Used for publishing and, unless `ListenConnectionString` is set, for listening |
| `ListenConnectionString` | `ConnectionString` | A direct connection for `LISTEN`. Use it when `ConnectionString` goes through PgBouncer in transaction mode, which does not support `LISTEN` |
| `Channel` | `inertia_live` | The `NOTIFY` channel. Apps sharing a database need different channels |
| `LargeMessages` | `Table` | Over the 7900-byte `NOTIFY` limit: `Table` stores the message in a table and notifies a reference; `Reload` sends it without values, so clients reload |
| `TableName` | `inertia_live_messages` | The large-message table, optionally schema-qualified |
| `AutoCreateTable` | `true` | Create the table at startup. Set `false` and run `PostgresLiveBackplane.CreateTableSql()` in a migration when the app user cannot create tables |
| `DurableLargeMessages` | `false` | `false` creates an `UNLOGGED` table, which is faster and empty after a crash |
| `LargeMessageRetention` | 5 minutes | Rows older than this are deleted by a sweep every minute |
| `ReconnectDelayMax` | 30 seconds | The longest wait between attempts to reconnect the listening connection |

## Failures

- If PostgreSQL is down, `PublishAsync` throws. Retry from a job if it matters.
- The listening connection reconnects with backoff, and has a TCP keepalive so a half-open connection is noticed. Notifications
  sent while it was down are lost, so after it reconnects (once the messages received before are delivered) every instance closes
  its live connections, and the clients reconnect and reload their live props.
- A large message whose row was already swept is delivered without values.
- `LISTEN` has no permissions: any role that can connect to the database receives inline values. Treat the database as the
  trust boundary, or publish without values.

See [Live props at scale](https://inertia-dotnet.temtem.dev/docs/live-props-at-scale) for the permissions, metrics and troubleshooting.
