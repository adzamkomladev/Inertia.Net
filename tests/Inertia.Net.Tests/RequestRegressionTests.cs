using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace Inertia.Net.Tests;

public class RequestRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validation_schema_preserves_named_members_and_dictionary_keys(bool generated)
    {
        var naming = new PrefixNamingPolicy();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = naming,
            TypeInfoResolver = generated ? ValidationJsonContext.Default : new DefaultJsonTypeInfoResolver(),
        };
        var result = InertiaValidationFilter.ToClientKeys(new()
        {
            ["p_Items[0].p_Email"] = ["Invalid"],
            ["p_Fields[KeepThis].Email"] = ["Invalid"],
            ["p_Fields.KeepThis.Email"] = ["Invalid"],
            ["Fields.OtherKey.Email"] = ["Invalid"],
        }, naming, options.GetTypeInfo(typeof(ValidationInput)));
        Assert.Contains("p_Items.0.p_Email", result.Keys);
        Assert.Contains("p_Fields[KeepThis].p_Email", result.Keys);
        Assert.Contains("p_Fields.KeepThis.p_Email", result.Keys);
        Assert.Contains("p_Fields.OtherKey.p_Email", result.Keys);
    }
    private sealed class PrefixNamingPolicy : JsonNamingPolicy { public override string ConvertName(string name) => "p_" + name; }
    [Theory]
    [InlineData("Items[0].Email", "items.0.email")]
    [InlineData("Items[12].Children[3].Email", "items.12.children.3.email")]
    [InlineData("Fields[KeepThis].Email", "fields[KeepThis].email")]
    [InlineData("Fields['Keep.This'].Email", "fields['Keep.This'].email")]
    public void Validation_paths_normalize_indices_without_renaming_bracketed_keys(string input, string expected) =>
        Assert.Equal(expected, InertiaValidationFilter.ToClientPath(input, JsonNamingPolicy.CamelCase));

    [Theory]
    [InlineData("items.0.email")]
    [InlineData("items[0].email")]
    public async Task Collection_precognition_does_not_report_false_success(string only)
    {
        var harness = new Harness();
        var context = harness.Context().WithHeader(InertiaPrecognition.ValidateOnlyHeader, only);
        await InertiaPrecognition.Result(context, new() { ["Items[0].Email"] = ["Invalid"] }, JsonNamingPolicy.CamelCase).ExecuteAsync(context);
        await context.Response.BodyWriter.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        Assert.Contains("items.0.email", Harness.Body(context), StringComparison.Ordinal);
        var merged = InertiaValidationFilter.ToClientKeys(new() { ["Items[0].Email"] = ["first"], ["Items.0.Email"] = ["second"] }, JsonNamingPolicy.CamelCase);
        Assert.Equal(["first", "second"], merged["items.0.email"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task State_created_by_a_loader_is_emitted_without_repeating_the_loader(bool existing)
    {
        var harness = new Harness();
        var context = harness.Context().AsInertia();
        if (existing) context.Inertia();
        var calls = 0;
        var page = Harness.ParsePage(await Harness.ExecuteAsync(context, Inertia.Render("Test", new InertiaProps
        {
            ["value"] = Inertia.Prop(() => { calls++; context.Inertia().Flash("notice", "hello").ClearHistory().PreserveFragment(); return 1; }),
        })));
        Assert.Equal("hello", page["flash"]!["notice"]!.GetValue<string>());
        Assert.True(page["clearHistory"]!.GetValue<bool>());
        Assert.True(page["preserveFragment"]!.GetValue<bool>());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Request_shares_created_by_a_global_factory_are_included()
    {
        var harness = new Harness(o => o.Share("global", c =>
        {
            c.Inertia().Share("request", 2).Flash("notice", "factory").ClearHistory()
                .WithErrors(new Dictionary<string, string> { ["name"] = "Invalid" });
            return 1;
        }));
        var page = await harness.PageAsync(null);
        Assert.Equal(2, page["props"]!["request"]!.GetValue<int>());
        Assert.Equal("factory", page["flash"]!["notice"]!.GetValue<string>());
        Assert.True(page["clearHistory"]!.GetValue<bool>());
        Assert.Equal("Invalid", page["props"]!["errors"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Many_shared_props_retain_request_and_page_precedence()
    {
        var harness = new Harness(o => { for (var i = 0; i < 32; i++) o.Share($"key{i}", i); });
        var props = new InertiaProps();
        for (var i = 0; i < 200; i++) props[$"page{i}"] = i;
        props["key0"] = "page";
        var page = await harness.PageAsync(props, c => c.Inertia().Share("key0", "request").Share("key1", "request"));
        Assert.Equal("page", page["props"]!["key0"]!.GetValue<string>());
        Assert.Equal("request", page["props"]!["key1"]!.GetValue<string>());
        Assert.Equal(31, page["props"]!["key31"]!.GetValue<int>());
        Assert.Equal(199, page["props"]!["page199"]!.GetValue<int>());
    }

    [Fact]
    public async Task Cancellation_stops_subsequent_synchronous_loaders()
    {
        var context = new Harness().Context().AsInertia();
        using var cancellation = new CancellationTokenSource();
        context.RequestAborted = cancellation.Token;
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Inertia.Render("Test", new InertiaProps
        {
            ["first"] = Inertia.Prop(() => { cancellation.Cancel(); return 1; }),
            ["second"] = Inertia.Prop(() => ++calls),
        }).ExecuteAsync(context));
        Assert.Equal(0, calls);
        Assert.Empty(Harness.Body(context));
    }

    [Fact]
    public void Restored_state_preserves_pending_overrides_and_reuses_effective_dictionaries()
    {
        var pending = new InertiaFeature().Flash("same", "pending").WithErrors(new Dictionary<string, string> { ["same"] = "pending" }).ClearHistory();
        pending.RestoreFlash("same", "restored");
        pending.RestoreFlash("old", "restored");
        pending.RestoreError("default", "same", ["restored"]);
        pending.RestoreError("default", "old", ["restored"]);
        pending.RestoreFlags(false, true);
        Assert.Equal("pending", pending.FlashData!["same"]);
        Assert.Equal(["pending"], pending.Errors!["default"]["same"]);
        Assert.Equal("restored", pending.FlashData["old"]);
        Assert.True(pending.HistoryCleared);
        Assert.True(pending.FragmentPreserved);
        Assert.Same(pending.FlashData, pending.FlashData);
        Assert.Same(pending.Errors, pending.Errors);
    }
}

public sealed class ValidationInput
{
    public List<ValidationItem> Items { get; init; } = [];
    public Dictionary<string, ValidationItem> Fields { get; init; } = [];
}
public sealed class ValidationItem { public string? Email { get; init; } }
[JsonSerializable(typeof(ValidationInput))]
internal sealed partial class ValidationJsonContext : JsonSerializerContext;
