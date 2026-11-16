using Npgsql;

namespace InertiaNetBench;

/// <summary>The queries behind the routes on plain Npgsql, for the Native AOT build (BenchDb.cs is the EF Core version).</summary>
public sealed class BenchData(NpgsqlDataSource db)
{
    public Task<Page<TodoItem>> TodosAsync(PageRequest request, CancellationToken ct) =>
        Pagination.PaginateAsync(request, async (skip, take, token) =>
        {
            await using var command = db.CreateCommand(
                "SELECT id, title, done, priority, assignee, due_at FROM todos ORDER BY created_at DESC, id DESC LIMIT $1 OFFSET $2");
            command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = take });
            command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = skip });
            var todos = new List<TodoItem>(take);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                todos.Add(new TodoItem(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2), reader.GetInt32(3), reader.GetString(4),
                    reader.GetFieldValue<DateOnly>(5)));
            }

            return (IReadOnlyList<TodoItem>)todos;
        }, ct: ct);

    public async Task<Stats> StatsAsync(CancellationToken ct)
    {
        await using var command = db.CreateCommand(
            "SELECT assignee, count(*)::int AS total, (count(*) FILTER (WHERE done))::int AS done, sum(priority)::int AS priority_sum FROM todos GROUP BY assignee ORDER BY assignee");
        var rows = new List<AssigneeStats>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new AssigneeStats(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
        }

        return new Stats(rows.Sum(r => r.Total), rows.Sum(r => r.Done), rows);
    }

    public async Task AddSubmissionAsync(string title, int priority, string assignee, DateOnly due, CancellationToken ct)
    {
        await using var command = db.CreateCommand("INSERT INTO submissions (title, priority, assignee, due_at) VALUES ($1, $2, $3, $4)");
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = title });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = priority });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = assignee });
        command.Parameters.Add(new NpgsqlParameter<DateOnly> { TypedValue = due });
        await command.ExecuteNonQueryAsync(ct);
    }
}

internal static class BenchDataServices
{
    public static IServiceCollection AddBenchData(this IServiceCollection services, string connectionString) =>
        services.AddSingleton(new NpgsqlSlimDataSourceBuilder(connectionString).Build()).AddScoped<BenchData>();
}
