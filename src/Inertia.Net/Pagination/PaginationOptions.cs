namespace Inertia.Net;

/// <summary>Defaults for pagination request binding.</summary>
public sealed class PaginationOptions
{
    /// <summary>Default page size.</summary>
    public int DefaultPerPage { get; set; } = 20;
    /// <summary>Maximum page size; must be below <c>int.MaxValue</c> to allow fetching one extra row.</summary>
    public int MaxPerPage { get; set; } = 100;
    /// <summary>
    /// The last numbered page a bound <see cref="PageRequest"/> may fetch. A page beyond it is empty and runs no query or count.
    /// Default <c>null</c>: no ceiling. Must be positive.
    /// </summary>
    public int? MaxPage { get; set; }
    /// <summary>Numbered page parameter name.</summary>
    public string PageName { get; set; } = "page";
    /// <summary>Page size parameter name.</summary>
    public string PerPageName { get; set; } = "perPage";
    /// <summary>Cursor parameter name.</summary>
    public string CursorName { get; set; } = "cursor";
}
