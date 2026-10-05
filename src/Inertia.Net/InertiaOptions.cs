using Microsoft.AspNetCore.Http;

namespace Inertia.Net;

/// <summary>Inertia.Net configuration, set with <see cref="InertiaServiceCollectionExtensions.AddInertia"/>.</summary>
public sealed class InertiaOptions
{
    internal Dictionary<string, object?> SharedProps { get; } = new(StringComparer.Ordinal);

    /// <summary>The root view rendered on the first (non-Inertia) visit. Default <c>app.html</c>.</summary>
    public string RootView { get; set; } = "app.html";

    /// <summary>The id of the root element and the <c>data-page</c> script. Default <c>app</c>.</summary>
    public string RootElementId { get; set; } = "app";

    /// <summary>
    /// The asset version. Null means a hash of the Vite manifest (<c>""</c> when there is none),
    /// unless <see cref="VersionResolver"/> returns one.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>Resolves the asset version per request; wins over <see cref="Version"/> when it returns non-null.</summary>
    public Func<HttpContext, string?>? VersionResolver { get; set; }

    /// <summary>Encrypts the browser history state of every page.</summary>
    public bool EncryptHistory { get; set; }

    /// <summary>Sends every validation message per field instead of only the first.</summary>
    public bool WithAllErrors { get; set; }

    /// <summary>Emits integers beyond ±(2^53−1) as <c>{"$bigint":"..."}</c> and sets <c>preserveBigIntegers</c>.</summary>
    public bool PreserveBigIntegers { get; set; }

    /// <summary>Lists the top-level shared prop keys in <c>sharedProps</c>. Default true.</summary>
    public bool ExposeSharedPropKeys { get; set; } = true;

    /// <summary>Vite integration settings.</summary>
    public ViteOptions Vite { get; } = new();

    /// <summary>Server-side rendering settings.</summary>
    public SsrOptions Ssr { get; } = new();

    /// <summary>Where flash data, errors and history flags are kept across redirects.</summary>
    public InertiaStateOptions State { get; } = new();

    /// <summary>Shares a prop with every page. Page props with the same key win.</summary>
    public InertiaOptions Share(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        SharedProps[key] = value;
        return this;
    }

    /// <summary>Shares a prop computed per request. Page props with the same key win.</summary>
    public InertiaOptions Share(string key, Func<HttpContext, object?> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        SharedProps[key] = factory;
        return this;
    }
}

/// <summary>Vite integration settings.</summary>
public sealed class ViteOptions
{
    /// <summary>The web root that contains the build directory and the hot file. Default <c>wwwroot</c>.</summary>
    public string PublicDirectory { get; set; } = "wwwroot";

    /// <summary>The Vite build output directory, relative to <see cref="PublicDirectory"/>. Default <c>build</c>.</summary>
    public string BuildDirectory { get; set; } = "build";

    /// <summary>The manifest path, relative to the content root. Null means <c>{PublicDirectory}/{BuildDirectory}/.vite/manifest.json</c>.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>The file the Vite dev server writes its URL to, relative to the content root. Null means <c>{PublicDirectory}/hot</c>.</summary>
    public string? HotFilePath { get; set; }

    /// <summary>The Vite dev server origin, e.g. <c>http://localhost:5173</c>. When set, the dev server tags are always rendered and the hot file is not consulted.</summary>
    public string? DevServerUrl { get; set; }

    /// <summary>Replaces <c>{PathBase}/{BuildDirectory}</c> as the URL prefix of built assets, e.g. <c>https://cdn.example.com/build</c>.</summary>
    public string? AssetBaseUrl { get; set; }
}

/// <summary>Server-side rendering settings.</summary>
public sealed class SsrOptions
{
    /// <summary>Renders the first visit on the SSR server. Default false.</summary>
    public bool Enabled { get; set; }

    /// <summary>The SSR server base URL. Default <c>http://127.0.0.1:13714</c>.</summary>
    public string Url { get; set; } = "http://127.0.0.1:13714";

    /// <summary>How long to wait for the SSR server. Default 5 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Throws instead of falling back to client-side rendering when SSR fails.</summary>
    public bool ThrowOnError { get; set; }

    /// <summary>
    /// If set (relative to the content root, or absolute), SSR is skipped while this file does not exist, unless the Vite dev server is running.
    /// Point it at the built SSR bundle, e.g. <c>bootstrap/ssr/ssr.mjs</c>.
    /// </summary>
    public string? BundlePath { get; set; }

    /// <summary>Request paths (<c>Request.Path</c>, without PathBase) that are never server-side rendered. Exact match, or a trailing <c>*</c> for a prefix match, e.g. <c>/admin/*</c>.</summary>
    public IList<string> ExcludePaths { get; } = [];

    /// <summary>The managed Node SSR process settings; null (the default) when the SSR server runs on its own. Set by <see cref="UseNodeProcess"/>.</summary>
    public SsrNodeProcessOptions? NodeProcess { get; private set; }

    /// <summary>
    /// Enables SSR and has the app start and supervise <c>node {bundle}</c> itself (restarted on crash, stopped with the app).
    /// Nothing is spawned while the Vite dev server runs: SSR then goes through the dev server.
    /// </summary>
    public SsrOptions UseNodeProcess(Action<SsrNodeProcessOptions>? configure = null)
    {
        Enabled = true;
        NodeProcess ??= new();
        configure?.Invoke(NodeProcess);
        return this;
    }
}

/// <summary>Settings for the Node SSR process that the app starts and supervises (<see cref="SsrOptions.UseNodeProcess"/>).</summary>
public sealed class SsrNodeProcessOptions
{
    /// <summary>
    /// The SSR bundle, relative to the content root (or absolute). Default: <see cref="SsrOptions.BundlePath"/>, else the first that exists of
    /// <c>ssr/ssr.js</c>, <c>ssr/ssr.mjs</c>, <c>bootstrap/ssr/ssr.js</c>, <c>bootstrap/ssr/ssr.mjs</c>, <c>dist/ssr.js</c>, <c>dist/ssr.mjs</c>.
    /// </summary>
    public string? BundlePath { get; set; }

    /// <summary>The Node executable, a path or a name looked up on <c>PATH</c>. Default <c>node</c>.</summary>
    public string Executable { get; set; } = "node";

    /// <summary>Arguments passed before the bundle path, e.g. <c>--enable-source-maps</c>.</summary>
    public IList<string> Arguments { get; } = [];

    /// <summary>Extra environment variables for the process (it inherits the app's environment).</summary>
    public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>();

    /// <summary>The working directory. Default: the content root.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>How long app startup waits for <c>GET {Url}/health</c> to answer. Default 10 seconds.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Restarts the process when it exits unexpectedly, after 1 s, doubling up to 30 s. Default true.</summary>
    public bool RestartOnExit { get; set; } = true;
}

/// <summary>Settings for the store that carries flash data, errors and history flags across redirects.</summary>
public sealed class InertiaStateOptions
{
    /// <summary>The cookie name (cookie store) or session key (session store). Default <c>.Inertia.State</c>.</summary>
    public string CookieName { get; set; } = ".Inertia.State";

    internal bool UsesSession { get; private set; }

    /// <summary>
    /// Keeps the state in <c>ISession</c> instead of the default encrypted cookie. Needs <c>services.AddSession()</c> and
    /// <c>app.UseSession()</c> before <c>app.UseInertia()</c>.
    /// </summary>
    public InertiaStateOptions UseSession()
    {
        UsesSession = true;
        return this;
    }
}
