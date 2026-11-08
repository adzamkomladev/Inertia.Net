using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>A request for a cursor page.</summary>
public sealed record CursorRequest(string? Cursor = null, int PerPage = 20)
{
    internal string CursorName { get; init; } = "cursor";

    /// <summary>
    /// Receives cursor diagnostics at Debug level. HTTP binding and <see cref="From(HttpContext, int?, int?, string?, string?)"/>
    /// populate this from the request's logger factory when available; standalone callers can set it explicitly.
    /// The logger does not participate in request equality or serialization.
    /// </summary>
    [JsonIgnore]
    public ILogger? Logger { get; init; }

    /// <summary>Reads and clamps paging parameters from a query string. Set <see cref="Logger"/> to enable standalone diagnostics.</summary>
    public static CursorRequest From(IQueryCollection query, int defaultPerPage = 20, int maxPerPage = 100, string cursorName = "cursor", string perPageName = "perPage") =>
        FromQuery(query, defaultPerPage, maxPerPage, cursorName, perPageName, logger: null);

    private static CursorRequest FromQuery(IQueryCollection query, int defaultPerPage, int maxPerPage, string cursorName, string perPageName, ILogger? logger) =>
        new(query.TryGetValue(cursorName, out var cursor) ? cursor.ToString() : null, PageRequest.ParsePerPage(query, defaultPerPage, maxPerPage, perPageName))
        {
            CursorName = cursorName,
            Logger = logger,
        };

    /// <summary>Reads the HTTP query, configured pagination defaults and request logger. Explicit arguments override the configured defaults.</summary>
    public static CursorRequest From(HttpContext context, int? defaultPerPage = null, int? maxPerPage = null, string? cursorName = null, string? perPageName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.RequestServices;
        var options = InertiaMountServices.Options(context)?.Pagination;
        return FromQuery(context.Request.Query, defaultPerPage ?? options?.DefaultPerPage ?? 20, maxPerPage ?? options?.MaxPerPage ?? 100,
            cursorName ?? options?.CursorName ?? "cursor", perPageName ?? options?.PerPageName ?? "perPage",
            services?.GetService<ILoggerFactory>()?.CreateLogger("Inertia.Net.Pagination"));
    }

    /// <summary>Binds a request from the current HTTP query, configured defaults and request logger.</summary>
    public static ValueTask<CursorRequest?> BindAsync(HttpContext context) => ValueTask.FromResult<CursorRequest?>(From(context));

    /// <inheritdoc />
    public bool Equals(CursorRequest? other) => other is not null && Cursor == other.Cursor && PerPage == other.PerPage && CursorName == other.CursorName;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Cursor, PerPage, CursorName);

    internal void LogInvalidCursor()
    {
        if (!string.IsNullOrEmpty(Cursor) && Logger is { } logger) PaginationLog.InvalidCursor(logger);
    }
}

internal static partial class PaginationLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignoring an invalid pagination cursor; loading the first page.")]
    internal static partial void InvalidCursor(ILogger logger);
}
