using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;

namespace Inertia.Net.EntityFrameworkCore.Tests;

public sealed class PaginationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1, 1, 10, true)]
    [InlineData(2, 11, 10, true)]
    [InlineData(3, 21, 5, false)]
    [InlineData(4, 0, 0, false)]
    [InlineData(int.MaxValue, 0, 0, false)]
    public async Task Offset_pages_count_and_trim_overfetch(int number, int first, int count, bool hasMore)
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = await fixture.Db.Rows.OrderBy(row => row.Id).ToPageAsync(new PageRequest(number, 10), ct: Ct);
        Assert.Equal(count, page.Data.Count);
        if (count > 0) Assert.Equal(first, page.Data[0].Id);
        Assert.Equal(25L, page.Total);
        Assert.Equal(3L, page.LastPage);
        Assert.Equal(hasMore, page.HasMore);
        Assert.Equal(number == int.MaxValue ? 1 : 2, fixture.Sql.Commands.Count);
    }

    [Fact]
    public async Task Offset_without_count_executes_only_the_rows_query()
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = await fixture.Db.Rows.OrderBy(row => row.Id).ToPageAsync(new PageRequest(2, 10), countTotal: false, ct: Ct);
        Assert.Null(page.Total);
        Assert.Null(page.LastPage);
        Assert.Equal(Enumerable.Range(11, 10), page.Data.Select(row => row.Id));
        Assert.Single(fixture.Sql.Commands);
    }

    [Fact]
    public async Task Offset_order_guard_uses_expression_order_rather_than_queryable_runtime_type()
    {
        await using var fixture = await Fixture.CreateAsync();
        var unordered = fixture.Db.Rows.Where(row => row.Id > 0);
        Assert.IsAssignableFrom<IOrderedQueryable<Row>>(unordered);
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = unordered.ToPageAsync(new PageRequest(), ct: Ct);
        });
        Assert.Equal("Order the query before paging it", exception.Message);
        Assert.Empty(fixture.Sql.Commands);
        var valid = fixture.Db.Rows.OrderBy(row => row.Id).Where(row => row.Id > 0).Select(row => row).AsNoTracking();
        var page = await valid.ToPageAsync(new PageRequest(1, 3), ct: Ct);
        Assert.Equal([1, 2, 3], page.Data.Select(row => row.Id));
    }

    [Theory]
    [InlineData("Distinct")]
    [InlineData("GroupBy")]
    [InlineData("Union")]
    [InlineData("Concat")]
    [InlineData("Intersect")]
    [InlineData("Except")]
    public async Task Operators_that_discard_order_require_a_new_order_by(string operation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var ordered = fixture.Db.Rows.OrderBy(row => row.Id);
        var query = operation switch
        {
            "Distinct" => ordered.Distinct(),
            "GroupBy" => ordered.GroupBy(row => row.Id).Select(group => group.First()),
            "Union" => ordered.Union(fixture.Db.Rows),
            "Concat" => ordered.Concat(fixture.Db.Rows),
            "Intersect" => ordered.Intersect(fixture.Db.Rows),
            _ => ordered.Except(fixture.Db.Rows),
        };
        Assert.Throws<InvalidOperationException>(() => { _ = query.ToPageAsync(new PageRequest(), ct: Ct); });
        Assert.Empty(fixture.Sql.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Composite_cursors_cross_duplicate_keys_forward_and_back_without_losing_rows(bool descending)
    {
        await using var fixture = await Fixture.CreateAsync();
        Task<CursorPage<Row>> Load(string? cursor) => fixture.Db.Rows.ToCursorPageAsync(row => row.CreatedAt, row => row.Id,
            new CursorRequest(cursor, 4), descending: descending, ct: Ct);
        var first = await Load(null);
        var second = await Load(first.NextCursor);
        var third = await Load(second.NextCursor);
        var back = await Load(third.PreviousCursor);
        Assert.Equal(second.Data.Select(row => row.Id), back.Data.Select(row => row.Id));
        Assert.NotNull(back.PreviousCursor);
        var start = await Load(second.PreviousCursor);
        Assert.Equal(first.Data.Select(row => row.Id), start.Data.Select(row => row.Id));
        Assert.Null(start.PreviousCursor);
        Assert.NotNull(start.NextCursor);
        var ids = new List<int>();
        var page = first;
        do
        {
            ids.AddRange(page.Data.Select(row => row.Id));
            if (page.NextCursor is null) break;
            page = await Load(page.NextCursor);
        } while (true);
        Assert.Equal(descending ? Enumerable.Range(1, 25).Reverse() : Enumerable.Range(1, 25), ids);
        Assert.Equal(25, ids.Distinct().Count());
        Assert.DoesNotContain(fixture.Sql.Commands, sql => sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Keyset_SQL_has_lexicographic_predicate_and_no_offset()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Db.Rows.ToCursorPageAsync(row => row.CreatedAt, row => row.Id, new CursorRequest(PerPage: 4), ct: Ct);
        await fixture.Db.Rows.ToCursorPageAsync(row => row.CreatedAt, row => row.Id, new CursorRequest(first.NextCursor, 4), ct: Ct);
        var expression = Assert.IsAssignableFrom<MethodCallExpression>(fixture.Sql.Expression);
        // Restore parameters observed by the compilation interceptor before asking EF for the query SQL again.
        var restored = new RestoreParameters(fixture.Sql.Parameters).Visit(expression)!;
        var query = ((IQueryable<Row>)fixture.Db.Rows).Provider.CreateQuery<Row>(restored);
        var sql = query.ToQueryString();
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" OR ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" AND ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CreatedAt", sql, StringComparison.Ordinal);
        Assert.Contains("Id", sql, StringComparison.Ordinal);
        TestContext.Current.TestOutputHelper!.WriteLine(sql);
    }

    [Fact]
    public async Task Cursor_boundaries_reuse_parameterized_SQL()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Db.Rows.ToCursorPageAsync(row => row.CreatedAt, row => row.Id, new CursorRequest(PerPage: 4), ct: Ct);
        var second = await fixture.Db.Rows.ToCursorPageAsync(row => row.CreatedAt, row => row.Id, new CursorRequest(first.NextCursor, 4), ct: Ct);
        var secondSql = fixture.Sql.Commands[^1];
        await fixture.Db.Rows.ToCursorPageAsync(row => row.CreatedAt, row => row.Id, new CursorRequest(second.NextCursor, 4), ct: Ct);
        Assert.Equal(secondSql, fixture.Sql.Commands[^1]);
        Assert.DoesNotContain("'2026-", secondSql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("guid")]
    public async Task Single_keys_execute_forward_and_backward_queries(string kind)
    {
        await using var fixture = await Fixture.CreateAsync();
        Task<CursorPage<Row>> Load(string? cursor) => kind switch
        {
            "string" => fixture.Db.Rows.ToCursorPageAsync(row => row.Name, new CursorRequest(cursor, 7), ct: Ct),
            "guid" => fixture.Db.Rows.ToCursorPageAsync(row => row.Token, new CursorRequest(cursor, 7), ct: Ct),
            _ => fixture.Db.Rows.ToCursorPageAsync(row => row.Id, new CursorRequest(cursor, 7), ct: Ct),
        };
        var first = await Load(null);
        var second = await Load(first.NextCursor);
        Assert.Equal(Enumerable.Range(8, 7), second.Data.Select(row => row.Id));
        var previous = await Load(second.PreviousCursor);
        Assert.Equal(first.Data.Select(row => row.Id), previous.Data.Select(row => row.Id));
        Assert.Null(previous.PreviousCursor);
    }

    [Fact]
    public async Task String_keys_use_the_database_collation_for_comparison_and_order()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rows = await fixture.Db.Rows.Where(row => row.Id <= 3).OrderBy(row => row.Id).ToListAsync(Ct);
        rows[0].Name = "a";
        rows[1].Name = "B";
        rows[2].Name = "c";
        await fixture.Db.SaveChangesAsync(Ct);
        var query = fixture.Db.Rows.Where(row => row.Id <= 3);
        var first = await query.ToCursorPageAsync(row => row.Name, new CursorRequest(PerPage: 1), ct: Ct);
        var second = await query.ToCursorPageAsync(row => row.Name, new CursorRequest(first.NextCursor, 1), ct: Ct);
        Assert.Equal(1, Assert.Single(first.Data).Id);
        Assert.Equal(2, Assert.Single(second.Data).Id);
    }

    [Fact]
    public async Task Boolean_keys_with_a_tie_breaker_translate_to_SQL()
    {
        await using var fixture = await Fixture.CreateAsync();
        var query = fixture.Db.Rows.Where(row => row.Id <= 6);
        var first = await query.ToCursorPageAsync(row => row.Flag, row => row.Id, new CursorRequest(PerPage: 3), ct: Ct);
        var second = await query.ToCursorPageAsync(row => row.Flag, row => row.Id, new CursorRequest(first.NextCursor, 3), ct: Ct);
        Assert.Equal([1, 3, 5], first.Data.Select(row => row.Id));
        Assert.Equal([2, 4, 6], second.Data.Select(row => row.Id));
    }

    [Fact]
    public async Task Malformed_empty_and_past_end_cursor_requests_have_safe_results()
    {
        await using var fixture = await Fixture.CreateAsync();
        var malformed = await fixture.Db.Rows.ToCursorPageAsync(row => row.Id, new CursorRequest("%%%", 3), ct: Ct);
        Assert.Equal([1, 2, 3], malformed.Data.Select(row => row.Id));
        Assert.Null(malformed.PreviousCursor);
        var empty = await fixture.Db.Rows.Where(row => row.Id < 0).ToCursorPageAsync(row => row.Id, new CursorRequest(), ct: Ct);
        Assert.Empty(empty.Data);
        Assert.Null(empty.NextCursor);
        Assert.Null(empty.PreviousCursor);
        var token = await Pagination.PaginateAsync<int, int>(new CursorRequest(PerPage: 1), id => id,
            (_, _, _) => Task.FromResult<IReadOnlyList<int>>([99, 100]), ct: Ct);
        var past = await fixture.Db.Rows.ToCursorPageAsync(row => row.Id, new CursorRequest(token.NextCursor), ct: Ct);
        Assert.Empty(past.Data);
        Assert.False(past.HasMore);
    }

    [Fact]
    public async Task Cursor_queries_reject_preexisting_order_at_call_time()
    {
        await using var fixture = await Fixture.CreateAsync();
        var query = fixture.Db.Rows.OrderBy(row => row.Name).Where(row => row.Id > 0);
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = query.ToCursorPageAsync(row => row.Id, new CursorRequest(), ct: Ct);
        });
        Assert.Empty(fixture.Sql.Commands);
    }

    [Fact]
    public async Task Unsupported_and_nullable_keys_are_rejected_at_call_time()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Throws<NotSupportedException>(() =>
        {
            _ = fixture.Db.Rows.ToCursorPageAsync(row => DateOnly.FromDateTime(row.CreatedAt), new CursorRequest(), ct: Ct);
        });
        Expression<Func<Row, int?>> selector = row => row.NullableId;
        var method = typeof(InertiaQueryableExtensions).GetMethods().Single(candidate => candidate.Name == "ToCursorPageAsync"
            && candidate.GetGenericArguments().Length == 2).MakeGenericMethod(typeof(Row), typeof(int?));
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [fixture.Db.Rows, selector, new CursorRequest(), false, null, Ct]));
        var inner = Assert.IsType<NotSupportedException>(exception.InnerException);
        Assert.Equal("Keyset paging needs non-null keys: order by a non-null column or coalesce the expression, and add a unique tie-breaker.", inner.Message);
        Assert.Empty(fixture.Sql.Commands);
    }

    [Fact]
    public async Task Parsed_names_and_explicit_overrides_are_preserved()
    {
        await using var fixture = await Fixture.CreateAsync();
        var query = new QueryCollection(QueryHelpers.ParseQuery("?p=2&n=3"));
        var request = PageRequest.From(query, pageName: "p", perPageName: "n");
        var page = await fixture.Db.Rows.OrderBy(row => row.Id).ToPageAsync(request, ct: Ct);
        Assert.Equal("p", page.GetScrollMetadata().PageName);
        Assert.Equal([4, 5, 6], page.Data.Select(row => row.Id));
        var overridden = await fixture.Db.Rows.OrderBy(row => row.Id).ToPageAsync(request, pageName: "pageOverride", ct: Ct);
        Assert.Equal("pageOverride", overridden.PageName);
        var cursorRequest = CursorRequest.From(query, cursorName: "c", perPageName: "n");
        var cursor = await fixture.Db.Rows.ToCursorPageAsync(row => row.Id, cursorRequest, ct: Ct);
        Assert.Equal("c", cursor.CursorName);
        var explicitCursor = await fixture.Db.Rows.ToCursorPageAsync(row => row.Id, cursorRequest, cursorName: "cursorOverride", ct: Ct);
        Assert.Equal("cursorOverride", explicitCursor.CursorName);
    }

    [Fact]
    public async Task Cancellation_reaches_EF_without_executing_a_query()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Db.Rows.OrderBy(row => row.Id).ToPageAsync(new PageRequest(), ct: source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Db.Rows.ToCursorPageAsync(row => row.Id, new CursorRequest(), ct: source.Token));
        Assert.Empty(fixture.Sql.Commands);
    }

    public sealed class Row
    {
        public int Id { get; set; }
        public int? NullableId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Name { get; set; } = "";
        public Guid Token { get; set; }
        public bool Flag { get; set; }
    }

    private sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Row>().Property(row => row.Name).UseCollation("NOCASE");
    }

    private sealed class CaptureSql : DbCommandInterceptor, IQueryExpressionInterceptor
    {
        public List<string> Commands { get; } = [];
        public Expression? Expression { get; private set; }
        public Dictionary<string, object?> Parameters { get; } = [];
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Expression = queryExpression;
            return queryExpression;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            Parameters.Clear();
            foreach (DbParameter parameter in command.Parameters)
                Parameters[parameter.ParameterName.TrimStart('@')] = parameter.Value;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RestoreParameters(IReadOnlyDictionary<string, object?> values) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node) => node is QueryParameterExpression parameter
            ? Expression.Constant(values[parameter.Name], parameter.Type)
            : base.VisitExtension(node);
    }

    private sealed class Fixture(SqliteConnection connection, Database db, CaptureSql sql) : IAsyncDisposable
    {
        public Database Db { get; } = db;
        public CaptureSql Sql { get; } = sql;
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(Ct);
            var sql = new CaptureSql();
            var options = new DbContextOptionsBuilder<Database>().EnableServiceProviderCaching(false).UseSqlite(connection).AddInterceptors(sql).Options;
            var db = new Database(options);
            await db.Database.EnsureCreatedAsync(Ct);
            db.Rows.AddRange(Enumerable.Range(1, 25).Select(id => new Row
            {
                Id = id,
                NullableId = id,
                CreatedAt = new DateTime(2026, 1, 1).AddDays((id - 1) / 3),
                Name = $"row{id:D2}",
                Token = Guid.Parse($"00000000-0000-0000-0000-{id:D12}"),
                Flag = id % 2 == 0,
            }));
            await db.SaveChangesAsync(Ct);
            sql.Commands.Clear();
            return new Fixture(connection, db, sql);
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
