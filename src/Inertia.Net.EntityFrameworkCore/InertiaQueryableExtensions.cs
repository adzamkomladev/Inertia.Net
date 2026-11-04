using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Inertia.Net;

/// <summary>Creates pagination values from Entity Framework Core queries.</summary>
public static class InertiaQueryableExtensions
{
    /// <summary>Creates a numbered page from an ordered query, optionally counting its total.</summary>
    /// <remarks>Apply a stable, fully unique ordering before calling this method. The total uses a 64-bit count.</remarks>
    public static Task<Page<T>> ToPageAsync<T>(this IQueryable<T> query, PageRequest request, bool countTotal = true, string? pageName = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);
        Pagination.Validate(request.Page, request.PerPage);
        if (!QueryOrdering.IsOrdered(query.Expression))
            throw new InvalidOperationException("Order the query before paging it");
        return Pagination.PaginateAsync(request,
            async (skip, take, token) => (IReadOnlyList<T>)await query.Skip(skip).Take(take).ToListAsync(token).ConfigureAwait(false),
            countTotal ? token => query.LongCountAsync(token) : null, pageName, ct);
    }

    /// <summary>Creates a cursor page, ordering the query by one unique non-null key.</summary>
    /// <remarks>The query must be unordered. The database provider must translate comparison and ordering for the key type.</remarks>
    public static Task<CursorPage<T>> ToCursorPageAsync<T, TKey>(this IQueryable<T> query, Expression<Func<T, TKey>> key, CursorRequest request,
        bool descending = false, string? cursorName = null, CancellationToken ct = default) where TKey : notnull
    {
        ValidateCursorQuery(query, request);
        ArgumentNullException.ThrowIfNull(key);
        ValidateKey<TKey>();
        return Pagination.PaginateAsync(request, key.Compile(), async (cursor, take, token) =>
        {
            KeysetKey[] keys = [new(key, cursor is { } value ? value.Value : null, descending)];
            var ordered = KeysetExpression.Apply(query, keys, cursor is not null, cursor?.Direction == CursorDirection.Before);
            return (IReadOnlyList<T>)await ordered.Take(take).ToListAsync(token).ConfigureAwait(false);
        }, cursorName, ct);
    }

    /// <summary>Creates a cursor page, ordering the query by a non-null key and unique tie-breaker.</summary>
    /// <remarks>The query must be unordered. Both columns use the requested direction and must be translatable by the provider.</remarks>
    public static Task<CursorPage<T>> ToCursorPageAsync<T, TKey, TTie>(this IQueryable<T> query, Expression<Func<T, TKey>> key, Expression<Func<T, TTie>> tieBreaker,
        CursorRequest request, bool descending = false, string? cursorName = null, CancellationToken ct = default) where TKey : notnull where TTie : notnull
    {
        ValidateCursorQuery(query, request);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tieBreaker);
        ValidateKey<TKey>();
        ValidateKey<TTie>();
        return FetchCompositePageAsync(query, key, tieBreaker, request, descending, cursorName ?? request.CursorName, ct);
    }

    private static async Task<CursorPage<T>> FetchCompositePageAsync<T, TKey, TTie>(IQueryable<T> query,
        Expression<Func<T, TKey>> key, Expression<Func<T, TTie>> tieBreaker, CursorRequest request,
        bool descending, string cursorName, CancellationToken ct) where TKey : notnull where TTie : notnull
    {
        ct.ThrowIfCancellationRequested();
        var cursor = CursorCodec.Decode<TKey, TTie>(request.Cursor);
        var before = cursor?.Cursor.Direction == CursorDirection.Before;
        KeysetKey[] keys = [
            new(key, cursor is { } value ? value.Cursor.Value : null, descending),
            new(tieBreaker, cursor is { } tie ? tie.TieBreaker : null, descending),
        ];
        var ordered = KeysetExpression.Apply(query, keys, cursor is not null, before);
        var rows = await ordered.Take(request.PerPage + 1).ToListAsync(ct).ConfigureAwait(false);
        return Pagination.CreateCursorPage<T, CompositeEncoder<T, TKey, TTie>>(rows, request.PerPage, before,
            cursor is not null, cursor is null ? null : request.Cursor, cursorName, new(key.Compile(), tieBreaker.Compile()));
    }

    private static void ValidateKey<TKey>()
    {
        if (Nullable.GetUnderlyingType(typeof(TKey)) is not null)
            throw new NotSupportedException("Keyset paging needs non-null keys: order by a non-null column or coalesce the expression, and add a unique tie-breaker.");
        CursorCodec.ValidateType<TKey>();
    }

    private static void ValidateCursorQuery<T>(IQueryable<T> query, CursorRequest request)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);
        Pagination.Validate(1, request.PerPage);
        if (QueryOrdering.IsOrdered(query.Expression))
            throw new InvalidOperationException("Cursor pagination applies its own ordering; remove OrderBy before calling ToCursorPageAsync.");
    }

    private readonly struct CompositeEncoder<T, TKey, TTie>(Func<T, TKey> key, Func<T, TTie> tieBreaker) : ICursorEncoder<T>
    {
        public string Encode(T row, CursorDirection direction) => CursorCodec.Encode(new Cursor<TKey>(direction, key(row)), tieBreaker(row));
    }
}
