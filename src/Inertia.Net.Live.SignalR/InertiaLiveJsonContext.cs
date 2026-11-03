using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inertia.Net.Live.SignalR;

/// <summary>The hub's argument and result types, so the SignalR JSON protocol needs no reflection (Native AOT).</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class InertiaLiveJsonContext : JsonSerializerContext;
