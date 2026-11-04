using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inertia.Net.Tests;

public class CursorLoggingTests
{
    [Fact]
    public async Task Bound_invalid_cursor_logs_once_at_debug_without_cursor_data()
    {
        using var logs = new CursorLogProvider();
        var harness = new Harness(configureServices: services => services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(logs)));
        const string cursor = "private-cursor-value%%%";
        var request = (await CursorRequest.BindAsync(harness.Context("/?cursor=" + Uri.EscapeDataString(cursor))))!;
        var page = await Paginate(request);
        Assert.Equal(new[] { 1 }, page.Data);
        var entry = Assert.Single(logs.Entries);
        Assert.Equal("Inertia.Net.Pagination", entry.Category);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal("Ignoring an invalid pagination cursor; loading the first page.", entry.Message);
        var state = Assert.Single(entry.State);
        Assert.Equal("{OriginalFormat}", state.Key);
        Assert.Equal(entry.Message, state.Value);
        Assert.DoesNotContain(cursor, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(cursor, string.Join(',', entry.State.Select(item => item.Value)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("%%%")]
    [InlineData("WyJhIiwiZGVjb2RlZC1zZWNyZXQiXQ")]
    [InlineData("WyJhIiwxLDJd")]
    public async Task Standalone_requests_log_malformed_or_foreign_cursors_without_payloads(string cursor)
    {
        using var logs = new CursorLogProvider();
        var query = new QueryCollection(QueryHelpers.ParseQuery("?cursor=" + Uri.EscapeDataString(cursor)));
        var request = CursorRequest.From(query) with { Logger = logs.CreateLogger("Standalone") };
        var page = await Paginate(request);
        Assert.Equal(new[] { 1 }, page.Data);
        Assert.Null(page.CurrentCursor);
        var entry = Assert.Single(logs.Entries);
        Assert.Equal("Standalone", entry.Category);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal("Ignoring an invalid pagination cursor; loading the first page.", entry.Message);
        Assert.Equal(new KeyValuePair<string, object?>("{OriginalFormat}", entry.Message), Assert.Single(entry.State));
    }

    [Fact]
    public async Task Missing_empty_and_valid_cursors_do_not_log()
    {
        using var logs = new CursorLogProvider();
        foreach (var cursor in new[] { null, "", CursorCodec.Encode(new Cursor<int>(CursorDirection.After, 1)) })
            await Paginate(new CursorRequest(cursor) { Logger = logs.CreateLogger("Standalone") });
        Assert.Empty(logs.Entries);
    }

    [Fact]
    public async Task Debug_filter_can_disable_diagnostics_without_changing_fallback()
    {
        using var logs = new CursorLogProvider();
        var harness = new Harness(configureServices: services => services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information).AddProvider(logs)));
        var request = (await CursorRequest.BindAsync(harness.Context("/?cursor=invalid")))!;
        Assert.Equal(new[] { 1 }, (await Paginate(request)).Data);
        Assert.Empty(logs.Entries);
    }

    [Fact]
    public async Task Missing_logger_and_unconfigured_http_services_are_safe()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.QueryString = new QueryString("?cursor=invalid");
        var http = CursorRequest.From(context);
        var bound = (await CursorRequest.BindAsync(context))!;
        var standalone = new CursorRequest("invalid");
        Assert.Null(http.Logger);
        Assert.Equal(standalone, http);
        Assert.Equal(standalone, bound);
        foreach (var request in new[] { http, bound, standalone }) Assert.Equal(new[] { 1 }, (await Paginate(request)).Data);
    }

    [Fact]
    public async Task Http_factory_uses_configured_names_limits_and_explicit_overrides()
    {
        using var logs = new CursorLogProvider();
        var harness = new Harness(options =>
        {
            options.Pagination.DefaultPerPage = 7;
            options.Pagination.MaxPerPage = 12;
            options.Pagination.CursorName = "c";
            options.Pagination.PerPageName = "size";
        }, configureServices: services => services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(logs)));
        var context = harness.Context("/?c=invalid&size=999&alternate=other&limit=30");
        var configured = CursorRequest.From(context);
        Assert.Equal(new CursorRequest("invalid", 12) { CursorName = "c" }, configured);
        Assert.NotNull(configured.Logger);
        var overridden = CursorRequest.From(context, defaultPerPage: 3, maxPerPage: 4, cursorName: "alternate", perPageName: "limit");
        Assert.Equal(new CursorRequest("other", 4) { CursorName = "alternate" }, overridden);
        Assert.Equal(3, CursorRequest.From(harness.Context("/"), defaultPerPage: 3).PerPage);
        await Paginate(overridden);
        Assert.Equal("Inertia.Net.Pagination", Assert.Single(logs.Entries).Category);
    }

    [Fact]
    public void Logger_does_not_affect_record_equality_hashing_or_copies()
    {
        using var logs = new CursorLogProvider();
        var plain = new CursorRequest("opaque", 7) { CursorName = "c" };
        var first = plain with { Logger = logs.CreateLogger("First") };
        var second = plain with { Logger = logs.CreateLogger("Second") };
        Assert.Equal(plain, first);
        Assert.True(first == second);
        Assert.True(first.Equals((object)second));
        Assert.Equal(plain.GetHashCode(), first.GetHashCode());
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(new HashSet<CursorRequest> { plain, first, second });
        Assert.NotEqual(first, first with { Cursor = "different" });
        Assert.NotEqual(first, first with { PerPage = 8 });
        Assert.NotEqual(first, first with { CursorName = "different" });
        Assert.Same(first.Logger, (first with { PerPage = 8 }).Logger);
    }

    [Fact]
    public void Logger_is_excluded_from_source_generated_json_and_typescript()
    {
        using var logs = new CursorLogProvider();
        var request = new CursorRequest("opaque", 7) { Logger = logs.CreateLogger("Standalone") };
        Assert.Equal("""{"cursor":"opaque","perPage":7}""", JsonSerializer.Serialize(request, CursorRequestJsonContext.Default.CursorRequest));
        var options = new InertiaOptions();
        options.TypeScript.Page<CursorRequest>("CursorRequest");
        var output = new TypeScriptGenerator(new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = CursorRequestJsonContext.Default }, options).Generate();
        Assert.DoesNotContain("logger", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ILogger", output, StringComparison.Ordinal);
        Assert.Contains("cursor: string | null", output);
        Assert.Contains("perPage: number", output);
    }

    private static Task<CursorPage<int>> Paginate(CursorRequest request) =>
        Pagination.PaginateAsync<int, int>(request, item => item,
            (_, _, _) => Task.FromResult<IReadOnlyList<int>>([1]), ct: TestContext.Current.CancellationToken);

    private sealed class CursorLogProvider : ILoggerProvider
    {
        internal sealed record Entry(string Category, LogLevel Level, string Message, Exception? Exception, KeyValuePair<string, object?>[] State);
        public ConcurrentQueue<Entry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }

        private sealed class Logger(CursorLogProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), exception,
                    state is IEnumerable<KeyValuePair<string, object?>> values ? values.ToArray() : []));
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CursorRequest))]
internal sealed partial class CursorRequestJsonContext : JsonSerializerContext;
