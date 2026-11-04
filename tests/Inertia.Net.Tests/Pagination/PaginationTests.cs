namespace Inertia.Net.Tests;

public sealed class PaginationTests
{
    private static readonly int[] Items = Enumerable.Range(1, 25).ToArray();

    [Theory]
    [InlineData(1, 1, 10, true)]
    [InlineData(2, 11, 10, true)]
    [InlineData(3, 21, 5, false)]
    [InlineData(4, 0, 0, false)]
    public void InMemoryPages(int page, int first, int count, bool hasMore)
    {
        var result = Pagination.Paginate(Items, new PageRequest(page, 10));
        Assert.Equal(count, result.Data.Count);
        if (count > 0)
        {
            Assert.Equal(first, result.Data[0]);
        }

        Assert.Equal(25, result.Total);
        Assert.Equal(hasMore, result.HasMore);
    }

    [Fact]
    public async Task OffsetDelegatesReceiveOverfetchAndCancellation()
    {
        using var cts = new CancellationTokenSource();
        var countCalls = 0;
        var result = await Pagination.PaginateAsync<int>(new PageRequest(2, 10), (skip, take, ct) =>
        {
            Assert.Equal(10, skip);
            Assert.Equal(11, take);
            Assert.Equal(cts.Token, ct);
            return Task.FromResult<IReadOnlyList<int>>(Items.Skip(skip).Take(take).ToArray());
        }, ct =>
        {
            Assert.Equal(cts.Token, ct);
            countCalls++;
            return Task.FromResult(25L);
        }, ct: cts.Token);
        Assert.Equal(1, countCalls);
        Assert.Equal(10, result.Data.Count);
        Assert.True(result.HasMore);
        Assert.Equal(25, result.Total);

        var noCount = await Pagination.PaginateAsync<int>(new PageRequest(3, 10), (skip, take, _) =>
            Task.FromResult<IReadOnlyList<int>>(Items.Skip(skip).Take(take).ToArray()), ct: TestContext.Current.CancellationToken);
        Assert.Null(noCount.Total);
        Assert.False(noCount.HasMore);
    }

    [Fact]
    public async Task Parsed_parameter_names_flow_to_metadata_and_explicit_names_win()
    {
        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery("?p=2&size=3"));
        var request = PageRequest.From(query, pageName: "p", perPageName: "size");
        Assert.Equal("p", Pagination.Paginate(Items, request).PageName);
        Assert.Equal("override", Pagination.Paginate(Items, request, "override").PageName);
        var page = await Pagination.PaginateAsync<int>(request,
            (_, _, _) => Task.FromResult<IReadOnlyList<int>>([]), ct: TestContext.Current.CancellationToken);
        Assert.Equal("p", page.PageName);
        var overridden = await Pagination.PaginateAsync<int>(request,
            (_, _, _) => Task.FromResult<IReadOnlyList<int>>([]), pageName: "override", ct: TestContext.Current.CancellationToken);
        Assert.Equal("override", overridden.PageName);
        var cursorRequest = CursorRequest.From(query, cursorName: "c");
        var cursor = await Pagination.PaginateAsync<int, int>(cursorRequest, i => i,
            (_, _, _) => Task.FromResult<IReadOnlyList<int>>([]), ct: TestContext.Current.CancellationToken);
        Assert.Equal("c", cursor.CursorName);
        var cursorOverride = await Pagination.PaginateAsync<int, int>(cursorRequest, i => i,
            (_, _, _) => Task.FromResult<IReadOnlyList<int>>([]), cursorName: "override", ct: TestContext.Current.CancellationToken);
        Assert.Equal("override", cursorOverride.CursorName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delegate_offsets_beyond_Int32_return_empty_without_fetching(bool countTotal)
    {
        var fetchCalls = 0;
        var countCalls = 0;
        var page = await Pagination.PaginateAsync<int>(new PageRequest(int.MaxValue, 20),
            (_, _, _) =>
            {
                fetchCalls++;
                return Task.FromResult<IReadOnlyList<int>>([]);
            }, countTotal ? ct =>
            {
                Assert.Equal(TestContext.Current.CancellationToken, ct);
                countCalls++;
                return Task.FromResult(25L);
            } : null, ct: TestContext.Current.CancellationToken);
        Assert.Empty(page.Data);
        Assert.False(page.HasMore);
        Assert.Equal(countTotal ? 25L : (long?)null, page.Total);
        Assert.Null(page.From);
        Assert.Null(page.To);
        Assert.Equal(0, fetchCalls);
        Assert.Equal(countTotal ? 1 : 0, countCalls);
    }

    [Fact]
    public async Task EmptyAndMalformedCursorRequestsStartAtTheFirstPage()
    {
        foreach (var token in new string?[] { null, "%%%", "WyJhIiwid3JvbmctdHlwZSJd" })
        {
            var result = await Pagination.PaginateAsync<int, int>(new CursorRequest(token, 10), i => i,
                (cursor, take, _) =>
                {
                    Assert.Null(cursor);
                    Assert.Equal(11, take);
                    return Task.FromResult<IReadOnlyList<int>>([]);
                }, cursorName: "position", ct: TestContext.Current.CancellationToken);
            Assert.Empty(result.Data);
            Assert.Null(result.NextCursor);
            Assert.Null(result.PreviousCursor);
            Assert.False(result.HasMore);
            Assert.Equal("position", result.CursorName);
            Assert.Equal(1, result.GetScrollMetadata().CurrentPage);
        }
    }

    [Fact]
    public async Task CursorOverfetchIsTrimmedBeforeComputingBoundaries()
    {
        var result = await Pagination.PaginateAsync<int, int>(new CursorRequest(PerPage: 3), i => i,
            (_, _, _) => Task.FromResult<IReadOnlyList<int>>([1, 2, 3, 4]), ct: TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 3], result.Data);
        Assert.Equal(new Cursor<int>(CursorDirection.After, 3), CursorCodec.Decode<int>(result.NextCursor));
        Assert.True(result.HasMore);
    }

    [Fact]
    public void UnsupportedCursorKeysFailBeforeCallingTheFetchDelegate()
    {
        var called = false;
        Assert.Throws<NotSupportedException>(() =>
        {
            _ = Pagination.PaginateAsync<int, DateOnly>(new CursorRequest(),
            _ => default, (_, _, _) =>
            {
                called = true;
                return Task.FromResult<IReadOnlyList<int>>([]);
            }, ct: TestContext.Current.CancellationToken);
        });
        Assert.False(called);
    }

    [Fact]
    public async Task AlreadyCanceledRequestsDoNotCallDelegates()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pagination.PaginateAsync<int>(new PageRequest(),
            (_, _, _) =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<int>>([]);
            }, ct: source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pagination.PaginateAsync<int, int>(new CursorRequest(), i => i,
            (_, _, _) =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<int>>([]);
            }, ct: source.Token));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void InMemoryOffsetCannotOverflowAndPreservesCustomPageName()
    {
        var result = Pagination.Paginate(Items, new PageRequest(int.MaxValue, 10), "p");
        Assert.Empty(result.Data);
        Assert.False(result.HasMore);
        Assert.Equal("p", result.PageName);
    }

    [Fact]
    public async Task CursorPagesNavigateBothDirectionsInDisplayOrder()
    {
        using var cts = new CancellationTokenSource();
        Task<IReadOnlyList<int>> Fetch(Cursor<int>? cursor, int take, CancellationToken ct)
        {
            Assert.Equal(cts.Token, ct);
            IEnumerable<int> rows = Items;
            if (cursor is { Direction: CursorDirection.After } after)
            {
                rows = rows.Where(i => i > after.Value);
            }
            else if (cursor is { Direction: CursorDirection.Before } before)
            {
                rows = rows.Where(i => i < before.Value).Reverse();
            }

            return Task.FromResult<IReadOnlyList<int>>(rows.Take(take).ToArray());
        }

        async Task<CursorPage<int>> Get(string? cursor) => await Pagination.PaginateAsync<int, int>(
            new CursorRequest(cursor, 10), i => i, Fetch, ct: cts.Token);

        var first = await Get(null);
        Assert.Equal(Items.Take(10), first.Data);
        Assert.Null(first.PreviousCursor);
        Assert.NotNull(first.NextCursor);
        var second = await Get(first.NextCursor);
        Assert.Equal(Items.Skip(10).Take(10), second.Data);
        var third = await Get(second.NextCursor);
        Assert.Equal(Items.Skip(20), third.Data);
        Assert.Null(third.NextCursor);
        var back = await Get(third.PreviousCursor);
        Assert.Equal(second.Data, back.Data);
        Assert.NotNull(back.PreviousCursor);
        var start = await Get(second.PreviousCursor);
        Assert.Equal(first.Data, start.Data);
        Assert.Null(start.PreviousCursor);
        Assert.NotNull(start.NextCursor);

        var shortBefore = await Get(CursorCodec.Encode(new Cursor<int>(CursorDirection.Before, 6)));
        Assert.Equal(Items.Take(5), shortBefore.Data);
        Assert.Null(shortBefore.PreviousCursor);
        Assert.NotNull(shortBefore.NextCursor);
    }
}
