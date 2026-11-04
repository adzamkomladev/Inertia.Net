using Microsoft.Net.Http.Headers;

namespace Inertia.Net.Testing;

/// <summary>Helpers for sending the XSRF token from an Inertia page response with form requests.</summary>
public static class InertiaXsrfExtensions
{
    /// <summary>
    /// Reads and URL-decodes the token from the response's <c>Set-Cookie</c> headers. Pass the configured script-readable
    /// cookie name when it differs from <c>XSRF-TOKEN</c>. Throws when the response did not issue that cookie.
    /// </summary>
    public static string GetXsrfToken(this HttpResponseMessage response, string cookieName = "XSRF-TOKEN")
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrEmpty(cookieName);
        if (response.Headers.TryGetValues(HeaderNames.SetCookie, out var headers))
        {
            var cookie = SetCookieHeaderValue.ParseList(headers.ToList())
                .FirstOrDefault(cookie => string.Equals(cookie.Name.Value, cookieName, StringComparison.Ordinal));
            if (cookie is not null)
                return Uri.UnescapeDataString(HeaderUtilities.RemoveQuotes(cookie.Value).Value ?? "");
        }

        throw new InvalidOperationException($"The response did not issue the '{cookieName}' cookie. Load an Inertia page that issues an XSRF token first.");
    }

    /// <summary>
    /// Sets the token header, replacing any previous value. Pass the app's antiforgery header name when customized. The test
    /// client's cookie container must also send the paired antiforgery cookie; this helper leaves cookies unchanged.
    /// </summary>
    public static HttpRequestMessage WithXsrfToken(this HttpRequestMessage request, string token, string headerName = "X-XSRF-TOKEN")
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentException.ThrowIfNullOrEmpty(headerName);
        request.Headers.Remove(headerName);
        request.Headers.Add(headerName, token);
        return request;
    }

    /// <summary>
    /// Reads the token from a page response and sets its request header. Optional names must match the app's XSRF cookie and
    /// antiforgery header configuration. Keep the test client's paired antiforgery cookie when sending the request.
    /// </summary>
    public static HttpRequestMessage WithXsrfToken(this HttpRequestMessage request, HttpResponseMessage pageResponse,
        string cookieName = "XSRF-TOKEN", string headerName = "X-XSRF-TOKEN") =>
        request.WithXsrfToken(pageResponse.GetXsrfToken(cookieName), headerName);
}
