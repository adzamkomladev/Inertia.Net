using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Inertia.Net.Tests;

public class SerializationRegressionTests
{
    [Fact]
    public async Task Reused_prop_storage_does_not_leak_values_after_failure_or_between_concurrent_renders()
    {
        var harness = new Harness();
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.PageAsync(new InertiaProps
        {
            ["secret"] = "previous request",
            ["failure"] = Inertia.Prop<string>(() => throw new InvalidOperationException("loader failed")),
        }));
        var pages = await Task.WhenAll(Enumerable.Range(0, 32).Select(async index =>
        {
            var page = await harness.PageAsync(new InertiaProps
            {
                ["id"] = Inertia.Prop(async ct => { await Task.Delay(1, ct); return index; }),
                ["nested.value"] = index,
            });
            Assert.Equal(index, page["props"]!["id"]!.GetValue<int>());
            Assert.Equal(index, page["props"]!["nested"]!["value"]!.GetValue<int>());
            JsonAssert.Missing(page["props"], "secret");
            JsonAssert.Missing(page["props"], "failure");
            return page;
        }));
        Assert.Equal(32, pages.Length);
        var empty = await harness.PageAsync(new InertiaProps());
        JsonAssert.Missing(empty["props"], "id");
        JsonAssert.Missing(empty["props"], "nested");
    }

    private static Harness Create(bool generated, bool defaults = false, bool bigIntegers = false) => new(
        o => o.PreserveBigIntegers = bigIntegers,
        configureServices: services => services.PostConfigure<HttpJsonOptions>(o =>
        {
            o.SerializerOptions.TypeInfoResolver = generated ? RegressionJsonContext.Default : new DefaultJsonTypeInfoResolver();
            if (defaults) o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
        }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Indexed_reload_replaces_the_whole_array_and_reports_all_loaded_metadata(bool generated)
    {
        var harness = Create(generated);
        var calls = 0;
        var props = new InertiaProps
        {
            ["rows"] = new List<Row>
        {
            new("first", Inertia.Defer(() => ++calls, "a").Once().Append()),
            new("second", Inertia.Defer(() => ++calls, "b").Once().Append()),
        }
        };
        var initial = await harness.PageAsync(props);
        JsonAssert.Equal("""[{"name":"first"},{"name":"second"}]""", initial["props"]!["rows"]);
        var partial = await harness.PageAsync(props, c => c.AsPartial("rows.1.extra"));
        JsonAssert.Equal("""[{"name":"first","extra":1},{"name":"second","extra":2}]""", partial["props"]!["rows"]);
        JsonAssert.Equal("""["rows.0.extra","rows.1.extra"]""", partial["mergeProps"]);
        Assert.Equal(2, partial["onceProps"]!.AsObject().Count);
        var excluded = await harness.PageAsync(props, c => c.AsPartial("rows.1.extra", "rows.0.name"));
        JsonAssert.Missing(excluded["props"], "rows");
        Assert.Equal(2, calls);
        var always = await harness.PageAsync(new InertiaProps { ["rows"] = Inertia.Always(new List<Row> { new("keep", null) }) },
            c => c.AsPartial(except: "rows.0.name"));
        Assert.Equal("keep", always["props"]!["rows"]![0]!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_dictionaries_whose_values_hold_props_are_walked(bool generated)
    {
        var harness = new Harness(configureServices: services => services.PostConfigure<HttpJsonOptions>(o =>
        {
            o.SerializerOptions.TypeInfoResolver = generated ? RegressionJsonContext.Default : new DefaultJsonTypeInfoResolver();
            o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        }));
        var calls = 0;
        var props = new InertiaProps
        {
            ["byName"] = new Dictionary<string, Row> { ["FirstRow"] = new("first", Inertia.Defer(() => ++calls, "rows")) },
            ["byId"] = new Dictionary<int, Row> { [7] = new("seven", Inertia.Prop(() => 70)) },
            ["byKind"] = new Dictionary<RowKind, Row> { [RowKind.SecondKind] = new("second", null) },
        };

        var initial = await harness.PageAsync(props);
        JsonAssert.Equal("""{"firstRow":{"name":"first"}}""", initial["props"]!["byName"]);
        JsonAssert.Equal("""{"7":{"name":"seven","extra":70}}""", initial["props"]!["byId"]);
        JsonAssert.Equal("""{"secondKind":{"name":"second","extra":null}}""", initial["props"]!["byKind"]);
        JsonAssert.Equal("""{"rows":["byName.firstRow.extra"]}""", initial["deferredProps"]);
        Assert.Equal(0, calls);

        // Dictionary keys are path segments, like object members.
        var partial = await harness.PageAsync(props, c => c.AsPartial("byName.firstRow.extra"));
        JsonAssert.Equal("""{"firstRow":{"extra":1}}""", partial["props"]!["byName"]);
        JsonAssert.Missing(partial["props"], "byId");
        Assert.Equal(1, calls);

        // The keys match a plain serialization of the same dictionary type.
        var options = harness.Services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        Assert.Equal("""{"secondKind":{"name":"second","extra":null}}""",
            JsonSerializer.Serialize(new Dictionary<RowKind, Row> { [RowKind.SecondKind] = new("second", null) }, options.GetTypeInfo(typeof(Dictionary<RowKind, Row>))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_dictionary_keys_preserve_custom_converters_when_the_scratch_buffer_is_reused(bool generated)
    {
        var harness = new Harness(configureServices: services => services.PostConfigure<HttpJsonOptions>(o =>
        {
            o.SerializerOptions.TypeInfoResolver = generated ? RegressionJsonContext.Default : new DefaultJsonTypeInfoResolver();
            o.SerializerOptions.Converters.Add(new DictionaryKeyConverter());
        }));
        var rows = new Dictionary<int, Row>
        {
            [100] = new("first", Inertia.Prop(() => 1)),
            [2] = new("second", Inertia.Prop(() => 2)),
            [-30] = new("third", Inertia.Prop(() => 3)),
        };

        var page = await harness.PageAsync(new InertiaProps { ["rows"] = rows });
        JsonAssert.Equal("""{"key:100":{"name":"first","extra":1},"key:2":{"name":"second","extra":2},"key:-30":{"name":"third","extra":3}}""", page["props"]!["rows"]);
        var partial = await harness.PageAsync(new InertiaProps { ["rows"] = rows }, c => c.AsPartial("rows.key:2.extra"));
        JsonAssert.Equal("""{"key:2":{"extra":2}}""", partial["props"]!["rows"]);
    }

    private sealed class DictionaryKeyConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetInt32();

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
            writer.WritePropertyName($"key:{value}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Members_with_the_same_number_handling_share_serializer_options(bool generated)
    {
        var harness = Create(generated);
        var page = await harness.PageAsync(new ContractPage());
        Assert.Equal("42", (string?)page["props"]!["number"]);
        Assert.Equal("0", (string?)page["props"]!["count"]);
        Assert.Equal(1, harness.Services.GetRequiredService<InertiaPageWriter>().MemberOptionsCount);
    }

    [Fact]
    public async Task Typed_page_members_of_dictionary_type_are_walked()
    {
        var page = await Create(generated: true).PageAsync(new DictionaryPage());
        JsonAssert.Equal("""{"a":{"name":"a"}}""", page["props"]!["rows"]);
        JsonAssert.Equal("""{"default":["rows.a.extra"]}""", page["deferredProps"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unrelated.child")]
    public async Task Partial_reload_does_not_require_metadata_for_omitted_loaders(string? except)
    {
        var calls = 0;
        var page = await Create(generated: true).PageAsync(new InertiaProps
        {
            ["wanted"] = 1,
            ["omitted"] = Inertia.Defer(() => { calls++; return new UnregisteredValue(); }),
        }, c => c.AsPartial("wanted", except));
        Assert.Equal(1, page["props"]!["wanted"]!.GetValue<int>());
        JsonAssert.Missing(page["props"], "omitted");
        Assert.Equal(0, calls);
    }
    private sealed class UnregisteredValue;

    [Fact]
    public async Task Omitted_and_rescued_array_elements_retain_their_positions()
    {
        var page = await new Harness().PageAsync(new InertiaProps
        {
            ["items"] = new object[]
        {
            Inertia.Defer(() => 1), "second", Inertia.Prop<int>(() => throw new InvalidOperationException()).Rescue(),
        }
        });
        JsonAssert.Equal("""[null,"second",null]""", page["props"]!["items"]);
        JsonAssert.Equal("""{"default":["items.0"]}""", page["deferredProps"]);
        JsonAssert.Equal("""["items.2"]""", page["rescuedProps"]);
    }

    [Fact]
    public async Task Dynamically_typed_array_loader_applies_exclusion_and_parent_metadata()
    {
        var harness = new Harness();
        var rows = Inertia.Prop<object>(() => new object[] { "first", "second" }).Append().Once();
        var page = await harness.PageAsync(new InertiaProps { ["rows"] = rows }, c => c.AsPartial("rows.1"));
        JsonAssert.Equal("""["first","second"]""", page["props"]!["rows"]);
        JsonAssert.Equal("""["rows"]""", page["mergeProps"]);
        Assert.NotNull(page["onceProps"]!["rows"]);
        var excluded = await harness.PageAsync(new InertiaProps { ["rows"] = rows }, c => c.AsPartial("rows.1", "rows.0"));
        JsonAssert.Missing(excluded["props"], "rows");
        JsonAssert.Missing(excluded, "mergeProps");
        JsonAssert.Missing(excluded, "onceProps");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Polymorphic_declared_members_preserve_derived_fields_and_discriminators(bool generated)
    {
        var harness = Create(generated);
        var page = await harness.PageAsync(new PolymorphicContainer { Item = new RegisteredPage() }, c => c.AsPartial("item.lazy"));
        JsonAssert.Equal("""{"$type":"registered","lazy":7}""", page["props"]!["item"]);
        var initial = await harness.PageAsync(new PolymorphicContainer { Item = new RegisteredPage() });
        JsonAssert.Equal("""{"$type":"registered","name":"derived"}""", initial["props"]!["item"]);
        await Assert.ThrowsAsync<NotSupportedException>(() => harness.PageAsync(new PolymorphicContainer { Item = new UnknownPage() }));
    }

    [Theory]
    [InlineData(false, JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
    [InlineData(true, JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
    [InlineData(false, JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
    [InlineData(true, JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
    public async Task Unknown_polymorphic_types_follow_the_serializer_fallback_contract(bool generated, JsonUnknownDerivedTypeHandling fallback)
    {
        IJsonTypeInfoResolver Resolver(bool ordinary) => (generated ? (IJsonTypeInfoResolver)RegressionJsonContext.Default : new DefaultJsonTypeInfoResolver())
            .WithAddedModifier(info =>
            {
                if (info.Type == typeof(PolymorphicPage))
                {
                    info.PolymorphismOptions!.UnknownDerivedTypeHandling = fallback;
                    info.PolymorphismOptions.DerivedTypes[0] = new JsonDerivedType(typeof(RegisteredPage), 19);
                }
                if (ordinary)
                    for (var i = info.Properties.Count - 1; i >= 0; i--)
                        if (info.Properties[i].PropertyType == typeof(InertiaProp)) info.Properties.RemoveAt(i);
            });
        var harness = new Harness(configureServices: services => services.PostConfigure<HttpJsonOptions>(o => o.SerializerOptions.TypeInfoResolver = Resolver(false)));
        var container = new PolymorphicContainer { Item = new UnknownRegisteredPage() };
        var page = await harness.PageAsync(container, c => c.AsPartial("item"));
        Assert.Equal(7, page["props"]!["item"]!["lazy"]!.GetValue<int>());
        page["props"]!["item"]!.AsObject().Remove("lazy");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = Resolver(true) };
        var reference = JsonSerializer.SerializeToNode(container, options.GetTypeInfo(typeof(PolymorphicContainer)));
        Assert.True(JsonNode.DeepEquals(reference!["item"], page["props"]!["item"]));
        JsonAssert.Missing(page["props"]!["item"], "secret");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Manual_objects_honor_defaults_number_handling_and_callbacks(bool generated, bool nested)
    {
        var harness = Create(generated, defaults: true);
        var model = new ContractPage();
        var page = await harness.PageAsync(nested ? new InertiaProps { ["item"] = model } : model);
        var actual = nested ? page["props"]!["item"] : page["props"];
        if (!nested) actual!.AsObject().Remove("errors");
        JsonAssert.Equal("""{"number":"42","nullableZero":"0","keepZero":"0","value":"normalized"}""", actual);
        Assert.Equal(1, model.BeforeCalls);
        Assert.Equal(1, model.AfterCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dom_big_integers_survive_props_flash_and_state_restoration(bool generated)
    {
        const string json = """{"safe":9007199254740991,"values":[9007199254740993,-9007199254740993,18446744073709551616],"fraction":1.25}""";
        const string expected = """{"safe":9007199254740991,"values":[{"$bigint":"9007199254740993"},{"$bigint":"-9007199254740993"},{"$bigint":"18446744073709551616"}],"fraction":1.25}""";
        using var document = JsonDocument.Parse(json);
        var harness = Create(generated, bigIntegers: true);
        var feature = new InertiaFeature().Flash("element", document.RootElement).Flash("node", JsonNode.Parse(json));
        var writer = harness.Services.GetRequiredService<InertiaPageWriter>();
        var restored = new InertiaFeature();
        Assert.True(InertiaStateSerializer.TryRestore(restored, InertiaStateSerializer.Serialize(feature, writer.SerializerOptions)));
        var page = await harness.PageAsync(new InertiaProps
        {
            ["element"] = document.RootElement,
            ["node"] = JsonNode.Parse(json),
            ["dto"] = new DomPage { Data = document.RootElement },
        }, c => c.Features.Set(restored));
        foreach (var key in new[] { "element", "node" })
        {
            JsonAssert.Equal(expected, page["props"]![key]);
            JsonAssert.Equal(expected, page["flash"]![key]);
        }
        JsonAssert.Equal(expected, page["props"]!["dto"]!["data"]);
        var plain = await Create(generated).PageAsync(new InertiaProps { ["element"] = document.RootElement });
        JsonAssert.Equal(json, plain["props"]!["element"]);
    }
}

[JsonPolymorphic]
[JsonDerivedType(typeof(RegisteredPage), "registered")]
public class PolymorphicPage
{
    public InertiaProp Lazy { get; init; } = Inertia.Defer(() => 7);
}

public class RegisteredPage : PolymorphicPage
{
    public string Name { get; init; } = "derived";
}

public sealed class UnknownPage : PolymorphicPage;
public sealed class UnknownRegisteredPage : RegisteredPage { public string Secret { get; init; } = "hidden"; }

public sealed class PolymorphicContainer
{
    public PolymorphicPage Item { get; init; } = new();
}

[JsonNumberHandling(JsonNumberHandling.WriteAsString)]
public sealed class ContractPage : IJsonOnSerializing, IJsonOnSerialized
{
    public int Count { get; init; }
    public bool Enabled { get; init; }
    public int Number { get; init; } = 42;
    public int? NullableZero { get; init; } = 0;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int KeepZero { get; init; }
    public string Value { get; set; } = "raw";
    public InertiaProp Lazy { get; init; } = Inertia.Defer(() => 1);
    [JsonIgnore] public int BeforeCalls { get; private set; }
    [JsonIgnore] public int AfterCalls { get; private set; }
    public void OnSerializing() { BeforeCalls++; Value = "normalized"; }
    public void OnSerialized() => AfterCalls++;
}

public enum RowKind
{
    FirstKind,
    SecondKind,
}

public sealed class DictionaryPage
{
    public Dictionary<string, Row> Rows { get; init; } = new() { ["a"] = new("a", Inertia.Defer(() => 1)) };
}

public sealed class DomPage
{
    public JsonElement Data { get; init; }
}

[JsonSerializable(typeof(List<Row>))]
[JsonSerializable(typeof(PolymorphicContainer))]
[JsonSerializable(typeof(UnknownPage))]
[JsonSerializable(typeof(UnknownRegisteredPage))]
[JsonSerializable(typeof(ContractPage))]
[JsonSerializable(typeof(DomPage))]
[JsonSerializable(typeof(DictionaryPage))]
[JsonSerializable(typeof(Dictionary<string, Row>))]
[JsonSerializable(typeof(Dictionary<int, Row>))]
[JsonSerializable(typeof(Dictionary<RowKind, Row>))]
internal sealed partial class RegressionJsonContext : JsonSerializerContext;
