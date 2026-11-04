using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_splitting_annotations_preserve_the_underlying_order_guard(bool split)
    {
        await using var fixture = await Fixture.CreateAsync();
        IQueryable<Row> Annotate(IQueryable<Row> source) => split ? source.AsSplitQuery() : source.AsSingleQuery();
        var ordered = Annotate(fixture.Db.Rows.OrderBy(row => row.Id));
        var page = await ordered.ToPageAsync(new PageRequest(2, 3), ct: Ct);
        Assert.Equal([4, 5, 6], page.Data.Select(row => row.Id));
        Assert.Throws<InvalidOperationException>(() => { _ = ordered.ToCursorPageAsync(row => row.Id, new CursorRequest(), ct: Ct); });
        var unordered = Annotate(fixture.Db.Rows.Where(row => row.Id > 0));
        Assert.Throws<InvalidOperationException>(() => { _ = unordered.ToPageAsync(new PageRequest(), ct: Ct); });
        var cursor = await unordered.ToCursorPageAsync(row => row.Id, new CursorRequest(PerPage: 3), ct: Ct);
        Assert.Equal([1, 2, 3], cursor.Data.Select(row => row.Id));
        var distinct = Annotate(fixture.Db.Rows.OrderBy(row => row.Id).Distinct());
        Assert.Throws<InvalidOperationException>(() => { _ = distinct.ToPageAsync(new PageRequest(), ct: Ct); });
    }

    [Fact]
    public async Task A_foreign_type_with_the_relational_extension_name_is_not_an_order_annotation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Untrusted.QueryAnnotations"), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("Annotations").DefineType("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = type.DefineMethod("AsSplitQuery", MethodAttributes.Public | MethodAttributes.Static);
        var entity = method.DefineGenericParameters("T")[0];
        var queryType = typeof(IQueryable<>).MakeGenericType(entity);
        method.SetReturnType(queryType);
        method.SetParameters(queryType);
        method.GetILGenerator().Emit(OpCodes.Ldarg_0);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var annotation = type.CreateType()!.GetMethod("AsSplitQuery")!.MakeGenericMethod(typeof(Row));
        var source = fixture.Db.Rows.OrderBy(row => row.Id);
        var query = source.Provider.CreateQuery<Row>(Expression.Call(annotation, source.Expression));
        Assert.Throws<InvalidOperationException>(() => { _ = query.ToPageAsync(new PageRequest(), ct: Ct); });
        Assert.Empty(fixture.Sql.Commands);
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

    [Theory]
    [InlineData("private-cursor-value%%%")]
    [InlineData("WyJhIiwiZGVjb2RlZC1zZWNyZXQiLDFd")]
    [InlineData("WyJhIiwxXQ")]
    public async Task Composite_invalid_cursors_log_once_without_payloads_or_exceptions(string cursor)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var logs = new CaptureLogs();
        var request = new CursorRequest(cursor, 3) { Logger = logs.CreateLogger("Standalone") };
        var page = await fixture.Db.Rows.ToCursorPageAsync(row => row.CreatedAt, row => row.Id, request, ct: Ct);
        Assert.Equal([1, 2, 3], page.Data.Select(row => row.Id));
        Assert.Null(page.CurrentCursor);
        var entry = Assert.Single(logs.Entries);
        AssertInvalidCursorLog(entry, "Standalone");
        Assert.DoesNotContain(cursor, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(cursor, string.Join(',', entry.State.Select(item => item.Value)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Composite_missing_empty_and_valid_cursors_do_not_log()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var logs = new CaptureLogs();
        var first = await fixture.Db.Rows.ToCursorPageAsync(row => row.CreatedAt, row => row.Id, new CursorRequest(PerPage: 3), ct: Ct);
        Assert.NotNull(first.NextCursor);
        foreach (var cursor in new[] { null, "", first.NextCursor })
        {
            var request = new CursorRequest(cursor, 3) { Logger = logs.CreateLogger("Standalone") };
            await fixture.Db.Rows.ToCursorPageAsync(row => row.CreatedAt, row => row.Id, request, ct: Ct);
        }

        Assert.Empty(logs.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HTTP_cursor_factory_supplies_EF_helpers_with_options_names_and_diagnostics(bool composite)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var logs = new CaptureLogs();
        using var services = new ServiceCollection()
            .Configure<InertiaOptions>(options =>
            {
                options.Pagination.DefaultPerPage = 7;
                options.Pagination.MaxPerPage = 12;
                options.Pagination.CursorName = "c";
                options.Pagination.PerPageName = "size";
            })
            .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(logs))
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.QueryString = new QueryString("?c=private-invalid%%%&size=999&alternate=other-invalid&limit=30");
        Task<CursorPage<Row>> Load(CursorRequest request) => composite
            ? fixture.Db.Rows.ToCursorPageAsync(row => row.CreatedAt, row => row.Id, request, ct: Ct)
            : fixture.Db.Rows.ToCursorPageAsync(row => row.Id, request, ct: Ct);
        var configured = await Load(CursorRequest.From(context));
        Assert.Equal("c", configured.CursorName);
        Assert.Equal(12, configured.PerPage);
        Assert.Equal(Enumerable.Range(1, 12), configured.Data.Select(row => row.Id));
        var overridden = await Load(CursorRequest.From(context, defaultPerPage: 3, maxPerPage: 4, cursorName: "alternate", perPageName: "limit"));
        Assert.Equal("alternate", overridden.CursorName);
        Assert.Equal(4, overridden.PerPage);
        Assert.Equal([1, 2, 3, 4], overridden.Data.Select(row => row.Id));
        Assert.Equal(2, logs.Entries.Count);
        Assert.All(logs.Entries, entry => AssertInvalidCursorLog(entry, "Inertia.Net.Pagination"));
        context.Request.QueryString = QueryString.Empty;
        var defaults = await Load(CursorRequest.From(context));
        Assert.Equal(7, defaults.PerPage);
        var explicitDefault = await Load(CursorRequest.From(context, defaultPerPage: 3));
        Assert.Equal(3, explicitDefault.PerPage);
        Assert.Equal(2, logs.Entries.Count);
    }

    private static void AssertInvalidCursorLog(CaptureLogs.Entry entry, string category)
    {
        Assert.Equal(category, entry.Category);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal("Ignoring an invalid pagination cursor; loading the first page.", entry.Message);
        Assert.Null(entry.Exception);
        Assert.Equal(new KeyValuePair<string, object?>("{OriginalFormat}", entry.Message), Assert.Single(entry.State));
    }

    private sealed class CaptureLogs : ILoggerProvider
    {
        internal sealed record Entry(string Category, LogLevel Level, string Message, Exception? Exception, KeyValuePair<string, object?>[] State);
        public List<Entry> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }

        private sealed class Logger(CaptureLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Add(new Entry(category, logLevel, formatter(state, exception), exception,
                    state is IEnumerable<KeyValuePair<string, object?>> values ? values.ToArray() : []));
        }
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
            Database? db = null;
            try
            {
                await connection.OpenAsync(Ct);
                var sql = new CaptureSql();
                var options = new DbContextOptionsBuilder<Database>().EnableServiceProviderCaching(false).UseSqlite(connection).AddInterceptors(sql).Options;
                db = new Database(options);
                await db.Database.EnsureCreatedAsync(Ct);
                db.Rows.AddRange(Enumerable.Range(1, 25).Select(id => new Row
                {
                    Id = id,
                    NullableId = id,
                    CreatedAt = new DateTime(2026, 1, 1).AddDays(Math.Floor((id - 1) / 3d)),
                    Name = $"row{id:D2}",
                    Token = Guid.Parse($"00000000-0000-0000-0000-{id:D12}"),
                    Flag = id % 2 == 0,
                }));
                await db.SaveChangesAsync(Ct);
                sql.Commands.Clear();
                return new Fixture(connection, db, sql);
            }
            catch
            {
                try
                {
                    if (db is not null) await db.DisposeAsync();
                }
                finally
                {
                    await connection.DisposeAsync();
                }

                throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                await Db.DisposeAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
