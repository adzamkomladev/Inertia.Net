using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Thrown when server-side rendering fails and <see cref="SsrOptions.ThrowOnError"/> is set.</summary>
public sealed class InertiaSsrException : Exception
{
    /// <summary>Creates the exception.</summary>
    public InertiaSsrException(string message, string? type = null, string? hint = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Type = type;
        Hint = hint;
    }

    /// <summary>The failure kind: the SSR server's error <c>type</c>, or <c>connection</c>, <c>timeout</c>, <c>http</c> or <c>response</c>.</summary>
    public string? Type { get; }

    /// <summary>The SSR server's hint on how to fix the error, when it sent one.</summary>
    public string? Hint { get; }
}

internal sealed record SsrResponse(
    [property: JsonPropertyName("head")] string[]? Head,
    [property: JsonPropertyName("body")] string? Body);

internal sealed record SsrError(
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("hint")] string? Hint,
    [property: JsonPropertyName("browserApi")] JsonElement? BrowserApi,
    [property: JsonPropertyName("sourceLocation")] JsonElement? SourceLocation);

/// <summary>Renders pages on the Inertia SSR server (or the Vite dev server) and falls back to client-side rendering on failure.</summary>
internal sealed partial class SsrGateway(
    IOptionsMonitor<InertiaOptions> options,
    ViteAssets vite,
    IHttpClientFactory httpClients,
    IHostEnvironment environment,
    ILogger<SsrGateway> logger) : IInertiaSsrRenderer
{
    internal const string HttpClientName = "Inertia.Net.Ssr";

    public async ValueTask<SsrRender?> RenderAsync(InertiaRootViewContext context)
    {
        var ssr = options.CurrentValue.Ssr;
        var http = context.HttpContext;
        if (!ssr.Enabled || IsExcluded(ssr.ExcludePaths, http.Request.Path.Value))
        {
            return null;
        }

        var devOrigin = vite.DevServerOrigin;
        if (devOrigin is null && ssr.BundlePath is { Length: > 0 } bundle && !File.Exists(Path.Combine(environment.ContentRootPath, bundle)))
        {
            return null;
        }

        var url = devOrigin is null ? $"{ssr.Url.TrimEnd('/')}/render" : $"{devOrigin}/__inertia_ssr";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(ssr.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ReadOnlyMemoryContent(context.PageJson) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, timeout.Token);
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return FailedResponse(ssr, (int)response.StatusCode, bytes);
            }

            SsrResponse? page = null;
            try
            {
                page = JsonSerializer.Deserialize(bytes, InertiaJsonContext.Default.SsrResponse);
            }
            catch (JsonException)
            {
            }

            return page is { Body.Length: > 0 }
                ? new SsrRender(string.Join('\n', page.Head ?? []), page.Body)
                : Fail(ssr, "response", "The SSR server returned an empty or invalid response.", null, null);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return Fail(ssr, "timeout", $"The SSR server did not respond within {ssr.Timeout}.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            return Fail(ssr, "connection", $"Could not reach the SSR server at {url}: {ex.Message}", null, ex);
        }
    }

    /// <summary>Matches the request path against exact entries and entries ending in <c>*</c> (prefix match).</summary>
    internal static bool IsExcluded(IList<string> patterns, string? path)
    {
        path ??= "/";
        foreach (var pattern in patterns)
        {
            if (pattern.EndsWith('*') ? path.StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal) : path == pattern)
            {
                return true;
            }
        }

        return false;
    }

    private SsrRender? FailedResponse(SsrOptions ssr, int status, byte[] body)
    {
        SsrError? error = null;
        try
        {
            error = JsonSerializer.Deserialize(body, InertiaJsonContext.Default.SsrError);
        }
        catch (JsonException)
        {
        }

        return error is null
            ? Fail(ssr, "http", $"The SSR server responded with HTTP {status}.", null, null)
            : Fail(ssr, error.Type ?? "http", error.Error ?? $"The SSR server responded with HTTP {status}.", error.Hint, null, error);
    }

    private SsrRender? Fail(SsrOptions ssr, string type, string message, string? hint, Exception? inner, SsrError? details = null)
    {
        LogFailure(logger, inner, type, message, hint, details?.BrowserApi?.ToString(), details?.SourceLocation?.ToString());
        return ssr.ThrowOnError ? throw new InertiaSsrException($"Inertia SSR failed ({type}): {message}", type, hint, inner) : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inertia SSR failed ({Type}), falling back to client-side rendering: {Error} Hint: {Hint} Browser API: {BrowserApi} Source: {SourceLocation}")]
    private static partial void LogFailure(ILogger logger, Exception? exception, string type, string error, string? hint, string? browserApi, string? sourceLocation);
}
