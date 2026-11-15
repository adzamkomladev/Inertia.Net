using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Tests;

/// <summary>
/// Values that cannot hold props go through the app's generated context when that writes the same output, and through Inertia's own
/// options otherwise.
/// </summary>
public sealed class GeneratedSerializerTests
{
    private static readonly WebRow[] Rows =
    [
        new(1, "</script><script>alert(1)</script><!--", "\u00e9 \u00fc \u2028 \u2029 +`'\"&", WebStatus.Open, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
        new(2, null, "plain", WebStatus.Closed, default),
    ];

    // Registered after the harness's own context, so this one is first in the chain.
    private static Harness Create(Action<JsonOptions>? configure = null) => new(configureServices: services =>
        services.PostConfigure<JsonOptions>(o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, WebTestContext.Default);
            configure?.Invoke(o);
        }));

    private static (JsonSerializerOptions Options, bool ContainsProps) Entry(Harness harness, Type type)
    {
        var (info, containsProps) = harness.Services.GetRequiredService<InertiaPageWriter>().TypeEntry(type);
        return (info.Options, containsProps);
    }

    private static async Task<string> RenderAsync(Harness harness, bool inertia)
    {
        var context = harness.Context("/rows");
        if (inertia) context.AsInertia();
        return await Harness.ExecuteAsync(context, Inertia.Render("Rows", new InertiaProps { ["rows"] = Rows, ["one"] = Rows[0] }));
    }

    [Fact]
    public void A_matching_first_context_serializes_values_without_props()
    {
        var harness = Create();
        Assert.Same(WebTestContext.Default.Options, Entry(harness, typeof(WebRow[])).Options);
        Assert.Same(WebTestContext.Default.Options, Entry(harness, typeof(WebRow)).Options);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Output_is_byte_identical_to_inertias_own_options(bool inertia)
    {
        var fast = await RenderAsync(Create(), inertia);
        // An unused converter is enough to make the context incompatible, so this harness writes through Inertia's options.
        var slow = Create(o => o.SerializerOptions.Converters.Add(new UnusedConverter()));
        Assert.NotSame(WebTestContext.Default.Options, Entry(slow, typeof(WebRow[])).Options);
        Assert.Equal(await RenderAsync(slow, inertia), fast);
        if (!inertia) Assert.DoesNotContain("</script><script>", fast, StringComparison.Ordinal); // JSON responses use the app's encoder
    }

    [Fact]
    public void A_context_with_other_settings_is_not_used()
    {
        var harness = Create(o => o.SerializerOptions.PropertyNamingPolicy = null);
        Assert.NotSame(WebTestContext.Default.Options, Entry(harness, typeof(WebRow)).Options);
    }

    [Fact]
    public void A_context_that_is_not_first_is_not_used()
    {
        var harness = Create(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()));
        Assert.NotSame(WebTestContext.Default.Options, Entry(harness, typeof(WebRow)).Options);
    }

    [Fact]
    public void Types_with_names_the_encoders_write_differently_are_not_used()
    {
        var harness = Create();
        Assert.NotSame(WebTestContext.Default.Options, Entry(harness, typeof(OddName)).Options);
        Assert.NotSame(WebTestContext.Default.Options, Entry(harness, typeof(OddName[])).Options);
    }

    [Fact]
    public void Preserving_big_integers_turns_it_off()
    {
        var harness = new Harness(o => o.PreserveBigIntegers = true, configureServices: services =>
            services.PostConfigure<JsonOptions>(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, WebTestContext.Default)));
        Assert.NotSame(WebTestContext.Default.Options, Entry(harness, typeof(WebRow)).Options);
    }

    private sealed class UnusedConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => throw new NotSupportedException();
    }
}

public enum WebStatus { Open, Closed }

public sealed record WebRow(int Id, string? Title, string Text, WebStatus Status, DateTime At);

public sealed record OddName([property: JsonPropertyName("a/b")] int Value);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WebRow[]))]
[JsonSerializable(typeof(OddName[]))]
internal sealed partial class WebTestContext : JsonSerializerContext;
