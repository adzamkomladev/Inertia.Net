#if NET11_0_OR_GREATER
namespace Inertia.Net.Tests;

/// <summary>C# 15 union props: written as the active case's JSON (web defaults, source-generated metadata).</summary>
public sealed class UnionPropsTests
{
    private readonly Harness _harness = new();

    private static UnionSubjectPage Page() => new(new User("Ann"), 5, "Union");

    [Fact]
    public async Task A_first_visit_writes_each_active_case()
    {
        var props = (await _harness.PageAsync(Page()))["props"]!;

        Assert.Equal("Ann", (string?)props["subject"]!["name"]);
        Assert.Equal(5, (int)props["score"]!);
        Assert.Equal("Union", (string?)props["title"]);
    }

    [Fact]
    public async Task A_partial_reload_writes_only_the_requested_union_prop()
    {
        var props = (await _harness.PageAsync(Page(), c => c.AsPartial(only: "score")))["props"]!.AsObject();

        Assert.Equal(5, (int)props["score"]!);
        Assert.False(props.ContainsKey("subject"));
        Assert.False(props.ContainsKey("title"));
    }
}
#endif
