namespace Inertia.Net;

/// <summary>A cursor page of data.</summary>
public sealed record CursorPage<T> : IProvidesScrollMetadata, IPaginatedValue
{
    /// <summary>Creates a cursor page.</summary>
    public CursorPage(IReadOnlyList<T> data, int perPage, string? nextCursor, string? previousCursor, bool hasMore, string cursorName = "cursor", string? currentCursor = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        Data = data;
        PerPage = perPage;
        NextCursor = nextCursor;
        PreviousCursor = previousCursor;
        HasMore = hasMore;
        CursorName = cursorName;
        CurrentCursor = currentCursor;
    }
    /// <summary>The page's items.</summary>
    public IReadOnlyList<T> Data { get; init; }
    /// <summary>The requested page size.</summary>
    public int PerPage { get; init; }
    /// <summary>The cursor for the next page.</summary>
    public string? NextCursor { get; init; }
    /// <summary>The cursor for the previous page.</summary>
    public string? PreviousCursor { get; init; }
    /// <summary>Whether more items exist in the requested direction.</summary>
    public bool HasMore { get; init; }
    /// <summary>The cursor query parameter name.</summary>
    public string CursorName { get; init; }
    /// <summary>The cursor used for this request.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? CurrentCursor { get; init; }
    /// <inheritdoc />
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromCursor(CurrentCursor, PreviousCursor, NextCursor, CursorName);
}
