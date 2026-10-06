using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Inertia.Net.Tests;

public class MemberSerializationTests
{
    private static Harness CreateHarness(bool generated) => new(configureServices: services =>
        services.PostConfigure<HttpJsonOptions>(o =>
        {
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            o.SerializerOptions.TypeInfoResolver = generated ? MemberJsonContext.Default : new DefaultJsonTypeInfoResolver();
        }));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Typed_members_preserve_converters_extension_data_and_explicit_nulls(bool generated, bool nested)
    {
        var harness = CreateHarness(generated);
        var model = new MemberPage();
        object props = nested ? new InertiaProps { ["nested"] = model } : model;
        foreach (var inertia in new[] { false, true })
        {
            var page = await harness.PageAsync(props, c => { if (inertia) c.AsInertia(); });
            var actual = nested ? page["props"]!["nested"] : page["props"];
            if (!nested) actual!.AsObject().Remove("errors");
            JsonAssert.Equal("""{"token":"masked </script>","number":"42","keepNull":null,"dynamic":42}""", actual);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partial_reloads_filter_extension_fields_and_do_not_run_excluded_converters(bool generated)
    {
        var harness = CreateHarness(generated);
        var model = new MemberPage { Token = "throw-if-converted" };
        foreach (var nested in new[] { false, true })
        {
            var prefix = nested ? "nested." : "";
            var page = await harness.PageAsync(nested ? new InertiaProps { ["nested"] = model } : model,
                c => c.AsPartial(prefix + "keepNull," + prefix + "dynamic"));
            var actual = nested ? page["props"]!["nested"] : page["props"];
            if (!nested) actual!.AsObject().Remove("errors");
            JsonAssert.Equal("""{"keepNull":null,"dynamic":42}""", actual);
        }
    }

    [Theory]
    [InlineData(false, "object")]
    [InlineData(true, "object")]
    [InlineData(false, "element")]
    [InlineData(true, "element")]
    [InlineData(false, "node")]
    [InlineData(true, "node")]
    public async Task Extension_data_shapes_preserve_literal_keys_and_null_values(bool generated, string shape)
    {
        var harness = CreateHarness(generated);
        // A dictionary policy must not transform JsonExtensionData keys.
        var options = harness.Services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        object model = shape switch
        {
            "object" => new ExtensionPage<Dictionary<string, object?>> { Extra = new() { ["DynamicValue"] = 42, ["ExplicitNull"] = null } },
            "element" => new ExtensionPage<Dictionary<string, JsonElement>> { Extra = new() { ["DynamicValue"] = JsonSerializer.SerializeToElement(42), ["ExplicitNull"] = JsonSerializer.SerializeToElement<object?>(null) } },
            _ => new ExtensionPage<JsonObject> { Extra = new() { ["DynamicValue"] = 42, ["ExplicitNull"] = null } },
        };
        var page = await harness.PageAsync(model);
        JsonAssert.Equal("""{"errors":{},"DynamicValue":42,"ExplicitNull":null}""", page["props"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extension_data_preserves_inertia_loading_behavior(bool generated)
    {
        var harness = CreateHarness(generated);
        var model = new ExtensionPage<Dictionary<string, object?>>
        {
            Extra = new() { ["dynamic"] = Inertia.Defer(() => 42), ["keep"] = 1 },
        };
        var initial = await harness.PageAsync(model);
        JsonAssert.Equal("""{"errors":{},"keep":1}""", initial["props"]);
        var partial = await harness.PageAsync(model, c => c.AsPartial("dynamic"));
        JsonAssert.Equal("""{"errors":{},"dynamic":42}""", partial["props"]);
    }
}

public sealed class MemberPage
{
    [JsonConverter(typeof(MemberMaskConverter))]
    public string Token { get; init; } = "private-value";

    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public int Number { get; init; } = 42;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? KeepNull { get; init; }

    public string? OmitNull { get; init; }

    public InertiaProp Lazy { get; init; } = Inertia.Defer(() => 1);

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new() { ["dynamic"] = JsonSerializer.SerializeToElement(42) };
}

public sealed class MemberMaskConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()!;

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (value == "throw-if-converted") throw new InvalidOperationException("Excluded converter ran.");
        writer.WriteStringValue("masked </script>");
    }
}

public sealed class ExtensionPage<T>
{
    public InertiaProp Lazy { get; init; } = Inertia.Defer(() => 1);

    [JsonExtensionData]
    public T? Extra { get; set; }
}

[JsonSerializable(typeof(MemberPage))]
[JsonSerializable(typeof(ExtensionPage<Dictionary<string, object?>>))]
[JsonSerializable(typeof(ExtensionPage<Dictionary<string, JsonElement>>))]
[JsonSerializable(typeof(ExtensionPage<JsonObject>))]
internal sealed partial class MemberJsonContext : JsonSerializerContext;
