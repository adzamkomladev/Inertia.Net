using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Inertia.Net.Tests;

public class RequestTests
{
    [Theory]
    [InlineData("?page=-1&perPage=0", 1, 20)]
    [InlineData("?page=abc&perPage=999", 1, 100)]
    [InlineData("?page=3&perPage=5", 3, 5)]
    public void Parses_and_clamps(string input, int page, int perPage)
    {
        var query = new QueryCollection(QueryHelpers.ParseQuery(input));
        Assert.Equal(new PageRequest(page, perPage), PageRequest.From(query));
    }

    [Fact]
    public void Supports_custom_parameter_names()
    {
        var query = new QueryCollection(QueryHelpers.ParseQuery("?p=2&n=4&c=opaque"));
        Assert.Equal(new PageRequest(2, 4) { PageName = "p" }, PageRequest.From(query, pageName: "p", perPageName: "n"));
        Assert.Equal(new CursorRequest("opaque", 4) { CursorName = "c" }, CursorRequest.From(query, cursorName: "c", perPageName: "n"));
    }
    [Theory]
    [InlineData("", 1, 20)]
    [InlineData("?page=0&perPage=-2", 1, 20)]
    [InlineData("?page=999999999999999&perPage=999999999999999", 1, 20)]
    [InlineData("?page=&perPage=abc", 1, 20)]
    [InlineData("?page=2&page=3&perPage=4&perPage=5", 1, 20)]
    public void Missing_and_invalid_values_use_defaults(string input, int page, int perPage)
    {
        var query = new QueryCollection(QueryHelpers.ParseQuery(input));
        Assert.Equal(new PageRequest(page, perPage), PageRequest.From(query));
        Assert.Equal(new CursorRequest(null, perPage), CursorRequest.From(query));
    }

    [Fact]
    public async Task Binders_read_configured_defaults_names_and_limits()
    {
        var harness = new Harness(o =>
        {
            o.Pagination.DefaultPerPage = 7;
            o.Pagination.MaxPerPage = 12;
            o.Pagination.PageName = "p";
            o.Pagination.PerPageName = "size";
            o.Pagination.CursorName = "c";
        });
        var defaults = harness.Context("/posts");
        Assert.Equal(new PageRequest(1, 7) { PageName = "p" }, await PageRequest.BindAsync(defaults));
        Assert.Equal(new CursorRequest(null, 7) { CursorName = "c" }, await CursorRequest.BindAsync(defaults));
        var custom = harness.Context("/posts?p=3&size=999&c=opaque");
        Assert.Equal(new PageRequest(3, 12) { PageName = "p" }, await PageRequest.BindAsync(custom));
        Assert.Equal(new CursorRequest("opaque", 12) { CursorName = "c" }, await CursorRequest.BindAsync(custom));
    }

    [Theory]
    [InlineData("?perPage=2147483647", 2147483646)]
    [InlineData("?perPage=2147483648", 20)]
    [InlineData("?perPage=2147483647&perPage=1", 20)]
    [InlineData("?perPage=-2147483648", 20)]
    public void Page_sizes_leave_room_for_overfetch(string input, int expected)
    {
        var query = new QueryCollection(QueryHelpers.ParseQuery(input));
        Assert.Equal(expected, PageRequest.From(query, maxPerPage: int.MaxValue).PerPage);
        Assert.Equal(expected, CursorRequest.From(query, maxPerPage: int.MaxValue).PerPage);
    }

    [Fact]
    public void Maximum_default_page_size_leaves_room_for_overfetch()
    {
        var query = new QueryCollection();
        Assert.Equal(int.MaxValue - 1, PageRequest.From(query, int.MaxValue, int.MaxValue).PerPage);
        Assert.Equal(int.MaxValue - 1, CursorRequest.From(query, int.MaxValue, int.MaxValue).PerPage);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(20, 10)]
    [InlineData(20, int.MaxValue)]
    public void Options_validator_rejects_invalid_pagination_sizes(int defaultPerPage, int maxPerPage)
    {
        var options = new InertiaOptions();
        options.Pagination.DefaultPerPage = defaultPerPage;
        options.Pagination.MaxPerPage = maxPerPage;
        var result = new InertiaOptionsValidator().Validate(Options.DefaultName, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Pagination", StringComparison.Ordinal));
    }
}
