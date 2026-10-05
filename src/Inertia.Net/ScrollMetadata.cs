namespace Inertia.Net;

/// <summary>Paging metadata of a scroll prop, emitted under <c>scrollProps</c>. Page values must be <c>int</c>, <c>long</c>, <c>string</c> or null.</summary>
/// <param name="PageName">The query-string parameter that holds the page.</param>
/// <param name="PreviousPage">The previous page, or null on the first page.</param>
/// <param name="NextPage">The next page, or null on the last page.</param>
/// <param name="CurrentPage">The current page.</param>
public sealed record ScrollMetadata(string PageName = "page", object? PreviousPage = null, object? NextPage = null, object? CurrentPage = null)
{
    /// <summary>Metadata for numbered pages starting at 1.</summary>
    public static ScrollMetadata FromPage(int currentPage, bool hasMore, string pageName = "page") =>
        new(pageName, currentPage > 1 ? currentPage - 1 : null, hasMore ? currentPage + 1 : null, currentPage);
}

/// <summary>Implemented by paged values so <see cref="Inertia.Scroll{T}(T, string, ScrollMetadata?)"/> can read their metadata.</summary>
public interface IProvidesScrollMetadata
{
    /// <summary>Returns the paging metadata of this value.</summary>
    ScrollMetadata GetScrollMetadata();
}
