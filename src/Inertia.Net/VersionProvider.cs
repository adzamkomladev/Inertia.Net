using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>
/// The asset version: <see cref="InertiaOptions.VersionResolver"/>, else <see cref="InertiaOptions.Version"/>, else the hash of the
/// Vite manifest (<see cref="ViteAssets.ManifestHash"/>: computed once, re-checked on change in Development), else <c>""</c>.
/// </summary>
internal sealed class VersionProvider(IOptions<InertiaOptions> options, ViteAssets vite)
{
    private readonly InertiaOptions _options = options.Value;

    /// <summary>The version for <paramref name="httpContext"/>.</summary>
    public string GetVersion(HttpContext httpContext) =>
        _options.VersionResolver?.Invoke(httpContext) ?? _options.Version ?? vite.ManifestHash();

    /// <summary>Whether the request's <c>X-Inertia-Version</c> (absent = <c>""</c>) equals the current version. Ordinal, no allocation.</summary>
    public bool Matches(HttpContext httpContext)
    {
        var header = httpContext.Request.Headers[InertiaHeaders.Version];
        var version = GetVersion(httpContext);
        return header.Count switch
        {
            0 => version.Length == 0,
            1 => string.Equals(header[0] ?? "", version, StringComparison.Ordinal),
            _ => false,
        };
    }
}
