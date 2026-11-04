using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inertia.Net.Tests;

public class InertiaTypeScriptTests
{
    [Fact]
    public void Numbered_pages_emit_items_numbers_and_nullable_totals()
    {
        var options = new InertiaOptions();
        options.TypeScript.Page<NumberedPosts>("Posts/Index");
        var output = new TypeScriptGenerator(new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = PaginationJsonContext.Default }, options).Generate();
        Assert.Contains("export interface PagePostDto {", output);
        Assert.Contains("data: PostDto[]", output);
        Assert.Contains("currentPage: number", output);
        Assert.Contains("perPage: number", output);
        Assert.Contains("total: number | null", output);
        Assert.Contains("lastPage: number | null", output);
        Assert.Contains("from: number | null", output);
        Assert.Contains("to: number | null", output);
        Assert.Contains("hasMore: boolean", output);
        Assert.Contains("pageName: string", output);
    }

    [Fact]
    public void Cursor_pages_emit_nullable_cursors_and_omit_the_current_cursor()
    {
        var options = new InertiaOptions();
        options.TypeScript.Page<CursorPosts>("Posts/Cursor");
        var output = new TypeScriptGenerator(new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = PaginationJsonContext.Default }, options).Generate();
        Assert.Contains("export interface CursorPagePostDto {", output);
        Assert.Contains("data: PostDto[]", output);
        Assert.Contains("nextCursor: string | null", output);
        Assert.Contains("previousCursor: string | null", output);
        Assert.Contains("cursorName: string", output);
        Assert.DoesNotContain("currentCursor", output);
    }
}

internal sealed record PostDto(int Id, string Title);
internal sealed record NumberedPosts(Page<PostDto> Posts);
internal sealed record CursorPosts(CursorPage<PostDto> Posts);

[JsonSerializable(typeof(NumberedPosts))]
[JsonSerializable(typeof(CursorPosts))]
internal sealed partial class PaginationJsonContext : JsonSerializerContext;
