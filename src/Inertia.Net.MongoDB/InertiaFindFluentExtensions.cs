using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Inertia.Net;

/// <summary>Creates pagination values from MongoDB finds (<c>collection.Find(filter)</c>).</summary>
#pragma warning disable RS0026 // The same overloads as Inertia.Net.EntityFrameworkCore's, so code moves between the two unchanged.
public static class InertiaFindFluentExtensions
{
    /// <summary>Creates a numbered page from a sorted find, optionally counting the matching documents.</summary>
    /// <remarks>Sort the find (<c>SortBy</c>) by a stable, fully unique order before calling this method. The find's skip and limit are replaced.</remarks>
    public static Task<Page<T>> ToPageAsync<T>(this IFindFluent<T, T> find, PageRequest request, bool countTotal = true, string? pageName = null, CancellationToken ct = default)
        => ToProjectedPageAsync(find, request, countTotal, pageName, ct);

    /// <summary>Creates a numbered page of projected results from a sorted find.</summary>
    /// <remarks>Apply a stable, fully unique sort before paging. The find's skip and limit are replaced.</remarks>
    public static Task<Page<TProjection>> ToProjectedPageAsync<TDocument, TProjection>(this IFindFluent<TDocument, TProjection> find, PageRequest request,
        bool countTotal = true, string? pageName = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(request);
        Pagination.Validate(request.Page, request.PerPage);
        if (find.Options.Sort is null)
            throw new InvalidOperationException("Sort the find before paging it");
        return Pagination.PaginateAsync(request,
            async (skip, take, token) => (IReadOnlyList<TProjection>)await find.Skip(skip).Limit(take).ToListAsync(token).ConfigureAwait(false),
            countTotal ? token => CountAsync(find, token) : null, pageName, ct);
    }

    /// <summary>Creates a cursor page, sorting the find by one unique key: <c>ToCursorPageAsync(p =&gt; p.Id, request)</c>.</summary>
    /// <remarks>
    /// The find must be unsorted; the keyset condition is added to its filter. Keys may be <see cref="ObjectId"/> or a type the cursor
    /// codec supports (<c>int</c>, <c>long</c>, <c>string</c>, <c>Guid</c>, <c>DateTime</c>, <c>DateTimeOffset</c>, <c>decimal</c>, <c>double</c>, <c>bool</c>).
    /// </remarks>
    public static Task<CursorPage<T>> ToCursorPageAsync<T, TKey>(this IFindFluent<T, T> find, Expression<Func<T, TKey>> key, CursorRequest request,
        bool descending = false, string? cursorName = null, CancellationToken ct = default) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(key);
        return Keyset<T, TKey, TKey>.PageAsync(find, key, null, request, descending, cursorName, ct);
    }

    /// <summary>Creates a cursor page, sorting the find by a key and a unique tie-breaker: <c>ToCursorPageAsync(p =&gt; p.CreatedAt, p =&gt; p.Id, request)</c>.</summary>
    /// <remarks>The find must be unsorted. Both fields use the requested direction.</remarks>
    public static Task<CursorPage<T>> ToCursorPageAsync<T, TKey, TTie>(this IFindFluent<T, T> find, Expression<Func<T, TKey>> key, Expression<Func<T, TTie>> tieBreaker,
        CursorRequest request, bool descending = false, string? cursorName = null, CancellationToken ct = default) where TKey : notnull where TTie : notnull
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tieBreaker);
        return Keyset<T, TKey, TTie>.PageAsync(find, key, tieBreaker, request, descending, cursorName, ct);
    }

    // CountDocuments honors the find's skip and limit, which the page query has just set: count the filter alone.
    private static Task<long> CountAsync<TDocument, TProjection>(IFindFluent<TDocument, TProjection> find, CancellationToken ct)
    {
        find.Options.Skip = null;
        find.Options.Limit = null;
        return find.CountDocumentsAsync(ct);
    }
}
#pragma warning restore RS0026

/// <summary>
/// Keyset paging on a find. A cursor holds the last row's key values; an <see cref="ObjectId"/> is held as its 24-character string, as the
/// cursor codec has no driver dependency.
/// </summary>
internal static class Keyset<T, TKey, TTie> where TKey : notnull where TTie : notnull
{
    public static Task<CursorPage<T>> PageAsync(IFindFluent<T, T> find, Expression<Func<T, TKey>> key, Expression<Func<T, TTie>>? tieBreaker,
        CursorRequest request, bool descending, string? cursorName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(request);
        Pagination.Validate(1, request.PerPage);
        if (find.Options.Sort is not null)
            throw new InvalidOperationException("Cursor pagination applies its own sort; remove SortBy before calling ToCursorPageAsync.");
        Validate<TKey>();
        if (tieBreaker is not null) Validate<TTie>();
        return FetchAsync(find, key, tieBreaker, request, descending, cursorName ?? request.CursorName, ct);
    }

    private static async Task<CursorPage<T>> FetchAsync(IFindFluent<T, T> find, Expression<Func<T, TKey>> key, Expression<Func<T, TTie>>? tieBreaker,
        CursorRequest request, bool descending, string cursorName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var after = Decode(request.Cursor, tieBreaker is not null);
        if (after is null) request.LogInvalidCursor();
        var before = after?.Direction == CursorDirection.Before;
        var (filter, sort) = Build(key, tieBreaker, after, descending);
        if (filter is not null) find.Filter &= filter;
        var rows = await find.Sort(sort).Limit(request.PerPage + 1).ToListAsync(ct).ConfigureAwait(false);
        return Pagination.CreateCursorPage(rows, request.PerPage, before, after is not null, after is null ? null : request.Cursor, cursorName,
            new Encoder(KeyReader.For(key), tieBreaker is null ? null : KeyReader.For(tieBreaker)));
    }

    /// <summary>The keyset condition (null on the first page) and the sort: reversed for a <see cref="CursorDirection.Before"/> cursor.</summary>
    internal static (FilterDefinition<T>? Filter, SortDefinition<T> Sort) Build(Expression<Func<T, TKey>> key, Expression<Func<T, TTie>>? tieBreaker,
        Position? position, bool descending)
    {
        var filters = Builders<T>.Filter;
        var sorts = Builders<T>.Sort;
        var down = descending ^ position?.Direction == CursorDirection.Before;
        var keyField = new ExpressionFieldDefinition<T, TKey>(key);
        var sort = down ? sorts.Descending(new ExpressionFieldDefinition<T>(key)) : sorts.Ascending(new ExpressionFieldDefinition<T>(key));
        if (tieBreaker is not null)
        {
            sort = down ? sort.Descending(new ExpressionFieldDefinition<T>(tieBreaker)) : sort.Ascending(new ExpressionFieldDefinition<T>(tieBreaker));
        }

        if (position is not { } at)
        {
            return (null, sort);
        }

        var past = down ? filters.Lt(keyField, at.Key) : filters.Gt(keyField, at.Key);
        if (tieBreaker is null)
        {
            return (past, sort);
        }

        var tieField = new ExpressionFieldDefinition<T, TTie>(tieBreaker);
        var tied = filters.And(filters.Eq(keyField, at.Key), down ? filters.Lt(tieField, at.Tie!) : filters.Gt(tieField, at.Tie!));
        return (filters.Or(past, tied), sort);
    }

    internal readonly record struct Position(CursorDirection Direction, TKey Key, TTie? Tie);

    private static Position? Decode(string? cursor, bool composite)
    {
        try
        {
            if (!composite)
            {
                return Codec<TKey>.IsObjectId
                    ? CursorCodec.Decode<string>(cursor) is { } id ? new(id.Direction, Codec<TKey>.Parse(id.Value), default) : null
                    : CursorCodec.Decode<TKey>(cursor) is { } value ? new(value.Direction, value.Value, default) : null;
            }

            return (Codec<TKey>.IsObjectId, Codec<TTie>.IsObjectId) switch
            {
                (false, false) => CursorCodec.Decode<TKey, TTie>(cursor) is { } c ? new(c.Cursor.Direction, c.Cursor.Value, c.TieBreaker) : null,
                (true, false) => CursorCodec.Decode<string, TTie>(cursor) is { } c ? new(c.Cursor.Direction, Codec<TKey>.Parse(c.Cursor.Value), c.TieBreaker) : null,
                (false, true) => CursorCodec.Decode<TKey, string>(cursor) is { } c ? new(c.Cursor.Direction, c.Cursor.Value, Codec<TTie>.Parse(c.TieBreaker)) : null,
                (true, true) => CursorCodec.Decode<string, string>(cursor) is { } c ? new(c.Cursor.Direction, Codec<TKey>.Parse(c.Cursor.Value), Codec<TTie>.Parse(c.TieBreaker)) : null,
            };
        }
        catch (FormatException)
        {
            return null; // a cursor whose ObjectId does not parse: invalid, like any tampered cursor
        }
    }

    private static void Validate<TValue>()
    {
        if (Nullable.GetUnderlyingType(typeof(TValue)) is not null)
            throw new NotSupportedException("Keyset paging needs non-null keys: sort by a non-null field and add a unique tie-breaker.");
        if (!Codec<TValue>.IsObjectId) CursorCodec.ValidateType<TValue>();
    }

    private readonly struct Encoder(Func<T, TKey> key, Func<T, TTie>? tieBreaker) : ICursorEncoder<T>
    {
        public string Encode(T row, CursorDirection direction)
        {
            var k = key(row);
            if (tieBreaker is null)
            {
                return Codec<TKey>.IsObjectId ? CursorCodec.Encode(new Cursor<string>(direction, k.ToString()!)) : CursorCodec.Encode(new Cursor<TKey>(direction, k));
            }

            var t = tieBreaker(row);
            return (Codec<TKey>.IsObjectId, Codec<TTie>.IsObjectId) switch
            {
                (false, false) => CursorCodec.Encode(new Cursor<TKey>(direction, k), t),
                (true, false) => CursorCodec.Encode(new Cursor<string>(direction, k.ToString()!), t),
                (false, true) => CursorCodec.Encode(new Cursor<TKey>(direction, k), t.ToString()!),
                (true, true) => CursorCodec.Encode(new Cursor<string>(direction, k.ToString()!), t.ToString()!),
            };
        }
    }
}

internal static class Codec<TValue>
{
    public static readonly bool IsObjectId = typeof(TValue) == typeof(ObjectId);

    public static TValue Parse(string value) => ObjectId.Parse(value) is TValue id ? id : throw new InvalidOperationException($"{typeof(TValue)} is not ObjectId.");
}
