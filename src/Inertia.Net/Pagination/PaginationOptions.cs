namespace Inertia.Net;

/// <summary>Defaults for pagination request binding.</summary>
public sealed class PaginationOptions
{
    /// <summary>Default page size.</summary>
    public int DefaultPerPage { get; set; } = 20;
    /// <summary>Maximum page size; must be below <c>int.MaxValue</c> to allow fetching one extra row.</summary>
    public int MaxPerPage { get; set; } = 100;
    /// <summary>Numbered page parameter name.</summary>
    public string PageName { get; set; } = "page";
    /// <summary>Page size parameter name.</summary>
    public string PerPageName { get; set; } = "perPage";
    /// <summary>Cursor parameter name.</summary>
    public string CursorName { get; set; } = "cursor";
}
