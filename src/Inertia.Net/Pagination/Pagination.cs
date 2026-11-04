namespace Inertia.Net;

/// <summary>Creates offset and cursor pages from materialized data or fetch delegates.</summary>
public static class Pagination
{
    /// <summary>Pages a materialized source and includes its total count.</summary>
    public static Page<T> Paginate<T>(IReadOnlyList<T> source, PageRequest request, string? pageName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.Page, request.PerPage);
        var skip = (long)(request.Page - 1) * request.PerPage;
        var length = skip >= source.Count ? 0 : (int)Math.Min(request.PerPage, source.Count - skip);
        var data = new T[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = source[(int)skip + i];
        }

        return new Page<T>(data, request.Page, request.PerPage, source.Count, skip + length < source.Count, pageName ?? request.PageName);
    }

    /// <summary>Pages data fetched by a delegate, optionally including a total count.</summary>
    /// <remarks>Offsets above <c>int.MaxValue</c> return an empty page without fetching; the optional count still runs.</remarks>
    public static async Task<Page<T>> PaginateAsync<T>(PageRequest request,
        Func<int, int, CancellationToken, Task<IReadOnlyList<T>>> fetch,
        Func<CancellationToken, Task<long>>? count = null, string? pageName = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fetch);
        Validate(request.Page, request.PerPage);
        var skip = (long)(request.Page - 1) * request.PerPage;
        ct.ThrowIfCancellationRequested();
        var rows = skip > int.MaxValue
            ? Array.Empty<T>()
            : await fetch((int)skip, request.PerPage + 1, ct).ConfigureAwait(false);
        var hasMore = rows.Count > request.PerPage;
        var data = Trim(rows, request.PerPage, false);
        var total = count is null ? (long?)null : await count(ct).ConfigureAwait(false);
        return new Page<T>(data, request.Page, request.PerPage, total, hasMore, pageName ?? request.PageName);
    }

    /// <summary>Pages data fetched in cursor order and returns it in display order.</summary>
    public static Task<CursorPage<T>> PaginateAsync<T, TKey>(CursorRequest request, Func<T, TKey> key,
        Func<Cursor<TKey>?, int, CancellationToken, Task<IReadOnlyList<T>>> fetch,
        string? cursorName = null, CancellationToken ct = default) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(fetch);
        Validate(1, request.PerPage);
        CursorCodec.ValidateType<TKey>();
        return FetchCursorPageAsync(request, key, fetch, cursorName ?? request.CursorName, ct);
    }

    private static async Task<CursorPage<T>> FetchCursorPageAsync<T, TKey>(CursorRequest request, Func<T, TKey> key,
        Func<Cursor<TKey>?, int, CancellationToken, Task<IReadOnlyList<T>>> fetch,
        string cursorName, CancellationToken ct) where TKey : notnull
    {
        ct.ThrowIfCancellationRequested();
        var cursor = CursorCodec.Decode<TKey>(request.Cursor);
        if (cursor is null) request.LogInvalidCursor();
        var before = cursor?.Direction == CursorDirection.Before;
        var rows = await fetch(cursor, request.PerPage + 1, ct).ConfigureAwait(false);
        var hasMore = rows.Count > request.PerPage;
        var data = Trim(rows, request.PerPage, before);
        string? previous = null;
        string? next = null;
        if (data.Count > 0)
        {
            if (before ? hasMore : cursor is not null)
            {
                previous = CursorCodec.Encode(new Cursor<TKey>(CursorDirection.Before, key(data[0])));
            }

            if (before || hasMore)
            {
                next = CursorCodec.Encode(new Cursor<TKey>(CursorDirection.After, key(data[^1])));
            }
        }

        return new CursorPage<T>(data, request.PerPage, next, previous, hasMore, cursorName, cursor is null ? null : request.Cursor);
    }

    private static void Validate(int page, int perPage)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (perPage < 1 || perPage == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(perPage));
    }

    private static IReadOnlyList<T> Trim<T>(IReadOnlyList<T> rows, int take, bool reverse)
    {
        var length = Math.Min(rows.Count, take);
        if (!reverse && length == rows.Count) return rows;
        var result = new T[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = rows[reverse ? length - i - 1 : i];
        }
        return result;
    }
}
