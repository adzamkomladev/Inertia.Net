using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Benchmarks;

/// <summary>Handler construction and rendering of the same numbered feed, plus request and cursor controls.</summary>
[MemoryDiagnoser]
public class PaginationBenchmarks
{
    private const string Component = "Feed/Index";
    private static readonly User[] Source = [.. Enumerable.Range(1, 10000).Select(i => new User(i, $"User {i}", $"user{i}@example.com", i % 3 == 0))];
    private static readonly PageRequest Request = new(3, 20);
    private static readonly Cursor<DateTimeOffset> Cursor = new(CursorDirection.After, new DateTimeOffset(2026, 11, 4, 12, 34, 56, TimeSpan.FromHours(2)));
    private const long TieBreaker = 1234567890;
    private ServiceProvider _services = null!;
    private DefaultHttpContext _full = null!;
    private DefaultHttpContext _partial = null!;
    private IQueryCollection _valid = null!;
    private IQueryCollection _garbage = null!;
    private readonly ArrayBufferWriter<byte> _buffer = new(8192);
    private Utf8JsonWriter _writer = null!;
    private PaginationFeedPage _plain = null!;
    private PageRequest? _requestResult;
    private (Cursor<DateTimeOffset> Cursor, long TieBreaker)? _cursorResult;

    /// <summary>Builds the host and verifies equivalent output before measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _services = (ServiceProvider)BenchApp.CreateServices();
        var version = BenchApp.Version(_services);
        _full = BenchApp.CreateContext(_services, "/feed", (InertiaHeaders.Inertia, "true"), (InertiaHeaders.Version, version));
        _full.Request.QueryString = new QueryString("?page=3&perPage=20");
        _partial = BenchApp.CreateContext(_services, "/feed", (InertiaHeaders.Inertia, "true"), (InertiaHeaders.Version, version),
            (InertiaHeaders.PartialComponent, Component), (InertiaHeaders.PartialData, "posts"),
            (InertiaHeaders.InfiniteScrollMergeIntent, "prepend"));
        _partial.Request.QueryString = _full.Request.QueryString;
        _valid = _full.Request.Query;
        var garbage = new DefaultHttpContext();
        garbage.Request.QueryString = new QueryString("?page=garbage&perPage=garbage");
        _garbage = garbage.Request.Query;
        _writer = new Utf8JsonWriter(_buffer);
        _plain = HandRolledPage();
        CheckOutputAsync(_full, "mergeProps").GetAwaiter().GetResult();
        CheckOutputAsync(_partial, "prependProps").GetAwaiter().GetResult();
        if (PageRequest.From(_valid) != Request || PageRequest.From(_garbage) != new PageRequest())
            throw new InvalidOperationException("Request fixture did not parse the expected page.");
        CursorRoundTrip();
        if (_cursorResult != (Cursor, TieBreaker)) throw new InvalidOperationException("Cursor fixture did not round-trip both values.");
    }

    /// <summary>Releases services and writer resources.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _writer?.Dispose();
        _services?.Dispose();
    }

    /// <summary>Constructs the previous hand-written page and renders a full Inertia response.</summary>
    [Benchmark(Baseline = true)]
    public Task HandRolledFull() => Execute(_full, false);

    /// <summary>Constructs a helper page and renders a full Inertia response.</summary>
    [Benchmark]
    public Task PaginateFull() => Execute(_full, true);

    /// <summary>Constructs the previous hand-written page and renders a prepend partial response.</summary>
    [Benchmark]
    public Task HandRolledPartial() => Execute(_partial, false);

    /// <summary>Constructs a helper page and renders a prepend partial response.</summary>
    [Benchmark]
    public Task PaginatePartial() => Execute(_partial, true);

    /// <summary>Parses an already parsed, valid HTTP query.</summary>
    [Benchmark]
    public PageRequest RequestValid() => _requestResult = PageRequest.From(_valid);

    /// <summary>Parses an already parsed HTTP query containing invalid user values.</summary>
    [Benchmark]
    public PageRequest RequestGarbage() => _requestResult = PageRequest.From(_garbage);

    /// <summary>Encodes and decodes a two-key cursor without serializer metadata.</summary>
    [Benchmark]
    public void CursorRoundTrip() => _cursorResult = CursorCodec.Decode<DateTimeOffset, long>(CursorCodec.Encode(Cursor, TieBreaker));

    /// <summary>Serializes the same feed data and pagination metadata without Inertia.</summary>
    [Benchmark]
    public void PlainSerialize()
    {
        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);
        JsonSerializer.Serialize(_writer, _plain, BenchJsonContext.Default.PaginationFeedPage);
        _writer.Flush();
    }

    private static PaginationFeedPage HandRolledPage()
    {
        var data = Source.Skip((Request.Page - 1) * Request.PerPage).Take(Request.PerPage).ToArray();
        return new PaginationFeedPage(data, Request.Page, Request.PerPage, Source.Length, Request.Page * Request.PerPage < Source.Length);
    }

    private Task Execute(DefaultHttpContext context, bool paginate)
    {
        context.Response.Headers.Clear();
        var prop = paginate
            ? Scroll(Pagination.Paginate(Source, Request))
            : Scroll(HandRolledPage(), metadata: ScrollMetadata.FromPage(Request.Page, true));
        return Render(Component, new InertiaProps { ["posts"] = prop }).ExecuteAsync(context);
    }

    private async Task CheckOutputAsync(DefaultHttpContext context, string mergeProperty)
    {
        using var body = new MemoryStream();
        context.Response.Body = body;
        await Execute(context, false);
        var baseline = body.ToArray();
        body.SetLength(0);
        await Execute(context, true);
        var current = body.ToArray();
        context.Response.Body = Stream.Null;
        if (!baseline.AsSpan().SequenceEqual(current)) throw new InvalidOperationException("Pagination render fixtures differ on the wire.");
        using var document = JsonDocument.Parse(current);
        var root = document.RootElement;
        var posts = root.GetProperty("props").GetProperty("posts");
        if (posts.GetProperty("data").GetArrayLength() != 20 || posts.GetProperty("data")[0].GetProperty("id").GetInt32() != 41
            || posts.GetProperty("currentPage").GetInt32() != 3 || posts.GetProperty("total").GetInt64() != 10000
            || root.GetProperty(mergeProperty)[0].GetString() != "posts.data"
            || root.GetProperty("scrollProps").GetProperty("posts").GetProperty("nextPage").GetInt32() != 4)
            throw new InvalidOperationException("Pagination render fixture returned the wrong page or merge metadata.");
    }
}

/// <summary>Hand-written pagination metadata matching the helper's wire shape for a fair serialization comparison.</summary>
public sealed record PaginationFeedPage
{
    /// <summary>Creates the hand-written page used as the baseline.</summary>
    public PaginationFeedPage(IReadOnlyList<User> data, int currentPage, int perPage, long total, bool hasMore)
    {
        Data = data;
        CurrentPage = currentPage;
        PerPage = perPage;
        Total = total;
        HasMore = hasMore;
    }

    /// <summary>The selected data.</summary>
    public IReadOnlyList<User> Data { get; } = null!;
    /// <summary>The current page number.</summary>
    public int CurrentPage { get; }
    /// <summary>The requested page size.</summary>
    public int PerPage { get; }
    /// <summary>The source count.</summary>
    public long Total { get; }
    /// <summary>The final numbered page.</summary>
    public long LastPage => Math.Max(1, Total / PerPage + (Total % PerPage == 0 ? 0 : 1));
    /// <summary>The first item index.</summary>
    public long? From => Data.Count == 0 ? null : ((long)CurrentPage - 1) * PerPage + 1;
    /// <summary>The last item index.</summary>
    public long? To => From is long from ? from + Data.Count - 1 : null;
    /// <summary>Whether another page exists.</summary>
    public bool HasMore { get; }
    /// <summary>The numbered-page query parameter.</summary>
    public string PageName => "page";
}
