using Microsoft.AspNetCore.Http;

namespace Inertia.Net;

/// <summary>Inertia.Net configuration, set with <see cref="InertiaServiceCollectionExtensions.AddInertia"/>.</summary>
public sealed class InertiaOptions
{
    internal Dictionary<string, object?> SharedProps { get; } = new(StringComparer.Ordinal);

    /// <summary>The root view rendered on the first (non-Inertia) visit, relative to the content root. Default <c>app.html</c>.</summary>
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

    /// <summary>Prop resolution settings.</summary>
    public InertiaPropsOptions Props { get; } = new();

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

/// <summary>How a page uses server-side rendering.</summary>
public enum SsrMode
{
    /// <summary>The page is rendered on the client only.</summary>
    Disabled,

    /// <summary>The page is server-side rendered when possible and falls back to client-side rendering on any SSR failure.</summary>
    Preferred,

    /// <summary>
    /// The page must be server-side rendered: any SSR failure (including a skipped render, e.g. an open circuit or a missing bundle)
    /// throws <see cref="InertiaSsrException"/> before the first byte of the response is written.
    /// </summary>
    Required,
}

/// <summary>Server-side rendering settings. Read once at startup; changes after the app starts are ignored.</summary>
public sealed class SsrOptions
{
    /// <summary>The default SSR server URL, used when <see cref="Urls"/> is empty.</summary>
    public const string DefaultUrl = "http://127.0.0.1:13714";

    /// <summary>
    /// Whether first visits are server-side rendered. Default <see cref="SsrMode.Disabled"/>. <see cref="SsrMode.Disabled"/> is a kill
    /// switch: a per-response <see cref="InertiaResult.WithSsr"/> cannot turn SSR back on.
    /// </summary>
    public SsrMode Mode { get; set; }

    /// <summary>
    /// The SSR server base URLs. Empty (the default) means <see cref="DefaultUrl"/>. With several URLs each render goes to the server with
    /// the fewest renders in flight whose circuit is closed. Not used with <see cref="UseNodeProcess"/>, whose instances listen on
    /// <see cref="SsrNodeProcessOptions.BasePort"/> and up.
    /// </summary>
    public IList<string> Urls { get; } = [];

    /// <summary>
    /// The total render deadline, covering the wait for a free render slot and the HTTP call. Default 5 seconds;
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> disables it.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum SSR response body size in bytes, including error responses. Default 16 MiB. Must be positive.</summary>
    public long MaxResponseBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Pages whose JSON is larger than this many bytes are rendered on the client instead of being sent to the SSR server. Default 2 MiB.</summary>
    public long MaxPageBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// If set (relative to the content root, or absolute), SSR is skipped while this file does not exist, unless the Vite dev server is running.
    /// Point it at the built SSR bundle, e.g. <c>bootstrap/ssr/ssr.mjs</c>.
    /// </summary>
    public string? BundlePath { get; set; }

    /// <summary>
    /// Request paths (<c>Request.Path</c>, without PathBase) that are never server-side rendered, unless the response sets its own mode with
    /// <see cref="InertiaResult.WithSsr"/>. Exact match, or a trailing <c>*</c> for a prefix match, e.g. <c>/admin/*</c>.
    /// </summary>
    public IList<string> ExcludePaths { get; } = [];

    /// <summary>Limits on concurrent renders per SSR server.</summary>
    public SsrConcurrencyOptions Concurrency { get; } = new();

    /// <summary>Stops sending renders to an SSR server that keeps failing, and probes it again after a pause.</summary>
    public SsrCircuitBreakerOptions CircuitBreaker { get; } = new();

    /// <summary>The managed Node SSR process settings; null (the default) when the SSR servers run on their own. Set by <see cref="UseNodeProcess"/>.</summary>
    public SsrNodeProcessOptions? NodeProcess { get; private set; }

    /// <summary>
    /// Has the app start and supervise <c>node {bundle}</c> itself (restarted on crash or when it stops answering, stopped with the app),
    /// and sets <see cref="Mode"/> to <see cref="SsrMode.Preferred"/> when it is <see cref="SsrMode.Disabled"/>.
    /// Nothing is spawned while the Vite dev server runs: SSR then goes through the dev server.
    /// Occupied ports prevent startup. Shutdown stops the owned process trees.
    /// </summary>
    public SsrOptions UseNodeProcess(Action<SsrNodeProcessOptions>? configure = null)
    {
        if (Mode == SsrMode.Disabled)
        {
            Mode = SsrMode.Preferred;
        }

        NodeProcess ??= new();
        configure?.Invoke(NodeProcess);
        return this;
    }
}

/// <summary>Limits on concurrent SSR renders, per SSR server and per app instance.</summary>
public sealed class SsrConcurrencyOptions
{
    /// <summary>Renders in flight per SSR server. Default 8; 0 disables the limit (and the queue).</summary>
    public int MaxActive { get; set; } = 8;

    /// <summary>Renders waiting for a slot per SSR server; further renders fall back to client-side rendering at once. Default 32.</summary>
    public int MaxQueued { get; set; } = 32;

    /// <summary>
    /// How long a render waits for a slot before falling back to client-side rendering (within <see cref="SsrOptions.Timeout"/>).
    /// Default 250 ms.
    /// </summary>
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>
/// Circuit breaker settings. Only failures that affect every page open the circuit (connection errors, timeouts, unstructured HTTP
/// errors, malformed responses); a component's own render error, an oversized response, overload and client aborts never do.
/// </summary>
public sealed class SsrCircuitBreakerOptions
{
    /// <summary>Whether the circuit breaker is used. Default true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Consecutive failures that open the circuit. Default 5.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>How long an open circuit skips the server before one render probes it again. Default 10 seconds.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Settings for the Node SSR processes that the app starts and supervises (<see cref="SsrOptions.UseNodeProcess"/>).</summary>
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

    /// <summary>Extra environment variables for the processes (they inherit the app's environment).</summary>
    public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>();

    /// <summary>The working directory. Default: the content root.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>How long app startup waits for every instance to answer <c>GET /health</c>. Default 10 seconds.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Restarts a process when it exits unexpectedly or stops answering, after 1 s, doubling up to 30 s. Default true.</summary>
    public bool RestartOnExit { get; set; } = true;

    /// <summary>
    /// The number of Node processes to run. Default 1. Instance <c>i</c> listens on <see cref="BasePort"/> + <c>i</c> and gets that port in
    /// <c>INERTIA_SSR_PORT</c>; with more than one instance the SSR entry must listen on that variable.
    /// </summary>
    public int Instances { get; set; } = 1;

    /// <summary>The port of the first instance, on 127.0.0.1. Default 13714 (the Inertia SSR server default).</summary>
    public int BasePort { get; set; } = 13714;

    /// <summary>How often a running instance is checked with <c>GET /health</c>. Default 5 seconds.</summary>
    public TimeSpan LivenessInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Consecutive failed liveness checks after which an instance is killed and restarted. Default 3.</summary>
    public int LivenessFailureThreshold { get; set; } = 3;

    /// <summary>
    /// Keeps an instance that also answers on a non-loopback address. Default false: such an instance is stopped, because the Inertia
    /// SSR server serves unauthenticated <c>/render</c> and <c>/shutdown</c> routes. Bind it to loopback instead with
    /// <c>inertia({ ssr: { host: '127.0.0.1' } })</c> in the Vite config.
    /// </summary>
    public bool AllowNetworkExposure { get; set; }
}

/// <summary>Settings for the store that carries flash data, errors and history flags across redirects.</summary>
public sealed class InertiaStateOptions
{
    /// <summary>The cookie name (cookie store) or session key (session store). Default <c>.Inertia.State</c>.</summary>
    public string CookieName { get; set; } = ".Inertia.State";

    /// <summary>
    /// When the state cookie (cookie store) is marked <c>Secure</c>. Default <see cref="CookieSecurePolicy.SameAsRequest"/>; use
    /// <see cref="CookieSecurePolicy.Always"/> behind a TLS-terminating proxy that does not forward the scheme.
    /// </summary>
    public CookieSecurePolicy CookieSecurePolicy { get; set; } = CookieSecurePolicy.SameAsRequest;

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

/// <summary>Prop resolution settings.</summary>
public sealed class InertiaPropsOptions
{
    /// <summary>
    /// How many <see cref="InertiaProp.Parallel"/> loaders of one render may run at the same time. Default 4; must be positive.
    /// Props that are not marked parallel are resolved one after another.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;
}
