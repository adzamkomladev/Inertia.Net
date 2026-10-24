using System.Collections.Concurrent;
using System.Security.Cryptography;
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

    // Marks where a nonce attribute goes in a cached tag set. Never produced by the encoded URLs around it.
    private const char NonceMarker = '\u0001';

    private readonly ViteOptions _options;
    private readonly Func<HttpContext, string?>? _nonce;
    private readonly string _contentRoot;
    private readonly bool _development;
    private readonly FileCache<ManifestState> _manifests;
    private readonly FileCache<string> _hotFiles;
    private readonly FileCache<string> _manifestHashes;
    private readonly string[] _manifestCandidates;

    /// <summary>Creates the service; registered as a singleton by <c>AddInertia</c>.</summary>
    public ViteAssets(IOptions<InertiaOptions> options, IHostEnvironment environment, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options.Value.Vite;
        _nonce = options.Value.CspNonce;
        _contentRoot = environment.ContentRootPath;
        _development = environment.IsDevelopment();
        _manifests = new FileCache<ManifestState>(ManifestState.Load, _development, timeProvider);
        _hotFiles = new FileCache<string>(ReadHotFile, _development, timeProvider);
        _manifestHashes = new FileCache<string>(HashFile, _development, timeProvider);
        var buildRoot = Path.Combine(_contentRoot, _options.PublicDirectory, _options.BuildDirectory);
        _manifestCandidates = _options.ManifestPath is { } custom
            ? [Path.Combine(_contentRoot, custom)]
            : [Path.Combine(buildRoot, ".vite", "manifest.json"), Path.Combine(buildRoot, "manifest.json")];
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
        ArgumentNullException.ThrowIfNull(entries);
        return RenderTagsUtf8(context, entries, string.Join('\n', entries));
    }

    /// <summary>The tags for <paramref name="entries"/>, given their cache key: the entries joined by <c>'\n'</c>.</summary>
    internal byte[] RenderTagsUtf8(HttpContext context, string[] entries, string key)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Length == 0)
        {
            throw new ArgumentException("At least one Vite entry is required.", nameof(entries));
        }

        var nonce = CspNonce.Resolve(_nonce, context);
        if (DevOrigin() is { } origin)
        {
            return Encoding.UTF8.GetBytes(RenderDev(origin, entries, CspNonce.Attribute(nonce)));
        }

        var manifest = LoadManifest();
        var pathBase = context.Request.PathBase.ToUriComponent();
        if (!manifest.Rendered.TryGetValue((key, pathBase), out var tags))
        {
            var prefix = _options.AssetBaseUrl?.TrimEnd('/') ?? $"{pathBase}/{_options.BuildDirectory.Trim('/')}";
            tags = TagSet.Create(RenderProduction(manifest, entries, prefix, _options), NonceMarker);
            if (manifest.Rendered.Count < MaxCachedTagSets)
            {
                manifest.Rendered[(key, pathBase)] = tags;
            }
        }

        return tags.With(nonce);
    }

    internal byte[] RenderReactRefreshUtf8(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return DevOrigin() is { } origin
            ? Encoding.UTF8.GetBytes($"<script type=\"module\"{CspNonce.Attribute(CspNonce.Resolve(_nonce, context))}>import RefreshRuntime from '{origin}/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$ = () => {{}};window.$RefreshSig$ = () => (type) => type;window.__vite_plugin_react_preamble_installed__ = true;</script>\n")
            : [];
    }

    private static string RenderDev(string origin, string[] entries, string nonce)
    {
        var encodedOrigin = HtmlEncoder.Default.Encode(origin);
        var html = new StringBuilder($"<script type=\"module\"{nonce} src=\"{encodedOrigin}/@vite/client\"></script>\n");
        foreach (var entry in entries)
        {
            AppendEntry(html, $"{encodedOrigin}/{HtmlEncoder.Default.Encode(entry.TrimStart('/'))}", entry, nonce);
        }

        return html.ToString();
    }

    // The tags, with a NonceMarker wherever a nonce attribute goes.
    private static string RenderProduction(ManifestState manifest, string[] entries, string prefix, ViteOptions options)
    {
        var html = new StringBuilder();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var encodedPrefix = HtmlEncoder.Default.Encode(prefix);
        var marker = NonceMarker.ToString();
        var loaded = new List<ViteManifestChunk>();
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
            loaded.AddRange(closure);
            foreach (var css in closure.SelectMany(c => c.Css ?? []))
            {
                if (emitted.Add(css))
                {
                    html.Append("<link rel=\"stylesheet\"").Append(marker).Append(" href=\"").Append(Url(css)).Append("\">\n");
                }
            }

            if (emitted.Add(closure[0].File))
            {
                AppendEntry(html, Url(closure[0].File), closure[0].File, marker);
            }

            foreach (var import in closure.Skip(1))
            {
                if (!import.File.EndsWith(".css", StringComparison.Ordinal) && emitted.Add(import.File))
                {
                    html.Append("<link rel=\"modulepreload\"").Append(marker).Append(" href=\"").Append(Url(import.File)).Append("\">\n");
                }
            }
        }

        if (options.Prefetch != VitePrefetch.None && PrefetchFiles(manifest, loaded, emitted) is { Count: > 0 } files)
        {
            var concurrency = options.Prefetch == VitePrefetch.Aggressive ? files.Count : options.PrefetchConcurrency;
            AppendPrefetch(html, [.. files.Select(file => $"{prefix}/{file}")], concurrency, marker);
        }

        return html.ToString();
    }

    // The chunks the loaded ones can import later (dynamic imports, then their static and dynamic imports and CSS), minus what the
    // tags already load, in breadth-first order so the nearest pages come first.
    private static List<string> PrefetchFiles(ManifestState manifest, List<ViteManifestChunk> loaded, HashSet<string> emitted)
    {
        var files = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(loaded.SelectMany(c => c.DynamicImports ?? []));
        while (pending.TryDequeue(out var key))
        {
            if (!visited.Add(key) || !manifest.Chunks.TryGetValue(key, out var chunk))
            {
                continue;
            }

            if (emitted.Add(chunk.File))
            {
                files.Add(chunk.File);
            }

            foreach (var css in chunk.Css ?? [])
            {
                if (emitted.Add(css))
                {
                    files.Add(css);
                }
            }

            foreach (var next in (chunk.Imports ?? []).Concat(chunk.DynamicImports ?? []))
            {
                pending.Enqueue(next);
            }
        }

        return files;
    }

    // After the load event, appends low-priority <link rel="prefetch"> elements, `concurrency` at a time: each finished or failed
    // download starts the next. The links carry the script's nonce, for policies that govern prefetches too.
    private static void AppendPrefetch(StringBuilder html, string[] urls, int concurrency, string marker)
    {
        // A JSON array with the default encoder: '<', '>', '&' and quotes are escaped, so it cannot close the script element.
        var list = JsonSerializer.Serialize(urls, InertiaJsonContext.Default.StringArray);
        html.Append("<script").Append(marker).Append(">(()=>{const n=document.currentScript?.nonce,q=").Append(list)
            .Append(";const next=()=>{const h=q.shift();if(!h)return;const l=document.createElement('link');l.rel='prefetch';l.href=h;")
            .Append("l.fetchPriority='low';if(n)l.nonce=n;l.onload=l.onerror=next;document.head.append(l)};")
            .Append("addEventListener('load',()=>setTimeout(()=>{for(let i=0;i<")
            .Append(concurrency.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(";i++)next()}))})()</script>\n");
    }

    private static void AppendEntry(StringBuilder html, string url, string file, string nonce) =>
        html.Append(file.EndsWith(".css", StringComparison.Ordinal)
            ? $"<link rel=\"stylesheet\"{nonce} href=\"{url}\">\n"
            : $"<script type=\"module\"{nonce} src=\"{url}\"></script>\n");

    /// <summary>The asset version derived from the manifest: the first 16 bytes (hex) of its SHA-256, or <c>""</c> when there is no manifest.</summary>
    internal string ManifestHash()
    {
        foreach (var path in _manifestCandidates)
        {
            if (_manifestHashes.Get(path) is { } hash)
            {
                return hash;
            }
        }

        return "";
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(stream, hash);
        return Convert.ToHexStringLower(hash[..16]);
    }

    private ManifestState LoadManifest()
    {
        foreach (var path in _manifestCandidates)
        {
            if (_manifests.Get(path) is { } manifest)
            {
                return manifest;
            }
        }

        throw new InvalidOperationException($"Vite manifest not found. Looked in: {string.Join(", ", _manifestCandidates)}. Run the Vite build (e.g. 'npm run build'), start the dev server so the hot file exists, or set InertiaOptions.Vite.ManifestPath.");
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
        public ConcurrentDictionary<(string Entries, string PathBase), TagSet> Rendered { get; } = new();

        public static ManifestState Load(string path)
        {
            using var stream = File.OpenRead(path);
            var chunks = JsonSerializer.Deserialize(stream, InertiaJsonContext.Default.DictionaryStringViteManifestChunk)
                ?? throw new InvalidOperationException($"Vite manifest '{path}' is empty.");
            return new ManifestState { Path = path, Chunks = chunks };
        }
    }
}

/// <summary>Pre-rendered tags: the bytes without a nonce, and the pieces between the places a nonce attribute goes.</summary>
internal sealed class TagSet
{
    private readonly byte[] _plain;
    private readonly byte[][] _parts;

    private TagSet(byte[] plain, byte[][] parts)
    {
        _plain = plain;
        _parts = parts;
    }

    public static TagSet Create(string html, char marker) => new(
        Encoding.UTF8.GetBytes(html.Replace(marker.ToString(), "", StringComparison.Ordinal)),
        [.. html.Split(marker).Select(Encoding.UTF8.GetBytes)]);

    /// <summary>The tags, with <c>nonce="..."</c> where it goes when <paramref name="nonce"/> (already checked) is set.</summary>
    public byte[] With(string? nonce)
    {
        if (nonce is null || _parts.Length == 1)
        {
            return _plain;
        }

        var attribute = Encoding.ASCII.GetBytes(CspNonce.Attribute(nonce));
        var result = new byte[_plain.Length + (attribute.Length * (_parts.Length - 1))];
        var written = 0;
        for (var i = 0; i < _parts.Length; i++)
        {
            if (i > 0)
            {
                attribute.CopyTo(result, written);
                written += attribute.Length;
            }

            _parts[i].CopyTo(result, written);
            written += _parts[i].Length;
        }

        return result;
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
