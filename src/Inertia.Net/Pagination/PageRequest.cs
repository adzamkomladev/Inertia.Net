using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>A request for a numbered page.</summary>
public sealed record PageRequest(int Page = 1, int PerPage = 20)
{
    internal string PageName { get; init; } = "page";

    /// <summary>Reads and clamps paging parameters from a query string.</summary>
    public static PageRequest From(IQueryCollection query, int defaultPerPage = 20, int maxPerPage = 100, string pageName = "page", string perPageName = "perPage")
    {
        var values = query[pageName];
        var page = values.Count == 1 && int.TryParse(values[0].AsSpan(), out var parsed) && parsed > 0 ? parsed : 1;
        return new(page, ParsePerPage(query, defaultPerPage, maxPerPage, perPageName)) { PageName = pageName };
    }

    internal static int ParsePerPage(IQueryCollection query, int fallback, int max, string name)
    {
        max = Math.Clamp(max, 1, int.MaxValue - 1);
        fallback = Math.Clamp(fallback, 1, max);
        var values = query[name];
        return values.Count == 1 && int.TryParse(values[0].AsSpan(), out var parsed) && parsed > 0
            ? Math.Min(parsed, max) : Math.Min(fallback, max);
    }

    /// <summary>Binds a request from the current HTTP query and configured defaults.</summary>
    public static ValueTask<PageRequest?> BindAsync(HttpContext context)
    {
        var options = (InertiaMountServices.Options(context) ?? context.RequestServices.GetRequiredService<IOptions<InertiaOptions>>().Value).Pagination;
        return ValueTask.FromResult<PageRequest?>(From(context.Request.Query, options.DefaultPerPage, options.MaxPerPage, options.PageName, options.PerPageName));
    }
}
