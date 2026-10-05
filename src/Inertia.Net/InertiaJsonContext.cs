using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inertia.Net;

/// <summary>
/// Metadata for the library's own types and common prop values. Chained after the app's resolver,
/// so plain values in <see cref="InertiaProps"/> work under Native AOT without the app registering them.
/// </summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TimeOnly))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(InertiaProps))]
[JsonSerializable(typeof(InertiaProps[]))]
[JsonSerializable(typeof(List<InertiaProps>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal sealed partial class InertiaJsonContext : JsonSerializerContext;
