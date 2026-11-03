using Microsoft.AspNetCore.Http;

namespace Inertia.Net.Live.SignalR;

/// <summary>
/// The hub's cross-site check. Browsers send <c>Origin</c> on the negotiate request and the WebSocket handshake, and neither is covered by
/// CORS for cookie-authenticated connections, so a page on another site could otherwise subscribe with the user's cookies.
/// </summary>
internal static class LiveOrigin
{
    public static bool IsAllowed(HttpRequest request, IList<string> allowedOrigins)
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

        return false;
    }

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
