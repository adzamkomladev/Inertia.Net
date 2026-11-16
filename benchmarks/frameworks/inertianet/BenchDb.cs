using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace InertiaNetBench;

/// <summary>The app's EF Core context over the tables in shared/schema.sql.</summary>
public sealed class BenchDb(DbContextOptions<BenchDb> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    public DbSet<Submission> Submissions => Set<Submission>();
}

[Table("todos")]
public sealed class Todo
{
    [Column("id")] public int Id { get; set; }
    [Column("title")] public required string Title { get; set; }
    [Column("done")] public bool Done { get; set; }
    [Column("priority")] public int Priority { get; set; }
    [Column("assignee")] public required string Assignee { get; set; }
    [Column("due_at")] public DateOnly DueAt { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("submissions")]
public sealed class Submission
{
    [Column("id")] public int Id { get; set; }
    [Column("title")] public required string Title { get; set; }
    [Column("priority")] public int Priority { get; set; }
    [Column("assignee")] public required string Assignee { get; set; }
    [Column("due_at")] public DateOnly DueAt { get; set; }
}

/// <summary>The queries behind the routes.</summary>
public sealed class BenchData(BenchDb db)
{
    public Task<Page<TodoItem>> TodosAsync(PageRequest request, CancellationToken ct) =>
        db.Todos.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => new TodoItem(t.Id, t.Title, t.Done, t.Priority, t.Assignee, t.DueAt))
            .ToPageAsync(request, countTotal: false, ct: ct);

    public async Task<Stats> StatsAsync(CancellationToken ct)
    {
        var rows = await db.Todos.GroupBy(t => t.Assignee).OrderBy(g => g.Key)
            .Select(g => new AssigneeStats(g.Key, g.Count(), g.Count(t => t.Done), g.Sum(t => t.Priority)))
            .ToListAsync(ct);
        return new Stats(rows.Sum(r => r.Total), rows.Sum(r => r.Done), rows);
    }

    public async Task AddSubmissionAsync(string title, int priority, string assignee, DateOnly due, CancellationToken ct)
    {
        db.Submissions.Add(new Submission { Title = title, Priority = priority, Assignee = assignee, DueAt = due });
        await db.SaveChangesAsync(ct);
    }
}

internal static class BenchDataServices
{
    public static IServiceCollection AddBenchData(this IServiceCollection services, string connectionString) =>
        services.AddDbContextPool<BenchDb>(o => o.UseNpgsql(connectionString)).AddScoped<BenchData>();
}
