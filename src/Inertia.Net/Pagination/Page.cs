namespace Inertia.Net;

internal interface IPaginatedValue;

/// <summary>A numbered page of data.</summary>
public sealed record Page<T> : IProvidesScrollMetadata, IPaginatedValue
{
    /// <summary>Creates a numbered page.</summary>
    public Page(IReadOnlyList<T> data, int currentPage, int perPage, long? total, bool hasMore, string pageName = "page")
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentPage, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
        Data = data;
        CurrentPage = currentPage;
        PerPage = perPage;
        Total = total;
        HasMore = hasMore;
        PageName = pageName;
    }

    /// <summary>The page's items.</summary>
    public IReadOnlyList<T> Data { get; init; }
    /// <summary>The current page number.</summary>
    public int CurrentPage { get; init; }
    /// <summary>The requested page size.</summary>
    public int PerPage { get; init; }
    /// <summary>The total item count, if counted.</summary>
    public long? Total { get; init; }
    /// <summary>The last page number, if counted.</summary>
    public long? LastPage => Total is long total ? Math.Max(1, total / PerPage + (total % PerPage == 0 ? 0 : 1)) : null;
    /// <summary>The one-based index of the first item, or null for an empty page.</summary>
    public long? From => Data.Count == 0 ? null : ((long)CurrentPage - 1) * PerPage + 1;
    /// <summary>The one-based index of the last item, or null for an empty page.</summary>
    public long? To => From is long from ? from + Data.Count - 1 : null;
    /// <summary>Whether another page exists.</summary>
    public bool HasMore { get; init; }
    /// <summary>The page query parameter name.</summary>
    public string PageName { get; init; }
    /// <inheritdoc />
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(CurrentPage, HasMore && CurrentPage < int.MaxValue, PageName);
}
