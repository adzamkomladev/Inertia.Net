using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net;

/// <summary>Antiforgery support for the Inertia client (axios), which reads <c>XSRF-TOKEN</c> and sends it back as <c>X-XSRF-TOKEN</c>.</summary>
public static class InertiaAntiforgeryApplicationBuilderExtensions
{
    /// <summary>
    /// On page loads (GET/HEAD requests that are Inertia requests or accept HTML), writes the request token to a script-readable
    /// cookie (default <c>XSRF-TOKEN</c>; HttpOnly off, SameSite=Lax, Secure on HTTPS, Path=/). Pair it with
    /// <c>services.AddAntiforgery(o =&gt; o.HeaderName = "X-XSRF-TOKEN")</c> and validate with <c>app.UseAntiforgery()</c> (form bodies)
    /// or <c>IAntiforgery.ValidateRequestAsync</c>. Call it after authentication, so the token is bound to the signed-in user.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="cookieName">The cookie the client reads the token from.</param>
    public static IApplicationBuilder UseInertiaAntiforgeryCookie(this IApplicationBuilder app, string cookieName = "XSRF-TOKEN")
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrEmpty(cookieName);
        var antiforgery = app.ApplicationServices.GetService<IAntiforgery>()
            ?? throw new InvalidOperationException("UseInertiaAntiforgeryCookie() needs the antiforgery services. Call services.AddAntiforgery(o => o.HeaderName = \"X-XSRF-TOKEN\") at startup.");
        return app.Use((httpContext, next) =>
        {
            var request = httpContext.Request;
            if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                && (!string.IsNullOrEmpty(request.Headers[InertiaHeaders.Inertia]) || request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase)))
            {
                var tokens = antiforgery.GetAndStoreTokens(httpContext);
                httpContext.Response.Cookies.Append(cookieName, tokens.RequestToken!, new CookieOptions
                {
                    HttpOnly = false,
                    SameSite = SameSiteMode.Lax,
                    Secure = request.IsHttps,
                    Path = "/",
                });
            }

            return next(httpContext);
        });
    }
}
