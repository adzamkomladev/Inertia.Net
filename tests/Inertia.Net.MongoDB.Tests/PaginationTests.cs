using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Inertia.Net.MongoDB.Tests;

public sealed class Row
{
    public ObjectId Id { get; set; }

    public int Number { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Name { get; set; } = "";

    public bool Active { get; set; } = true;

    public int? NullableNumber { get; set; }
}

/// <summary>The keyset filter and sort, rendered without a server.</summary>
public sealed class KeysetRenderTests
{
    private static string Render(FilterDefinition<Row>? filter) =>
        filter is null ? "null" : filter.Render(new RenderArgs<Row>(BsonSerializer.LookupSerializer<Row>(), BsonSerializer.SerializerRegistry)).ToString();

    private static string Render(SortDefinition<Row> sort) =>
        sort.Render(new RenderArgs<Row>(BsonSerializer.LookupSerializer<Row>(), BsonSerializer.SerializerRegistry)).ToString();

    [Theory]
    [InlineData(false, CursorDirection.After, "{ \"Number\" : { \"$gt\" : 5 } }", "{ \"Number\" : 1 }")]
    [InlineData(true, CursorDirection.After, "{ \"Number\" : { \"$lt\" : 5 } }", "{ \"Number\" : -1 }")]
    [InlineData(false, CursorDirection.Before, "{ \"Number\" : { \"$lt\" : 5 } }", "{ \"Number\" : -1 }")]
    [InlineData(true, CursorDirection.Before, "{ \"Number\" : { \"$gt\" : 5 } }", "{ \"Number\" : 1 }")]
    public void One_key(bool descending, CursorDirection direction, string filter, string sort)
    {
        var (f, s) = Keyset<Row, int, int>.Build(r => r.Number, null, new(direction, 5, 0), descending);
        Assert.Equal(filter, Render(f));
        Assert.Equal(sort, Render(s));
        var (first, firstSort) = Keyset<Row, int, int>.Build(r => r.Number, null, null, descending);
        Assert.Equal("null", Render(first));
        Assert.Equal(descending ? "{ \"Number\" : -1 }" : "{ \"Number\" : 1 }", Render(firstSort));
    }

    [Fact]
    public void Key_and_tie_breaker()
    {
        var id = ObjectId.Parse("650000000000000000000001");
        var (f, s) = Keyset<Row, string, ObjectId>.Build(r => r.Name, r => r.Id, new(CursorDirection.After, "b", id), descending: true);
        Assert.Equal("{ \"$or\" : [{ \"Name\" : { \"$lt\" : \"b\" } }, { \"Name\" : \"b\", \"_id\" : { \"$lt\" : { \"$oid\" : \"650000000000000000000001\" } } }] }", Render(f));
        Assert.Equal("{ \"Name\" : -1, \"_id\" : -1 }", Render(s));
    }
}

/// <summary>Against a real server: set <c>MONGODB_URI</c> (e.g. <c>mongodb://localhost:27017</c>); without it the tests are skipped.</summary>
public sealed class PaginationTests : IAsyncLifetime
{
    private static readonly string? Uri = Environment.GetEnvironmentVariable("MONGODB_URI");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private MongoClient? _client;
    private IMongoDatabase? _database;
    private IMongoCollection<Row> _rows = null!;
    private List<Row> _seeded = [];

    public async ValueTask InitializeAsync()
    {
        if (Uri is null) return;
        _client = new MongoClient(Uri);
        _database = _client.GetDatabase("inertia_tests_" + Guid.NewGuid().ToString("N"));
        _rows = _database.GetCollection<Row>("rows");
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // 25 rows; CreatedAt repeats in fives, so a composite cursor crosses duplicate keys.
        _seeded = [.. Enumerable.Range(1, 25).Select(i => new Row
        {
            Id = ObjectId.Parse($"6500000000000000000000{i:x2}"),
            Number = i,
            CreatedAt = start + TimeSpan.FromHours((i - 1) / 5),
            Name = $"n{i:00}",
            Active = i % 2 == 1,
        })];
        await _rows.InsertManyAsync(_seeded);
    }

    public async ValueTask DisposeAsync()
    {
        if (_database is not null) await _database.Client.DropDatabaseAsync(_database.DatabaseNamespace.DatabaseName);
        _client?.Dispose();
    }

    private IFindFluent<Row, Row> All() => Find(_ => true);

    private IFindFluent<Row, Row> Find(Expression<Func<Row, bool>> filter)
    {
        Assert.SkipWhen(Uri is null, "MONGODB_URI is not set");
        return _rows.Find(filter);
    }

    [Theory]
    [InlineData(1, 1, 10, true)]
    [InlineData(2, 11, 10, true)]
    [InlineData(3, 21, 5, false)]
    [InlineData(4, 0, 0, false)]
    [InlineData(int.MaxValue, 0, 0, false)]
    public async Task Offset_pages_count_and_trim_overfetch(int number, int first, int count, bool hasMore)
    {
        var page = await All().SortBy(r => r.Number).ToPageAsync(new PageRequest(number, 10), ct: Ct);
        Assert.Equal(count, page.Data.Count);
        if (count > 0) Assert.Equal(first, page.Data[0].Number);
        Assert.Equal(25L, page.Total);
        Assert.Equal(3L, page.LastPage);
        Assert.Equal(hasMore, page.HasMore);
    }

    [Fact]
    public async Task Offset_counts_the_filter_and_can_skip_the_count()
    {
        var active = await Find(r => r.Active).SortBy(r => r.Number).ToPageAsync(new PageRequest(2, 5), ct: Ct);
        Assert.Equal(13L, active.Total);
        Assert.Equal([11, 13, 15, 17, 19], active.Data.Select(r => r.Number));
        var uncounted = await All().SortBy(r => r.Number).ToPageAsync(new PageRequest(2, 10), countTotal: false, ct: Ct);
        Assert.Null(uncounted.Total);
        Assert.Equal(Enumerable.Range(11, 10), uncounted.Data.Select(r => r.Number));
    }

    [Fact]
    public async Task Offset_needs_a_sort_and_cursors_refuse_one()
    {
        Assert.Throws<InvalidOperationException>(() => { _ = All().ToPageAsync(new PageRequest(), ct: Ct); });
        Assert.Throws<InvalidOperationException>(() => { _ = All().SortBy(r => r.Name).ToCursorPageAsync(r => r.Number, new CursorRequest(), ct: Ct); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Composite_cursors_cross_duplicate_keys_forward_and_back_without_losing_rows(bool descending)
    {
        Task<CursorPage<Row>> Load(string? cursor) => All().ToCursorPageAsync(r => r.CreatedAt, r => r.Id, new CursorRequest(cursor, 4), descending, ct: Ct);
        var first = await Load(null);
        var second = await Load(first.NextCursor);
        var third = await Load(second.NextCursor);
        var back = await Load(third.PreviousCursor);
        Assert.Equal(second.Data.Select(r => r.Number), back.Data.Select(r => r.Number));
        var start = await Load(second.PreviousCursor);
        Assert.Equal(first.Data.Select(r => r.Number), start.Data.Select(r => r.Number));
        Assert.Null(start.PreviousCursor);

        var numbers = new List<int>();
        for (var page = first; ; page = await Load(page.NextCursor))
        {
            numbers.AddRange(page.Data.Select(r => r.Number));
            if (page.NextCursor is null) break;
        }

        Assert.Equal(descending ? Enumerable.Range(1, 25).Reverse() : Enumerable.Range(1, 25), numbers);
    }

    public static TheoryData<string> Keys => ["int", "objectId", "string", "date"];

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Single_keys_walk_forward_and_back(string kind)
    {
        Task<CursorPage<Row>> Load(string? cursor) => kind switch
        {
            "int" => All().ToCursorPageAsync(r => r.Number, new CursorRequest(cursor, 10), ct: Ct),
            "objectId" => All().ToCursorPageAsync(r => r.Id, new CursorRequest(cursor, 10), ct: Ct),
            "string" => All().ToCursorPageAsync(r => r.Name, new CursorRequest(cursor, 10), ct: Ct),
            _ => All().ToCursorPageAsync(r => r.CreatedAt, r => r.Number, new CursorRequest(cursor, 10), ct: Ct),
        };
        var first = await Load(null);
        var second = await Load(first.NextCursor);
        var third = await Load(second.NextCursor);
        Assert.Equal(Enumerable.Range(1, 10), first.Data.Select(r => r.Number));
        Assert.Equal(Enumerable.Range(21, 5), third.Data.Select(r => r.Number));
        Assert.Null(third.NextCursor);
        Assert.Equal(Enumerable.Range(11, 10), (await Load(third.PreviousCursor)).Data.Select(r => r.Number));
    }

    [Fact]
    public async Task The_keyset_is_added_to_the_users_filter()
    {
        var first = await Find(r => r.Active).ToCursorPageAsync(r => r.Number, new CursorRequest(null, 3), ct: Ct);
        var second = await Find(r => r.Active).ToCursorPageAsync(r => r.Number, new CursorRequest(first.NextCursor, 3), ct: Ct);
        Assert.Equal([1, 3, 5], first.Data.Select(r => r.Number));
        Assert.Equal([7, 9, 11], second.Data.Select(r => r.Number));
    }

    [Theory]
    [InlineData("%%%")]
    [InlineData("WyJhIiwibm90LWFuLWlkIl0")] // ["a","not-an-id"]: a cursor whose ObjectId does not parse
    public async Task Tampered_cursors_start_at_the_first_page(string cursor)
    {
        var page = await All().ToCursorPageAsync(r => r.Id, new CursorRequest(cursor, 3), ct: Ct);
        Assert.Equal([1, 2, 3], page.Data.Select(r => r.Number));
        Assert.Null(page.PreviousCursor);
    }

    [Fact]
    public async Task Empty_and_past_the_end_pages_are_empty()
    {
        var empty = await Find(r => r.Number < 0).ToCursorPageAsync(r => r.Number, new CursorRequest(), ct: Ct);
        Assert.Empty(empty.Data);
        Assert.Null(empty.NextCursor);
        var token = await Pagination.PaginateAsync<int, int>(new CursorRequest(PerPage: 1), id => id, (_, _, _) => Task.FromResult<IReadOnlyList<int>>([99, 100]), ct: Ct);
        var past = await All().ToCursorPageAsync(r => r.Number, new CursorRequest(token.NextCursor), ct: Ct);
        Assert.Empty(past.Data);
        Assert.False(past.HasMore);
    }

    [Fact]
    public async Task Unsupported_and_nullable_keys_are_rejected_at_call_time()
    {
#pragma warning disable CS8714 // the nullable key the notnull constraint warns about is the point of the test
        Assert.Throws<NotSupportedException>(() => { _ = All().ToCursorPageAsync(r => r.NullableNumber, new CursorRequest(), ct: Ct); });
#pragma warning restore CS8714
        Assert.Throws<NotSupportedException>(() => { _ = All().ToCursorPageAsync(r => DateOnly.FromDateTime(r.CreatedAt), new CursorRequest(), ct: Ct); });
    }

    [Fact]
    public async Task Non_ascii_string_keys_round_trip_through_the_cursor()
    {
        Assert.SkipWhen(Uri is null, "MONGODB_URI is not set");
        var named = _database!.GetCollection<Row>("named");
        await named.InsertManyAsync(new[] { "ä", "z", "日本", "émile", "ß" }.Select((name, i) => new Row { Id = ObjectId.GenerateNewId(), Number = i, Name = name }), cancellationToken: Ct);
        var first = await named.Find(_ => true).ToCursorPageAsync(r => r.Name, new CursorRequest(null, 2), ct: Ct);
        var second = await named.Find(_ => true).ToCursorPageAsync(r => r.Name, new CursorRequest(first.NextCursor, 2), ct: Ct);
        var third = await named.Find(_ => true).ToCursorPageAsync(r => r.Name, new CursorRequest(second.NextCursor, 2), ct: Ct);
        // MongoDB orders strings by UTF-8 bytes (simple binary collation).
        Assert.Equal(["z", "ß", "ä", "émile", "日本"], first.Data.Concat(second.Data).Concat(third.Data).Select(r => r.Name));
    }

    [Fact]
    public async Task Scroll_metadata_follows_the_cursor_name()
    {
        var page = await All().ToCursorPageAsync(r => r.Number, new CursorRequest(null, 10), cursorName: "c", ct: Ct);
        Assert.Equal("c", page.GetScrollMetadata().PageName);
        Assert.NotNull(page.GetScrollMetadata().NextPage);
    }
}
