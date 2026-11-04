using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Inertia.Net.Live.SignalR;

/// <summary>
/// The hub's cross-site check. Browsers send <c>Origin</c> on the negotiate request and the WebSocket handshake, and neither is covered by
/// CORS for cookie-authenticated connections, so a page on another site could otherwise subscribe with the user's cookies.
/// </summary>
internal sealed partial class LiveOrigin(ILogger<LiveOrigin> logger)
{
    private int _proxyWarningLogged;

    public bool IsAllowed(HttpRequest request, IList<string> allowedOrigins)
    {
        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
        {
            return true; // not a browser request, or a same-origin GET without Origin
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (SameOrigin(uri, Normalize(request.Scheme), request.Host))
        {
            return true;
        }

        foreach (var allowed in allowedOrigins)
        {
            if (Uri.TryCreate(allowed, UriKind.Absolute, out var allowedUri) && SameOrigin(uri, allowedUri))
            {
                return true;
            }
        }

        if (Normalize(uri.Scheme) == "https" && Normalize(request.Scheme) == "http"
            && string.Equals(uri.IdnHost, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && Interlocked.Exchange(ref _proxyWarningLogged, 1) == 0)
        {
            LogProxySchemeMismatch(logger);
        }

        return false;
    }

    [LoggerMessage(LogLevel.Warning, "A live-props connection was refused because its HTTPS Origin uses the request's host but the request scheme is HTTP. Behind a TLS-terminating proxy, configure UseForwardedHeaders() before mapping the hub; otherwise configure InertiaLiveSignalROptions.AllowedOrigins explicitly.")]
    private static partial void LogProxySchemeMismatch(ILogger logger);

    // ws and wss are the WebSocket forms of http and https.
    private static string Normalize(string scheme) => scheme.ToLowerInvariant() switch
    {
        "ws" => "http",
        "wss" => "https",
        var other => other,
    };

    private static bool SameOrigin(Uri origin, string scheme, HostString host)
    {
        if (!host.HasValue)
        {
            return false;
        }

        var port = host.Port ?? (scheme == "https" ? 443 : 80);
        return Normalize(origin.Scheme) == scheme
            && string.Equals(origin.IdnHost, host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == port;
    }

    private static bool SameOrigin(Uri origin, Uri allowed) =>
        Normalize(origin.Scheme) == Normalize(allowed.Scheme)
        && string.Equals(origin.IdnHost, allowed.IdnHost, StringComparison.OrdinalIgnoreCase)
        && origin.Port == allowed.Port;
}
