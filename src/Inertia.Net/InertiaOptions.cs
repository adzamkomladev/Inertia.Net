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

    /// <summary>The asset version. Null means <c>""</c> unless <see cref="VersionResolver"/> returns one.</summary>
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

    /// <summary>The manifest path. Null means <c>{PublicDirectory}/{BuildDirectory}/.vite/manifest.json</c>.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>The file the Vite dev server writes its URL to. Null means <c>{PublicDirectory}/hot</c>.</summary>
    public string? HotFilePath { get; set; }
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

    /// <summary>Request paths that are never server-side rendered.</summary>
    public IList<string> ExcludePaths { get; } = [];
}
