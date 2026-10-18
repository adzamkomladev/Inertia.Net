using System.Buffers;
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

    /// <summary>The failure kind: the SSR server's error <c>type</c>, or <c>startup</c>, <c>connection</c>, <c>timeout</c>, <c>http</c>, <c>response</c>, <c>response-too-large</c> or <c>unknown</c>.</summary>
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
    SsrNodeProcess nodeProcess,
    TimeProvider timeProvider,
    ILogger<SsrGateway> logger) : IInertiaSsrRenderer
{
    internal const string HttpClientName = "Inertia.Net.Ssr";

    // Bundle presence is cached like the manifest: a present bundle is never stat-ed again in production, a missing one is re-checked
    // at most once per second (every second in Development), instead of a File.Exists on every server-rendered page.
    private readonly FileCache<string> _bundles = new(static path => path, environment.IsDevelopment(), timeProvider);
    private RenderEndpoint? _renderUrl;

    public async ValueTask<SsrRender?> RenderAsync(InertiaRootViewContext context)
    {
        var ssr = options.CurrentValue.Ssr;
        var http = context.HttpContext;
        if (!ssr.Enabled || IsExcluded(ssr.ExcludePaths, http.Request.Path.Value))
        {
            return null;
        }

        var devOrigin = vite.DevServerOrigin;
        http.RequestAborted.ThrowIfCancellationRequested();
        if (devOrigin is null && ssr.NodeProcess is not null && !nodeProcess.IsReadyFor(ssr.Url))
            return Fail(ssr, "startup", "The managed SSR process is not ready for the configured endpoint.", null, null);
        if (devOrigin is null && ssr.BundlePath is { Length: > 0 } bundle && _bundles.Get(Path.Combine(environment.ContentRootPath, bundle)) is null)
        {
            return null;
        }

        var url = devOrigin is null ? RenderUrl(ssr.Url) : $"{devOrigin}/__inertia_ssr";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(ssr.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ReadOnlyMemoryContent(context.PageJson) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (ssr.MaxResponseBytes is { } max && response.Content.Headers.ContentLength > max) throw new SsrResponseTooLargeException(max);
            using var stream = new SsrResponseStream(await response.Content.ReadAsStreamAsync(timeout.Token), ssr.MaxResponseBytes);
            if (!response.IsSuccessStatusCode)
            {
                return await FailedResponseAsync(ssr, (int)response.StatusCode, stream, timeout.Token);
            }

            // The bounded body goes into a pooled buffer and head/body are unescaped once into UTF-8 arrays that the root view writes
            // as-is: no UTF-16 strings (twice the size) and no re-encoding on the way out.
            using var buffer = new PooledBufferWriter(InitialBufferSize(response.Content.Headers.ContentLength));
            var known = response.Content.Headers.ContentLength is not null;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.GetMemory(known ? 1 : 4096), timeout.Token);
                if (read == 0)
                {
                    break;
                }

                buffer.Advance(read);
            }

            return ParseRender(buffer.WrittenMemory.Span) is { Body.Length: > 0 } page
                ? page
                : Fail(ssr, "response", "The SSR server returned an empty or invalid response.", null, null);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (SsrResponseTooLargeException ex)
        {
            return Fail(ssr, "response-too-large", ex.Message, null, ex);
        }
        catch (OperationCanceledException ex)
        {
            return Fail(ssr, "timeout", $"The SSR server did not respond within {ssr.Timeout}.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            return Fail(ssr, "connection", $"Could not reach the SSR server at {url}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not InertiaSsrException)
        {
            // e.g. a malformed Ssr.Url; like Laravel, SSR problems never take the page down unless ThrowOnError is set.
            return Fail(ssr, "unknown", $"SSR request to {url} failed: {ex.Message}", null, ex);
        }
    }

    // Exact for a declared length (plus one byte, so the final zero-length read does not grow the buffer); 16 KiB otherwise.
    private static int InitialBufferSize(long? contentLength) =>
        contentLength is { } length ? (int)Math.Clamp(length + 1, 1, 64 * 1024 * 1024) : 16 * 1024;

    /// <summary>
    /// Reads <c>{"head": string[], "body": string}</c> into UTF-8 HTML: head entries joined with <c>\n</c>. Null when the JSON is
    /// malformed, not an object, or not valid UTF-8.
    /// </summary>
    internal static SsrRender? ParseRender(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            ReadOnlyMemory<byte> head = default, body = default;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("body"u8))
                {
                    reader.Read();
                    body = reader.TokenType == JsonTokenType.String ? ReadString(ref reader) : default;
                    reader.Skip();
                }
                else if (reader.ValueTextEquals("head"u8))
                {
                    reader.Read();
                    head = reader.TokenType == JsonTokenType.StartArray ? ReadHead(ref reader) : default;
                    reader.Skip();
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            return System.Text.Unicode.Utf8.IsValid(head.Span) && System.Text.Unicode.Utf8.IsValid(body.Span) ? new SsrRender(head, body) : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) // InvalidOperationException: an unpaired \uD800 escape
        {
            return null;
        }
    }

    // One allocation of at most the escaped length; unescaping only shrinks.
    private static ReadOnlyMemory<byte> ReadString(ref Utf8JsonReader reader)
    {
        if (!reader.ValueIsEscaped)
        {
            return reader.ValueSpan.ToArray();
        }

        var bytes = new byte[reader.ValueSpan.Length];
        return bytes.AsMemory(0, reader.CopyString(bytes));
    }

    private static ReadOnlyMemory<byte> ReadHead(ref Utf8JsonReader reader)
    {
        var head = new ArrayBufferWriter<byte>(256);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                continue;
            }

            if (head.WrittenCount > 0)
            {
                head.Write("\n"u8);
            }

            var span = head.GetSpan(reader.ValueSpan.Length);
            head.Advance(reader.CopyString(span));
        }

        return head.WrittenMemory;
    }

    // The render endpoint, rebuilt only when Ssr.Url changes (options can be reloaded).
    private string RenderUrl(string baseUrl)
    {
        if (_renderUrl is { } cached && string.Equals(cached.Base, baseUrl, StringComparison.Ordinal))
        {
            return cached.Render;
        }

        var render = $"{baseUrl.TrimEnd('/')}/render";
        _renderUrl = new RenderEndpoint(baseUrl, render);
        return render;
    }

    private sealed record RenderEndpoint(string Base, string Render);

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

    private async ValueTask<SsrRender?> FailedResponseAsync(SsrOptions ssr, int status, Stream body, CancellationToken cancellationToken)
    {
        SsrError? error = null;
        try
        {
            error = await JsonSerializer.DeserializeAsync(body, InertiaJsonContext.Default.SsrError, cancellationToken);
        }
        catch (JsonException)
        {
            if (ssr.MaxResponseBytes is not null) await body.CopyToAsync(Stream.Null, cancellationToken);
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
