using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>A request for a cursor page.</summary>
public sealed record CursorRequest(string? Cursor = null, int PerPage = 20)
{
    internal string CursorName { get; init; } = "cursor";

    /// <summary>Reads and clamps paging parameters from a query string.</summary>
    public static CursorRequest From(IQueryCollection query, int defaultPerPage = 20, int maxPerPage = 100, string cursorName = "cursor", string perPageName = "perPage") =>
        new(query.TryGetValue(cursorName, out var cursor) ? cursor.ToString() : null, PageRequest.ParsePerPage(query, defaultPerPage, maxPerPage, perPageName)) { CursorName = cursorName };

    /// <summary>Binds a request from the current HTTP query and configured defaults.</summary>
    public static ValueTask<CursorRequest?> BindAsync(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<InertiaOptions>>().Value.Pagination;
        return ValueTask.FromResult<CursorRequest?>(From(context.Request.Query, options.DefaultPerPage, options.MaxPerPage, options.CursorName, options.PerPageName));
    }
}
