using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Inertia.Net;

/// <summary>Settings for the Inertia DevTools recorder (<see cref="InertiaDevToolsExtensions.AddInertiaDevTools"/>).</summary>
public sealed class InertiaDevToolsOptions
{
    /// <summary>Whether requests are recorded. Null (the default) records in Development only.</summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Who may read the recorded entries. Null (the default) allows everyone in Development and nobody elsewhere. Outside Development
    /// the read API answers only requests this returns true for (for example a signed-in developer), and <c>404</c> otherwise.
    /// </summary>
    public Func<HttpContext, bool>? Authorize { get; set; }

    /// <summary>Entries kept per browser tab; older ones are dropped. Default 100.</summary>
    public int MaxEntriesPerTab { get; set; } = 100;

    /// <summary>Entries kept in all. Default 1000.</summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>How long an entry is kept. Default 30 minutes.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Request bodies larger than this are not recorded (reason <c>too-large</c>). Default 64 KiB.</summary>
    public int MaxBodyBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Prop, body and header names whose values are replaced with <c>[REDACTED]</c>, compared without case. Names containing
    /// <c>password</c>, <c>secret</c>, <c>token</c>, <c>authorization</c>, <c>cookie</c> or <c>api_key</c>/<c>apikey</c> are always redacted.
    /// </summary>
    public ISet<string> RedactedKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Support for the Inertia DevTools browser extension (the DevTools protocol of Inertia v3): every request through
/// <c>app.UseInertia()</c> is recorded in memory and read back by the extension. Sensitive values are redacted; nothing leaves the app.
/// </summary>
public static class InertiaDevToolsExtensions
{
    /// <summary>The path prefix of the read API.</summary>
    public const string PathPrefix = "/_inertia/devtools";

    /// <summary>
    /// Adds the DevTools recorder. It records only while <see cref="InertiaDevToolsOptions.Enabled"/> (Development by default); map the
    /// read API with <see cref="MapInertiaDevTools"/>. Call it before <c>app.UseInertia()</c> is built, i.e. at service registration.
    /// </summary>
    public static IServiceCollection AddInertiaDevTools(this IServiceCollection services, Action<InertiaDevToolsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<InertiaDevToolsOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.Validate(o => o.MaxEntriesPerTab > 0 && o.MaxEntries > 0 && o.MaxAge > TimeSpan.Zero && o.MaxBodyBytes >= 0,
            "InertiaDevToolsOptions limits must be positive.").ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DevToolsRecorder>();
        return services;
    }

    /// <summary>
    /// Maps the DevTools read API: <c>GET /_inertia/devtools/entries/{id}</c> and <c>GET /_inertia/devtools/entries</c> (newest first; optional
    /// <c>component</c>, <c>type</c>, <c>exclude</c>, <c>offset</c> and <c>limit</c> query parameters). Both answer <c>404</c> when recording is
    /// off or <see cref="InertiaDevToolsOptions.Authorize"/> refuses the request.
    /// </summary>
    public static IEndpointConventionBuilder MapInertiaDevTools(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var recorder = endpoints.ServiceProvider.GetService<DevToolsRecorder>()
            ?? throw new InvalidOperationException("Inertia DevTools services are not registered. Call services.AddInertiaDevTools() at startup.");
        var group = endpoints.MapGroup(PathPrefix);
        group.MapGet("/entries/{id}", recorder.GetEntryAsync);
        group.MapGet("/entries", recorder.ListEntriesAsync);
        return group;
    }
}

/// <summary>Per-request recording state, on <see cref="HttpContext.Features"/> while a request is recorded.</summary>
internal sealed class DevToolsCapture(string id, long started)
{
    public string Id { get; } = id;

    public long Started { get; } = started;

    public string? Component { get; set; }

    public byte[]? PageJson { get; set; }
}

/// <summary>Records entries and serves them (docs: inertiajs.com/docs/v3/advanced/devtools-protocol).</summary>
internal sealed partial class DevToolsRecorder
{
    private static readonly string[] SensitiveFragments = ["password", "secret", "token", "authorization", "cookie", "apikey", "api_key", "api-key"];
    private const string Redacted = "[REDACTED]";

    private readonly InertiaDevToolsOptions _options;
    private readonly bool _enabled;
    private readonly bool _development;
    private readonly TimeProvider _time;
    private readonly PageComponentCheck _pages;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Stored> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LinkedList<Stored>> _byTab = new(StringComparer.Ordinal);

    public DevToolsRecorder(IOptions<InertiaDevToolsOptions> options, IHostEnvironment environment, TimeProvider time, PageComponentCheck pages, ILogger<DevToolsRecorder> logger)
    {
        _options = options.Value;
        _development = environment.IsDevelopment();
        _enabled = _options.Enabled ?? _development;
        _time = time;
        _pages = pages;
        _logger = logger;
    }

    public bool Enabled => _enabled;

    internal int Count
    {
        get
        {
            lock (_lock)
            {
                return _byId.Count;
            }
        }
    }

    /// <summary>Starts recording the request, or returns null for the read API itself and while recording is off.</summary>
    public DevToolsCapture? Begin(HttpContext httpContext)
    {
        var request = httpContext.Request;
        if (!_enabled || request.Path.StartsWithSegments(InertiaDevToolsExtensions.PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var capture = new DevToolsCapture(Guid.CreateVersion7().ToString("N"), Stopwatch.GetTimestamp());
        httpContext.Features.Set(capture);
        if (IsTextBody(request) && request.ContentLength is null or > 0)
        {
            request.EnableBuffering(); // read back after the handler
        }

        httpContext.Response.OnStarting(static state =>
        {
            var (http, capture) = ((HttpContext, DevToolsCapture))state;
            var headers = http.Response.Headers;
            headers["X-Inertia-Devtools-Id"] = capture.Id;
            // A prefetch must not move the batch cursor, so it answers with its own id.
            var parent = http.Request.Headers["X-Inertia-Devtools-Parent"].ToString();
            headers["X-Inertia-Devtools-Parent-Out"] = parent.Length == 0 || IsPrefetch(http.Request) ? capture.Id : parent;
            return Task.CompletedTask;
        }, (httpContext, capture));
        return capture;
    }

    /// <summary>Stores the entry. Never throws: a recording failure is logged and the request is unaffected.</summary>
    public async Task CompleteAsync(HttpContext httpContext, DevToolsCapture capture, bool failed)
    {
        try
        {
            var requestBody = await ReadRequestBodyAsync(httpContext.Request);
            var now = _time.GetUtcNow();
            var tab = Header(httpContext.Request, "X-Inertia-Devtools-Tab");
            var requestType = RequestType(httpContext.Request, capture.PageJson is not null);
            var json = Write(httpContext, capture, requestBody, requestType, tab, now, failed);
            Add(new Stored(capture.Id, tab ?? "", capture.Component, requestType, now, json));
        }
        catch (Exception ex)
        {
            LogRecordingFailed(_logger, ex, httpContext.Request.Path);
        }
    }

    /// <summary>The discovery tag for the first HTML response: the entry id as a JSON string.</summary>
    public static byte[] DiscoveryTag(string id) =>
        Encoding.UTF8.GetBytes($"<script data-inertia-devtools-id type=\"application/json\">\"{id}\"</script>");

    internal Task GetEntryAsync(HttpContext httpContext)
    {
        if (!CanRead(httpContext))
        {
            return NotFound(httpContext);
        }

        var id = httpContext.Request.RouteValues["id"] as string ?? "";
        byte[]? json;
        lock (_lock)
        {
            Prune();
            json = _byId.TryGetValue(id, out var stored) ? stored.Json : null;
        }

        return json is null ? NotFound(httpContext) : WriteJsonAsync(httpContext, json);
    }

    internal Task ListEntriesAsync(HttpContext httpContext)
    {
        if (!CanRead(httpContext))
        {
            return NotFound(httpContext);
        }

        var query = httpContext.Request.Query;
        var component = query["component"].ToString();
        var types = Split(query["type"]);
        var excluded = Split(query["exclude"]);
        var offset = int.TryParse(query["offset"], NumberStyles.None, CultureInfo.InvariantCulture, out var o) ? o : 0;
        var limit = int.TryParse(query["limit"], NumberStyles.None, CultureInfo.InvariantCulture, out var l) ? l : int.MaxValue;
        List<byte[]> selected;
        lock (_lock)
        {
            Prune();
            selected = [.. _byId.Values
                .Where(e => (component.Length == 0 || e.Component == component)
                    && (types.Length == 0 || types.Contains(e.RequestType))
                    && !excluded.Contains(e.RequestType))
                .OrderByDescending(e => e.At)
                .ThenByDescending(e => e.Id, StringComparer.Ordinal)
                .Skip(offset)
                .Take(limit)
                .Select(e => e.Json)];
        }

        var buffer = new ArrayBufferWriter<byte>();
        buffer.Write("["u8);
        for (var i = 0; i < selected.Count; i++)
        {
            if (i > 0)
            {
                buffer.Write(","u8);
            }

            buffer.Write(selected[i]);
        }

        buffer.Write("]"u8);
        return WriteJsonAsync(httpContext, buffer.WrittenMemory);

        static string[] Split(StringValues value) => value.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private bool CanRead(HttpContext httpContext) =>
        _enabled && (_options.Authorize is { } authorize ? authorize(httpContext) : _development);

    private static Task NotFound(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    private static async Task WriteJsonAsync(HttpContext httpContext, ReadOnlyMemory<byte> json)
    {
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        httpContext.Response.Headers.CacheControl = "no-store";
        await httpContext.Response.Body.WriteAsync(json, httpContext.RequestAborted);
    }

    private void Add(Stored entry)
    {
        lock (_lock)
        {
            Prune();
            if (!_byTab.TryGetValue(entry.Tab, out var list))
            {
                _byTab[entry.Tab] = list = new LinkedList<Stored>();
            }

            list.AddLast(entry);
            _byId[entry.Id] = entry;
            if (list.Count > _options.MaxEntriesPerTab)
            {
                Remove(list.First!.Value);
            }

            // The oldest entry of all, which is the first of some tab.
            while (_byId.Count > _options.MaxEntries)
            {
                Remove(_byTab.Values.Select(l => l.First!.Value).MinBy(e => e.At)!);
            }
        }
    }

    // Drops expired entries from the front of each tab (entries are added in time order).
    private void Prune()
    {
        var cutoff = _time.GetUtcNow() - _options.MaxAge;
        foreach (var list in _byTab.Values.ToList())
        {
            while (list.First is { } first && first.Value.At < cutoff)
            {
                Remove(first.Value);
            }
        }
    }

    private void Remove(Stored entry)
    {
        _byId.Remove(entry.Id);
        if (_byTab.TryGetValue(entry.Tab, out var list))
        {
            list.Remove(entry);
            if (list.Count == 0)
            {
                _byTab.Remove(entry.Tab);
            }
        }
    }

    // The protocol's request type table, first match wins.
    internal static string RequestType(HttpRequest request, bool pageRendered)
    {
        var headers = request.Headers;
        if (headers.ContainsKey(InertiaPrecognition.Header))
        {
            return "precognition";
        }

        if (string.IsNullOrEmpty(headers[InertiaHeaders.Inertia]))
        {
            return pageRendered ? "initial" : "http";
        }

        return headers.ContainsKey("X-Inertia-Devtools-Deferred") ? "deferred"
            : headers.ContainsKey("X-Inertia-Devtools-Poll") ? "poll"
            : headers.ContainsKey(InertiaHeaders.PartialComponent) ? "partial"
            : IsPrefetch(request) ? "prefetch"
            : "navigate";
    }

    private static bool IsPrefetch(HttpRequest request) =>
        string.Equals(request.Headers[InertiaHeaders.Purpose], "prefetch", StringComparison.OrdinalIgnoreCase);

    private static string? Header(HttpRequest request, string name) => request.Headers[name].ToString() is { Length: > 0 } value ? value : null;

    private static bool IsTextBody(HttpRequest request) =>
        request.ContentType is { } type
        && (type.Contains("json", StringComparison.OrdinalIgnoreCase) || type.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase));

    private bool IsSensitive(string name)
    {
        if (_options.RedactedKeys.Contains(name))
        {
            return true;
        }

        foreach (var fragment in SensitiveFragments)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<Body> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.ContentLength == 0 || (request.ContentLength is null && !request.Headers.ContainsKey("Transfer-Encoding")))
        {
            return Body.Empty;
        }

        if (string.IsNullOrEmpty(request.Headers[InertiaHeaders.Inertia]))
        {
            return Body.Omitted("non-inertia-request");
        }

        if (!IsTextBody(request))
        {
            return Body.Omitted(request.HasFormContentType ? "binary" : "non-textual");
        }

        if (request.ContentLength > _options.MaxBodyBytes || !request.Body.CanSeek)
        {
            return Body.Omitted(request.Body.CanSeek ? "too-large" : "streamed");
        }

        request.Body.Position = 0;
        var buffer = new byte[_options.MaxBodyBytes + 1];
        var read = 0;
        while (read < buffer.Length && await request.Body.ReadAsync(buffer.AsMemory(read)) is var n and > 0)
        {
            read += n;
        }

        request.Body.Position = 0;
        if (read > _options.MaxBodyBytes)
        {
            return Body.Omitted("too-large");
        }

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        if (request.ContentType!.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Body.Json(JsonNode.Parse(text));
            }
            catch (JsonException)
            {
                return Body.Text(text);
            }
        }

        // A form: decoded into an object so its fields can be redacted.
        var form = new JsonObject();
        foreach (var (key, value) in Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(text))
        {
            form[key] = value.ToString();
        }

        return Body.Json(form);
    }

    private byte[] Write(HttpContext httpContext, DevToolsCapture capture, Body requestBody, string requestType, string? tab, DateTimeOffset now, bool failed)
    {
        var request = httpContext.Request;
        var response = httpContext.Response;
        var page = capture.PageJson is { } pageJson ? JsonNode.Parse(pageJson) as JsonObject : null;
        var status = failed && !response.HasStarted ? StatusCodes.Status500InternalServerError : response.StatusCode;

        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("__meta");
            writer.WriteString("id", capture.Id);
            writer.WriteString("method", request.Method);
            writer.WriteString("url", string.Concat(request.PathBase.ToUriComponent(), request.Path.ToUriComponent(), request.QueryString.ToUriComponent()));
            writer.WriteNumber("status", status);
            writer.WriteString("requestType", requestType);
            WriteStringOrNull(writer, "component", capture.Component);
            writer.WriteString("timestamp", now.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteNumber("utime", now.ToUnixTimeMilliseconds() / 1000.0);
            WriteStringOrNull(writer, "tabUuid", tab);
            WriteStringOrNull(writer, "batchId", Header(request, "X-Inertia-Devtools-Parent"));
            writer.WriteNumber("serverTimingMs", Math.Round(Stopwatch.GetElapsedTime(capture.Started).TotalMilliseconds, 2));
            // A redirect, Location() or a fragment redirect.
            var location = new[] { response.Headers.Location, response.Headers[InertiaHeaders.Location], response.Headers[InertiaHeaders.Redirect] }
                .Select(value => value.ToString())
                .FirstOrDefault(value => value.Length > 0);
            if (location is not null)
            {
                writer.WriteString("redirectLocation", location);
            }

            if (Header(request, "X-Inertia-Devtools-Visit") is { } visit)
            {
                writer.WriteString("visitId", visit);
            }

            writer.WriteEndObject();

            writer.WriteStartObject("http");
            WriteHeaders(writer, "requestHeaders", request.Headers);
            WriteHeaders(writer, "responseHeaders", response.Headers);
            WriteBody(writer, "requestBody", requestBody);
            WriteBody(writer, "responseBody", page is not null ? Body.Json(page.DeepClone())
                : IsRedirectOrEmpty(response) ? Body.Empty
                : Body.Omitted("non-inertia-response"));
            writer.WriteEndObject();

            WriteProps(writer, page);

            writer.WriteStartObject("route");
            var endpoint = httpContext.GetEndpoint();
            writer.WriteString("uri", (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "");
            WriteStringOrNull(writer, "name", endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
                ?? endpoint?.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName);
            WriteStringOrNull(writer, "action", endpoint?.DisplayName);
            writer.WriteEndObject();

            writer.WriteNull("renderSource");
            WriteStringOrNull(writer, "componentPath", capture.Component is { } component ? _pages.Find(component) : null);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static bool IsRedirectOrEmpty(HttpResponse response) =>
        response.StatusCode is 301 or 302 or 303 or 307 or 308 or 409 or 204 || response.ContentLength == 0;

    // props: metadata per dotted path, from the page object's own lists; propValues: the top-level values, redacted.
    private void WriteProps(Utf8JsonWriter writer, JsonObject? page)
    {
        var props = page?["props"] as JsonObject;
        var meta = new Dictionary<string, Dictionary<string, JsonNode?>>(StringComparer.Ordinal);
        Dictionary<string, JsonNode?> At(string path) => meta.TryGetValue(path, out var entry) ? entry : meta[path] = [];

        foreach (var (key, _) in props ?? [])
        {
            At(key);
        }

        foreach (var shared in Strings(page?["sharedProps"]))
        {
            At(shared)["shared"] = true;
        }

        if (page?["deferredProps"] is JsonObject deferred)
        {
            foreach (var (group, paths) in deferred)
            {
                foreach (var path in Strings(paths))
                {
                    var entry = At(path);
                    entry["inertiaType"] = "defer";
                    entry["deferGroup"] = group;
                }
            }
        }

        foreach (var (list, direction) in new[] { ("mergeProps", "append"), ("prependProps", "prepend") })
        {
            foreach (var path in Strings(page?[list]))
            {
                var entry = At(path);
                entry.TryAdd("inertiaType", "merge");
                entry["mergeDirection"] = direction;
            }
        }

        foreach (var path in Strings(page?["deepMergeProps"]))
        {
            var entry = At(path);
            entry.TryAdd("inertiaType", "merge");
            entry["deepMerge"] = true;
        }

        if (page?["scrollProps"] is JsonObject scroll)
        {
            foreach (var (path, value) in scroll)
            {
                var entry = At(path);
                entry["inertiaType"] = "scroll";
                if (value?["reset"] is JsonValue reset && reset.TryGetValue<bool>(out var isReset) && isReset)
                {
                    entry["reset"] = true;
                }
            }
        }

        if (page?["onceProps"] is JsonObject once)
        {
            foreach (var (key, value) in once)
            {
                if (value?["prop"]?.GetValue<string>() is { } path)
                {
                    var entry = At(path);
                    entry.TryAdd("inertiaType", "once");
                    entry["once"] = key;
                }
            }
        }

        foreach (var path in Strings(page?["rescuedProps"]))
        {
            At(path)["rescued"] = true;
        }

        writer.WriteStartObject("props");
        foreach (var (path, entry) in meta)
        {
            writer.WritePropertyName(path);
            writer.WriteStartObject();
            foreach (var (name, value) in entry)
            {
                writer.WritePropertyName(name);
                WriteNode(writer, value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();

        writer.WriteStartObject("propValues");
        foreach (var (key, value) in props ?? [])
        {
            writer.WritePropertyName(key);
            WriteNode(writer, IsSensitive(key) ? JsonValue.Create(Redacted) : Redact(value?.DeepClone()));
        }

        writer.WriteEndObject();

        static IEnumerable<string> Strings(JsonNode? node) =>
            node is JsonArray array ? array.Select(item => item?.GetValue<string>()).OfType<string>() : [];
    }

    private void WriteHeaders(Utf8JsonWriter writer, string name, IHeaderDictionary headers)
    {
        writer.WriteStartObject(name);
        foreach (var (header, value) in headers)
        {
            writer.WriteString(header.ToLowerInvariant(), IsSensitive(header) ? Redacted : value.ToString());
        }

        writer.WriteEndObject();
    }

    private void WriteBody(Utf8JsonWriter writer, string name, Body body)
    {
        writer.WriteStartObject(name);
        writer.WriteString("status", body.Status);
        if (body.Reason is { } reason)
        {
            writer.WriteString("reason", reason);
        }

        if (body.Status == "present")
        {
            writer.WritePropertyName("value");
            if (body.Value is { } text)
            {
                writer.WriteStringValue(text);
            }
            else
            {
                WriteNode(writer, Redact(body.Node));
            }
        }

        writer.WriteEndObject();
    }

    // Replaces the values of sensitive keys anywhere in the tree, in place.
    private JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsSensitive(key))
                    {
                        obj[key] = Redacted;
                    }
                    else
                    {
                        Redact(obj[key]);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Redact(item);
                }

                break;
        }

        return node;
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        if (node is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            node.WriteTo(writer);
        }
    }

    private static void WriteStringOrNull(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inertia DevTools could not record the request to {Path}.")]
    private static partial void LogRecordingFailed(ILogger logger, Exception exception, PathString path);

    private sealed record Stored(string Id, string Tab, string? Component, string RequestType, DateTimeOffset At, byte[] Json);

    private readonly record struct Body(string Status, string? Reason = null, string? Value = null, JsonNode? Node = null)
    {
        public static Body Empty => new("empty");

        public static Body Omitted(string reason) => new("omitted", reason);

        public static Body Text(string text) => new("present", Value: text);

        public static Body Json(JsonNode? node) => new("present", Node: node);
    }
}
