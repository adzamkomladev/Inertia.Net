using Microsoft.AspNetCore.Http;

namespace Inertia.Net;

internal static class PreviousUrlCookie
{
    internal static string? Read(HttpRequest request, InertiaStateOptions? options)
    {
        if (options?.RememberPreviousUrl != true) return null;
        var value = request.Cookies[options.PreviousUrlCookieName];
        return IsLocalPath(value) ? value : null;
    }

    private static bool IsLocalPath(string? value) => value is ['/', ..]
        && (value.Length == 1 || (value[1] != '/' && value[1] != '\\'))
        && !ContainsControl(value);

    private static bool ContainsControl(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character)) return true;
        }

        return false;
    }

    internal static void Store(HttpContext context, InertiaRequest request, InertiaStateOptions options, string component)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || request.IsPrefetch || request.IsPrecognition
            || string.Equals(request.PartialComponent, component, StringComparison.Ordinal)) return;

        var url = context.Request.PathBase.ToUriComponent() + context.Request.Path.ToUriComponent() + context.Request.QueryString.ToUriComponent();
        // Validate on write as well: unusual paths must never replace a usable back URL.
        if (!IsLocalPath(url) || string.Equals(context.Request.Cookies[options.PreviousUrlCookieName], url, StringComparison.Ordinal)) return;
        context.Response.Cookies.Append(options.PreviousUrlCookieName, url, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = InertiaCookies.IsSecure(options.CookieSecurePolicy, context.Request),
            Path = "/",
            IsEssential = true,
        });
    }
}
