using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Tests;

public sealed class DictionaryKeyFormattingTests
{
    [Fact]
    public void Common_and_fallback_keys_match_system_text_json()
    {
        var harness = new Harness();
        Check(harness, new Dictionary<int, int> { [-17] = 1, [int.MaxValue] = 2 });
        Check(harness, new Dictionary<long, int> { [long.MinValue] = 1 });
        Check(harness, new Dictionary<Guid, int> { [Guid.Parse("5f70f661-9f97-4daa-92fa-e93c47ea55ed")] = 1 });
        Check(harness, new Dictionary<KeyFlags, int> { [KeyFlags.First | KeyFlags.Second] = 1, [(KeyFlags)64] = 2 });
        Check(harness, new ReadOnlyDictionary<DateTime, int>(new Dictionary<DateTime, int> { [new(2026, 11, 4, 10, 20, 30, DateTimeKind.Utc)] = 1 }));
        Check(harness, new ReadOnlyDictionary<DateTimeOffset, int>(new Dictionary<DateTimeOffset, int> { [new(2026, 11, 4, 10, 20, 30, TimeSpan.FromHours(2))] = 1 }));
        Check(harness, new ReadOnlyDictionary<bool, int>(new Dictionary<bool, int> { [true] = 1, [false] = 2 }));
    }

    [Fact]
    public void Custom_key_converters_and_enum_policies_are_preserved()
    {
        var harness = new Harness(configureServices: services => services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<KeyFlags>(JsonNamingPolicy.CamelCase));
            options.SerializerOptions.Converters.Add(new StringKeyConverter());
            options.SerializerOptions.Converters.Add(new GuidKeyConverter());
        }));
        Check(harness, new Dictionary<string, int> { ["OriginalName"] = 1 });
        Check(harness, new Dictionary<Guid, int> { [Guid.Empty] = 1 });
        Check(harness, new Dictionary<KeyFlags, int> { [KeyFlags.First | KeyFlags.Second] = 1, [(KeyFlags)64] = 2 });
    }

    [Fact]
    public void A_declared_object_key_uses_its_configured_converter()
    {
        var harness = new Harness(configureServices: services => services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new ObjectKeyConverter())));
        Check(harness, new ReadOnlyDictionary<object, int>(new Dictionary<object, int> { ["name"] = 1 }));
    }

    private static void Check(Harness harness, IDictionary dictionary)
    {
        var writer = harness.Services.GetRequiredService<InertiaPageWriter>();
        var info = writer.SerializerOptions.GetTypeInfo(dictionary.GetType());
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dictionary, info));
        Assert.Equal(document.RootElement.EnumerateObject().Select(property => property.Name), writer.DictionaryKeys(dictionary, info));
    }

    [Flags]
    public enum KeyFlags { First = 1, Second = 2 }

    private sealed class StringKeyConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WritePropertyName("custom_" + value);
    }

    private sealed class ObjectKeyConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void WriteAsPropertyName(Utf8JsonWriter writer, object value, JsonSerializerOptions options) => writer.WritePropertyName("object_" + value);
    }

    private sealed class GuidKeyConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetGuid();
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WritePropertyName("guid_" + value.ToString("N"));
    }
}
