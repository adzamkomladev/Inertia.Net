using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>
/// Renders the Vite tags for the entry points: the manifest-driven tags in production, or the dev server tags
/// while the Vite dev server runs (hot file present, or <see cref="ViteOptions.DevServerUrl"/> set).
/// The manifest is parsed once and the production tags are cached; in Development both reload when the file changes.
/// </summary>
public sealed class ViteAssets
{
    private const int MaxCachedTagSets = 256;

    private readonly ViteOptions _options;
    private readonly string _contentRoot;
    private readonly bool _development;
    private readonly FileCache<ManifestState> _manifests;
    private readonly FileCache<string> _hotFiles;

    /// <summary>Creates the service; registered as a singleton by <c>AddInertia</c>.</summary>
    public ViteAssets(IOptions<InertiaOptions> options, IHostEnvironment environment, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options.Value.Vite;
        _contentRoot = environment.ContentRootPath;
        _development = environment.IsDevelopment();
        _manifests = new FileCache<ManifestState>(ManifestState.Load, _development, timeProvider);
        _hotFiles = new FileCache<string>(ReadHotFile, _development, timeProvider);
    }

    /// <summary>True while the Vite dev server is considered running: <see cref="ViteOptions.DevServerUrl"/> is set, or (in Development only) the hot file exists.</summary>
    public bool IsDevServerRunning => DevOrigin() is not null;

    internal string? DevServerOrigin => DevOrigin();

    /// <summary>Renders the tags for the entry points, e.g. <c>"src/app.tsx"</c> (the manifest key, relative to the Vite root).</summary>
    /// <exception cref="InvalidOperationException">An entry is not in the manifest, or there is no manifest and no dev server.</exception>
    public string RenderTags(HttpContext context, params string[] entries) => Encoding.UTF8.GetString(RenderTagsUtf8(context, entries));

    /// <summary>The React Fast Refresh preamble while the dev server runs; empty otherwise. Must come before the entry scripts.</summary>
    public string RenderReactRefresh(HttpContext context) => Encoding.UTF8.GetString(RenderReactRefreshUtf8(context));

    internal byte[] RenderTagsUtf8(HttpContext context, string[] entries)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Length == 0)
        {
            throw new ArgumentException("At least one Vite entry is required.", nameof(entries));
        }

        if (DevOrigin() is { } origin)
        {
            return Encoding.UTF8.GetBytes(RenderDev(origin, entries));
        }

        var manifest = LoadManifest();
        var pathBase = context.Request.PathBase.ToUriComponent();
        var key = (string.Join('\n', entries), pathBase);
        if (manifest.Rendered.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var prefix = _options.AssetBaseUrl?.TrimEnd('/') ?? $"{pathBase}/{_options.BuildDirectory.Trim('/')}";
        var bytes = Encoding.UTF8.GetBytes(RenderProduction(manifest, entries, prefix));
        if (manifest.Rendered.Count < MaxCachedTagSets)
        {
            manifest.Rendered[key] = bytes;
        }

        return bytes;
    }

    internal byte[] RenderReactRefreshUtf8(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return DevOrigin() is { } origin
            ? Encoding.UTF8.GetBytes($"<script type=\"module\">import RefreshRuntime from '{origin}/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$ = () => {{}};window.$RefreshSig$ = () => (type) => type;window.__vite_plugin_react_preamble_installed__ = true;</script>\n")
            : [];
    }

    private static string RenderDev(string origin, string[] entries)
    {
        var encodedOrigin = HtmlEncoder.Default.Encode(origin);
        var html = new StringBuilder($"<script type=\"module\" src=\"{encodedOrigin}/@vite/client\"></script>\n");
        foreach (var entry in entries)
        {
            AppendEntry(html, $"{encodedOrigin}/{HtmlEncoder.Default.Encode(entry.TrimStart('/'))}", entry);
        }

        return html.ToString();
    }

    private static string RenderProduction(ManifestState manifest, string[] entries, string prefix)
    {
        var html = new StringBuilder();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var encodedPrefix = HtmlEncoder.Default.Encode(prefix);
        string Url(string file) => $"{encodedPrefix}/{HtmlEncoder.Default.Encode(file)}";
        foreach (var entry in entries)
        {
            if (!manifest.Chunks.ContainsKey(entry))
            {
                var available = manifest.Chunks.Where(c => c.Value.IsEntry).Select(c => c.Key).ToList();
                if (available.Count == 0)
                {
                    available = [.. manifest.Chunks.Keys];
                }

                throw new InvalidOperationException($"Vite entry '{entry}' was not found in the manifest '{manifest.Path}'. Available entries: {string.Join(", ", available)}.");
            }

            // The entry first, then its static imports recursively; the visited set guards against cycles.
            var closure = new List<ViteManifestChunk>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string key)
            {
                if (visited.Add(key) && manifest.Chunks.TryGetValue(key, out var chunk))
                {
                    closure.Add(chunk);
                    foreach (var import in chunk.Imports ?? [])
                    {
                        Visit(import);
                    }
                }
            }

            Visit(entry);
            foreach (var css in closure.SelectMany(c => c.Css ?? []))
            {
                if (emitted.Add(css))
                {
                    html.Append("<link rel=\"stylesheet\" href=\"").Append(Url(css)).Append("\">\n");
                }
            }

            if (emitted.Add(closure[0].File))
            {
                AppendEntry(html, Url(closure[0].File), closure[0].File);
            }

            foreach (var import in closure.Skip(1))
            {
                if (!import.File.EndsWith(".css", StringComparison.Ordinal) && emitted.Add(import.File))
                {
                    html.Append("<link rel=\"modulepreload\" href=\"").Append(Url(import.File)).Append("\">\n");
                }
            }
        }

        return html.ToString();
    }

    private static void AppendEntry(StringBuilder html, string url, string file) =>
        html.Append(file.EndsWith(".css", StringComparison.Ordinal)
            ? $"<link rel=\"stylesheet\" href=\"{url}\">\n"
            : $"<script type=\"module\" src=\"{url}\"></script>\n");

    private ManifestState LoadManifest()
    {
        var buildRoot = Path.Combine(_contentRoot, _options.PublicDirectory, _options.BuildDirectory);
        string[] candidates = _options.ManifestPath is { } custom
            ? [Path.Combine(_contentRoot, custom)]
            : [Path.Combine(buildRoot, ".vite", "manifest.json"), Path.Combine(buildRoot, "manifest.json")];
        foreach (var path in candidates)
        {
            if (_manifests.Get(path) is { } manifest)
            {
                return manifest;
            }
        }

        throw new InvalidOperationException($"Vite manifest not found. Looked in: {string.Join(", ", candidates)}. Run the Vite build (e.g. 'npm run build'), start the dev server so the hot file exists, or set InertiaOptions.Vite.ManifestPath.");
    }

    private string? DevOrigin()
    {
        if (_options.DevServerUrl is { Length: > 0 } url)
        {
            return ValidateOrigin(url);
        }

        return _development
            ? _hotFiles.Get(Path.Combine(_contentRoot, _options.HotFilePath ?? Path.Combine(_options.PublicDirectory, "hot")))
            : null;
    }

    private static string? ReadHotFile(string path)
    {
        var text = File.ReadAllText(path).Trim();
        return text.Length == 0 ? null : ValidateOrigin(text);
    }

    // The origin is echoed into a script, so only plain http(s) URLs without quote or markup characters are accepted.
    private static string ValidateOrigin(string origin)
    {
        origin = origin.TrimEnd('/');
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || origin.AsSpan().IndexOfAny("'\"<>\\ ") >= 0)
        {
            throw new InvalidOperationException($"'{origin}' is not a valid Vite dev server URL. Expected something like http://localhost:5173.");
        }

        return origin;
    }

    private sealed class ManifestState
    {
        public required string Path { get; init; }

        public required Dictionary<string, ViteManifestChunk> Chunks { get; init; }

        /// <summary>Pre-rendered production tags per (entries, path base).</summary>
        public ConcurrentDictionary<(string Entries, string PathBase), byte[]> Rendered { get; } = new();

        public static ManifestState Load(string path)
        {
            using var stream = File.OpenRead(path);
            var chunks = JsonSerializer.Deserialize(stream, InertiaJsonContext.Default.DictionaryStringViteManifestChunk)
                ?? throw new InvalidOperationException($"Vite manifest '{path}' is empty.");
            return new ManifestState { Path = path, Chunks = chunks };
        }
    }
}

/// <summary>One entry of the Vite manifest.</summary>
internal sealed class ViteManifestChunk
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("src")]
    public string? Src { get; set; }

    [JsonPropertyName("css")]
    public string[]? Css { get; set; }

    [JsonPropertyName("assets")]
    public string[]? Assets { get; set; }

    [JsonPropertyName("isEntry")]
    public bool IsEntry { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("isDynamicEntry")]
    public bool IsDynamicEntry { get; set; }

    [JsonPropertyName("imports")]
    public string[]? Imports { get; set; }

    [JsonPropertyName("dynamicImports")]
    public string[]? DynamicImports { get; set; }
}
