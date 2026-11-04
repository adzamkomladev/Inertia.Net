using System.Text.Json;

namespace Inertia.Net.Tests;

public class PageTests
{
    [Fact]
    public void Page_computes_ranges_and_metadata()
    {
        var full = new Page<int>([1, 2], 2, 2, 5, true);
        Assert.Equal(3, full.From);
        Assert.Equal(4, full.To);
        Assert.Equal(3, full.LastPage);
        Assert.Equal(ScrollMetadata.FromPage(2, true), full.GetScrollMetadata());
        var partial = new Page<int>([5], 3, 2, 5, false);
        Assert.Equal(5, partial.From);
        Assert.Equal(5, partial.To);
        var empty = new Page<int>([], 4, 2, null, false);
        Assert.Null(empty.From);
        Assert.Null(empty.To);
        Assert.Null(empty.LastPage);
    }

    [Fact]
    public void Cursor_metadata_preserves_values()
    {
        var page = new CursorPage<int>([1], 2, "next+/=", "previous", true, "c", "current");
        Assert.Equal(ScrollMetadata.FromCursor("current", "previous", "next+/=", "c"), page.GetScrollMetadata());
    }

    [Fact]
    public void Page_serializes_expected_shape()
    {
        var page = new Page<int>([3, 4], 2, 2, 5, true);
        Assert.Equal("{\"data\":[3,4],\"currentPage\":2,\"perPage\":2,\"total\":5,\"lastPage\":3,\"from\":3,\"to\":4,\"hasMore\":true,\"pageName\":\"page\"}", JsonSerializer.Serialize(page, JsonSerializerOptions.Web));
    }

    [Fact]
    public void Page_rejects_non_data_scroll_wrapper() =>
        Assert.Throws<ArgumentException>(() => Inertia.Scroll(new Page<int>([], 1, 20, 0, false), "items"));
    [Fact]
    public void First_cursor_page_uses_numeric_one_for_current_page()
    {
        var page = new CursorPage<int>([1], 1, "next", null, true);
        Assert.Equal(new ScrollMetadata("cursor", null, "next", 1), page.GetScrollMetadata());
    }

    [Fact]
    public async Task Render_preserves_the_exact_numbered_page_contract()
    {
        var harness = new Harness();
        var context = harness.Context().AsInertia();
        var json = await Harness.ExecuteAsync(context, Inertia.Render("Posts", new InertiaProps
        {
            ["posts"] = new Page<int>([3, 4], 2, 2, 5, true),
        }));
        Assert.Equal("""{"component":"Posts","props":{"errors":{},"posts":{"data":[3,4],"currentPage":2,"perPage":2,"total":5,"lastPage":3,"from":3,"to":4,"hasMore":true,"pageName":"page"}},"url":"/","version":"","sharedProps":["errors"]}""", json);
    }

    [Fact]
    public void Unknown_total_and_cursor_page_have_the_documented_json_shape()
    {
        Assert.Equal("""{"data":[3],"currentPage":2,"perPage":2,"total":null,"lastPage":null,"from":3,"to":3,"hasMore":false,"pageName":"page"}""",
            JsonSerializer.Serialize(new Page<int>([3], 2, 2, null, false), JsonSerializerOptions.Web));
        Assert.Equal("""{"data":[3],"perPage":2,"nextCursor":null,"previousCursor":"before","hasMore":false,"cursorName":"cursor"}""",
            JsonSerializer.Serialize(new CursorPage<int>([3], 2, null, "before", false, currentCursor: "current"), JsonSerializerOptions.Web));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    public void Last_page_rounds_up_and_is_at_least_one(long total, long expected) =>
        Assert.Equal(expected, new Page<int>([], 1, 20, total, false).LastPage);

    [Fact]
    public void Lazy_pages_reject_custom_scroll_wrappers_at_the_call_site()
    {
        Assert.Throws<ArgumentException>(() => Inertia.Scroll(() => new Page<int>([], 1, 20, 0, false), "items"));
        Assert.Throws<ArgumentException>(() => Inertia.Scroll(() => Task.FromResult(new CursorPage<int>([], 20, null, null, false)), "items"));
    }
    [Fact]
    public void Constructors_reject_invalid_page_values()
    {
        Assert.Throws<ArgumentNullException>(() => new Page<int>(null!, 1, 20, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Page<int>([], 0, 20, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Page<int>([], 1, 0, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Page<int>([], 1, 20, -1, false));
        Assert.Throws<ArgumentNullException>(() => new CursorPage<int>(null!, 20, null, null, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CursorPage<int>([], 0, null, null, false));
    }
    [Fact]
    public void Last_representable_page_cannot_link_to_an_invalid_page_number() =>
        Assert.Null(new Page<int>([1], int.MaxValue, 1, null, true).GetScrollMetadata().NextPage);

}
