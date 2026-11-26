using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inertia.Net;

/// <summary>Diagnostic details of a failed SSR render. May contain secrets; send to logs or monitoring, never to the browser.</summary>
public sealed class SsrRenderFailed
{
    /// <summary>The renderer's error or a transport failure description.</summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }
    /// <summary>The failure category, such as render, connection or timeout.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }
    /// <summary>A diagnostic hint, or an empty string when unavailable.</summary>
    [JsonPropertyName("hint")]
    public string Hint { get; init; } = "";
    /// <summary>The time the adapter observed the failure.</summary>
    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }
    /// <summary>The requested page component.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("component")]
    public string? Component { get; init; }
    /// <summary>The requested page URL.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    public string? Url { get; init; }
    /// <summary>The browser API that failed on the server.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("browserApi")]
    public JsonElement? BrowserApi { get; init; }
    /// <summary>The renderer's stack trace, when supplied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stack")]
    public string? Stack { get; init; }
    /// <summary>The component source location, when supplied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sourceLocation")]
    public JsonElement? SourceLocation { get; init; }
}
