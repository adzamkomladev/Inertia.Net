using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Inertia.Net.Tests;

public class InertiaRequestTests
{
    private static InertiaRequest Parse(string name, StringValues value)
    {
        var headers = new HeaderDictionary { [name] = value };
        return new InertiaRequest(headers);
    }

    [Fact]
    public void Absent_headers_are_null_and_false()
    {
        var request = new InertiaRequest(new HeaderDictionary());
        Assert.False(request.IsInertia);
        Assert.Null(request.Version);
        Assert.Null(request.PartialComponent);
        Assert.Null(request.Only);
        Assert.Null(request.Except);
        Assert.Null(request.Reset);
        Assert.Null(request.ErrorBag);
        Assert.Null(request.ExceptOnceProps);
        Assert.False(request.MergeIntentPrepend);
        Assert.False(request.IsPrefetch);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(",", null)]
    [InlineData(" , ,", null)]
    [InlineData("a", new[] { "a" })]
    [InlineData("a,b", new[] { "a", "b" })]
    [InlineData("a,", new[] { "a" })]
    [InlineData(" a , b.c ,, ", new[] { "a", "b.c" })]
    public void List_headers_are_split_trimmed_and_empty_means_null(string header, string[]? expected)
    {
        Assert.Equal(expected, Parse(InertiaHeaders.PartialData, header).Only);
        Assert.Equal(expected, Parse(InertiaHeaders.PartialExcept, header).Except);
        Assert.Equal(expected, Parse(InertiaHeaders.Reset, header).Reset);
        Assert.Equal(expected, Parse(InertiaHeaders.ExceptOnceProps, header).ExceptOnceProps);
    }

    [Fact]
    public void Repeated_header_lines_are_combined()
    {
        Assert.Equal(new[] { "a", "b", "c" }, Parse(InertiaHeaders.PartialData, new StringValues(["a", "b,c"])).Only);
    }

    [Fact]
    public void Scalar_headers_are_read()
    {
        var headers = new HeaderDictionary
        {
            [InertiaHeaders.Inertia] = "true",
            [InertiaHeaders.Version] = "abc",
            [InertiaHeaders.PartialComponent] = "Users/Index",
            [InertiaHeaders.ErrorBag] = "login",
            [InertiaHeaders.InfiniteScrollMergeIntent] = "prepend",
            [InertiaHeaders.Purpose] = "Prefetch",
        };

        var request = new InertiaRequest(headers);
        Assert.True(request.IsInertia);
        Assert.Equal("abc", request.Version);
        Assert.Equal("Users/Index", request.PartialComponent);
        Assert.Equal("login", request.ErrorBag);
        Assert.True(request.MergeIntentPrepend);
        Assert.True(request.IsPrefetch);
    }

    [Theory]
    [InlineData("append", false)]
    [InlineData("down", false)]
    [InlineData("prepend", true)]
    public void Only_prepend_is_a_prepend_intent(string header, bool prepend) =>
        Assert.Equal(prepend, Parse(InertiaHeaders.InfiniteScrollMergeIntent, header).MergeIntentPrepend);

    [Fact]
    public void Empty_scalar_headers_are_null()
    {
        Assert.Null(Parse(InertiaHeaders.Version, "").Version);
        Assert.False(Parse(InertiaHeaders.Inertia, "").IsInertia);
    }

    [Fact]
    public async Task Partial_headers_for_another_component_mean_a_full_render()
    {
        var harness = new Harness();
        var page = await harness.PageAsync(
            new InertiaProps { ["a"] = 1, ["b"] = Inertia.Defer(() => 2) },
            c => c.AsPartial("b", component: "Other"));

        JsonAssert.Equal("""{"errors":{},"a":1}""", page["props"]);
        JsonAssert.Equal("""{"default":["b"]}""", page["deferredProps"]);
    }

    [Fact]
    public async Task Partial_reload_does_not_require_the_inertia_header()
    {
        var harness = new Harness();
        var html = await harness.PageAsync(
            new InertiaProps { ["a"] = 1, ["b"] = 2 },
            c => c.WithHeader(InertiaHeaders.PartialComponent, "TestComponent").WithHeader(InertiaHeaders.PartialData, "b"));

        JsonAssert.Equal("""{"errors":{},"b":2}""", html["props"]);
    }
}
