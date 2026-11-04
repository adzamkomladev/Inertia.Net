using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Inertia.Net.Tests;

public sealed class SmallAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_preceding_loader_can_invalidate_a_planned_cache_hit(bool byTag)
    {
        var harness = new Harness();
        var loads = 0;
        InertiaProp Cached() => Inertia.Prop(_ => ValueTask.FromResult(++loads)).Parallel()
            .Cache("ordered", TimeSpan.FromMinutes(1), tags: ["ordered-tag"]);
        await harness.PageAsync(new InertiaProps { ["value"] = Cached() });
        var cache = harness.Services.GetRequiredService<IInertiaCache>();
        var page = await harness.PageAsync(new InertiaProps
        {
            ["invalidate"] = Inertia.Prop(async cancellationToken =>
            {
                if (byTag) await cache.RemoveByTagAsync("ordered-tag", cancellationToken);
                else await cache.RemovePropAsync("ordered", cancellationToken);
                return true;
            }),
            ["value"] = Cached(),
        });
        Assert.Equal(2, (int?)page["props"]!["value"]);
        Assert.Equal(2, loads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Converter_instances_of_the_same_type_keep_their_configuration_and_dotted_member_values(bool generated)
    {
        var harness = Create(generated);
        for (var index = 0; index < 2; index++)
        {
            var page = await harness.PageAsync(new ConfiguredConverterPage { Value = $"value{index}" });
            Assert.Equal($"first:value{index}", (string?)page["props"]!["first"]);
            Assert.Equal($"second:value{index}", (string?)page["props"]!["second"]);
            Assert.Equal($"dot:value{index}", (string?)page["props"]!["nested"]!["converted"]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Member_serializers_control_array_filtering_and_dotted_parent_replacement(bool generated)
    {
        var harness = Create(generated);
        var page = await harness.PageAsync(new ConverterShapePage());
        JsonAssert.Equal("""{"child":9}""", page["props"]!["container"]);
        var partial = await harness.PageAsync(new ConverterShapePage(), context => context.AsPartial("converted.0,numbers.0", "converted.1,numbers.1"));
        Assert.Equal("converted array", (string?)partial["props"]!["converted"]);
        JsonAssert.Missing(partial["props"], "numbers");
    }

    private static Harness Create(bool generated) => new(configureServices: services => services.PostConfigure<HttpJsonOptions>(options =>
    {
        IJsonTypeInfoResolver resolver = generated ? SmallAllocationTestContext.Default : new DefaultJsonTypeInfoResolver();
        options.SerializerOptions.TypeInfoResolver = resolver.WithAddedModifier(info =>
        {
            if (info.Type != typeof(ConfiguredConverterPage)) return;
            foreach (var property in info.Properties)
            {
                var prefix = property.Name switch { "first" => "first:", "second" => "second:", "nested.converted" => "dot:", _ => null };
                if (prefix is not null) property.CustomConverter = new ConfiguredStringConverter(prefix);
            }
        });
    }));
}

public sealed class ConfiguredConverterPage
{
    public string Value { get; init; } = "value";
    public string First => Value;
    public string Second => Value;
    [JsonPropertyName("nested.converted")] public string Dotted => Value;
    public InertiaProp Extra { get; } = Inertia.Always(1);
}

public sealed class ConfiguredStringConverter(string prefix) : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(prefix + value);
}

public sealed class ConverterShapePage
{
    [JsonConverter(typeof(ArrayShapeConverter))] public int[] Converted { get; } = [1, 2];
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public int[] Numbers { get; } = [1, 2];
    [JsonConverter(typeof(DictionaryShapeConverter))] public Dictionary<string, object?> Container { get; } = new() { ["original"] = 1 };
    [JsonExtensionData] public Dictionary<string, object?> Extensions { get; } = new() { ["container.child"] = 9 };
    public InertiaProp Extra { get; } = Inertia.Always(1);
}

public sealed class ArrayShapeConverter : JsonConverter<int[]>
{
    public override int[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
    public override void Write(Utf8JsonWriter writer, int[] value, JsonSerializerOptions options) => writer.WriteStringValue("converted array");
}

public sealed class DictionaryShapeConverter : JsonConverter<Dictionary<string, object?>>
{
    public override Dictionary<string, object?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
    public override void Write(Utf8JsonWriter writer, Dictionary<string, object?> value, JsonSerializerOptions options) => writer.WriteStringValue("converted dictionary");
}

[JsonSerializable(typeof(ConfiguredConverterPage))]
[JsonSerializable(typeof(ConverterShapePage))]
internal sealed partial class SmallAllocationTestContext : JsonSerializerContext;
