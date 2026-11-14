// The data side of the todo app (PROBE_MODE=todo): a SQLite file with 10 users' todos, read per request for the signed-in user.
// The JIT build uses EF Core, as a typical small app would; the Native AOT build uses Microsoft.Data.Sqlite, since EF Core's
// runtime query compilation is not Native AOT compatible.
#if HOSTING_PROBE_AOT
using Microsoft.Data.Sqlite;
#else
using Microsoft.EntityFrameworkCore;
#endif

namespace Inertia.Net.HostingProbe;

public sealed class Todo
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Title { get; set; } = "";

    public bool Done { get; set; }

    public DateTime CreatedAt { get; set; }
}

internal static class TodoStore
{
    private static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hosting-probe-{Environment.ProcessId}.db");

    public static string ConnectionString => $"Data Source={Path}";

    public static IEnumerable<Todo> Seed()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var user = 1; user <= 10; user++)
        {
            for (var i = 1; i <= 25; i++)
            {
                yield return new Todo { UserId = user, Title = $"Todo {i} for user {user}", Done = i % 3 == 0, CreatedAt = created.AddHours(i) };
            }
        }
    }

#if HOSTING_PROBE_AOT
    public static void Add(IServiceCollection services)
    {
        File.Delete(Path);
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE Todos (Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId INTEGER NOT NULL, Title TEXT NOT NULL, Done INTEGER NOT NULL, CreatedAt TEXT NOT NULL); CREATE INDEX IX_Todos_UserId ON Todos (UserId);";
            create.ExecuteNonQuery();
        }

        foreach (var todo in Seed())
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO Todos (UserId, Title, Done, CreatedAt) VALUES ($user, $title, $done, $created)";
            insert.Parameters.AddWithValue("$user", todo.UserId);
            insert.Parameters.AddWithValue("$title", todo.Title);
            insert.Parameters.AddWithValue("$done", todo.Done);
            insert.Parameters.AddWithValue("$created", todo.CreatedAt);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public static async Task<Todo[]> ForUserAsync(IServiceProvider services, int userId)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT Id, UserId, Title, Done, CreatedAt FROM Todos WHERE UserId = $user ORDER BY Id";
        query.Parameters.AddWithValue("$user", userId);
        var todos = new List<Todo>(25);
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            todos.Add(new Todo { Id = reader.GetInt32(0), UserId = reader.GetInt32(1), Title = reader.GetString(2), Done = reader.GetBoolean(3), CreatedAt = reader.GetDateTime(4) });
        }

        return [.. todos];
    }
#else
    public static void Add(IServiceCollection services)
    {
        File.Delete(Path);
        services.AddDbContext<TodoDb>(o => o.UseSqlite(ConnectionString));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDb>();
        db.Database.EnsureCreated();
        db.Todos.AddRange(Seed());
        db.SaveChanges();
    }

    public static Task<Todo[]> ForUserAsync(IServiceProvider services, int userId) =>
        services.GetRequiredService<TodoDb>().Todos.AsNoTracking().Where(t => t.UserId == userId).OrderBy(t => t.Id).ToArrayAsync();
#endif
}

#if !HOSTING_PROBE_AOT
internal sealed class TodoDb(DbContextOptions<TodoDb> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder model) => model.Entity<Todo>().HasIndex(t => t.UserId);
}
#endif
