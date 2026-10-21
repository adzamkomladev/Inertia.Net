using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Settings for <see cref="InertiaAntiforgeryApplicationBuilderExtensions.UseInertiaAntiforgery"/>.</summary>
public sealed class InertiaAntiforgeryOptions
{
    /// <summary>The script-readable cookie the client copies into <c>X-XSRF-TOKEN</c>. Default <c>XSRF-TOKEN</c>.</summary>
    public string CookieName { get; set; } = "XSRF-TOKEN";

    /// <summary>
    /// When the token cookie is marked <c>Secure</c>. Default <see cref="CookieSecurePolicy.SameAsRequest"/>; use
    /// <see cref="CookieSecurePolicy.Always"/> behind a TLS-terminating proxy that does not forward the scheme.
    /// </summary>
    public CookieSecurePolicy SecurePolicy { get; set; } = CookieSecurePolicy.SameAsRequest;

    /// <summary>
    /// Whether an unsafe request (not GET, HEAD, OPTIONS or TRACE) is validated. Null (the default) validates every unsafe
    /// request except endpoints that opt out with <c>DisableAntiforgery()</c> (antiforgery metadata with
    /// <c>RequiresValidation = false</c>). Use it to exempt, for example, token-authenticated API routes.
    /// </summary>
    public Func<HttpContext, bool>? ShouldValidate { get; set; }

    /// <summary>
    /// The response for a request that fails validation. Null (the default) answers <c>419</c> (page expired). Return
    /// <c>Back().WithFlash(...)</c> to send the user back with a message instead (needs <c>app.UseInertia()</c> to run first).
    /// </summary>
    public Func<HttpContext, AntiforgeryValidationException, IResult>? OnRejected { get; set; }

    /// <summary>
    /// When page loads write the token cookie. Default <see cref="XsrfCookieRefresh.Always"/>. With <see cref="XsrfCookieRefresh.Lazy"/>
    /// a page load that already carries a valid token keeps it, so the response has no <c>Set-Cookie</c> and no
    /// <c>Cache-Control: no-cache, no-store</c> from antiforgery.
    /// </summary>
    public XsrfCookieRefresh CookieRefresh { get; set; }
}

/// <summary>When <see cref="InertiaAntiforgeryApplicationBuilderExtensions.UseInertiaAntiforgery"/> writes the token cookie.</summary>
public enum XsrfCookieRefresh
{
    /// <summary>On every page load (GET or HEAD that is an Inertia request or accepts HTML), like Laravel.</summary>
    Always,

    /// <summary>
    /// Only when the request has no token cookie, or its token no longer validates (for example after sign-in or sign-out, or a new
    /// antiforgery cookie). Responses that keep the token are left cacheable: antiforgery marks every response it issues a token on with
    /// <c>Cache-Control: no-cache, no-store</c>, which also keeps such pages out of the browser's back/forward cache. Set your own
    /// <c>Cache-Control</c> on pages with private data. Each page load validates the token instead of issuing one, which costs about the
    /// same; the gain is in the response, not in CPU.
    /// </summary>
    Lazy,
}

/// <summary>Antiforgery support for the Inertia client, which reads <c>XSRF-TOKEN</c> and sends it back as <c>X-XSRF-TOKEN</c>.</summary>
public static class InertiaAntiforgeryApplicationBuilderExtensions
{
    /// <summary>The status code of a rejected request: Laravel's "page expired".</summary>
    internal const int PageExpired = 419;

    /// <summary>
    /// On page loads (GET/HEAD requests that are Inertia requests or accept HTML), writes the request token to a script-readable
    /// cookie (default <c>XSRF-TOKEN</c>; HttpOnly off, SameSite=Lax, Secure on HTTPS, Path=/, essential). It only issues the token:
    /// prefer <see cref="UseInertiaAntiforgery"/>, which also validates it. Pair this one with
    /// <c>services.AddAntiforgery(o =&gt; o.HeaderName = "X-XSRF-TOKEN")</c> and your own validation.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="cookieName">The cookie the client reads the token from.</param>
    public static IApplicationBuilder UseInertiaAntiforgeryCookie(this IApplicationBuilder app, string cookieName = "XSRF-TOKEN")
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrEmpty(cookieName);
        var options = new InertiaAntiforgeryOptions { CookieName = cookieName };
        var antiforgery = GetAntiforgery(app);
        return app.Use((httpContext, next) =>
        {
            IssueToken(antiforgery, options, httpContext);
            return next(httpContext);
        });
    }

    /// <summary>
    /// CSRF protection for Inertia apps in one call. Issues the token cookie on page loads like <see cref="UseInertiaAntiforgeryCookie"/>,
    /// and validates every unsafe request (JSON and form bodies alike) with <see cref="IAntiforgery.ValidateRequestAsync"/>,
    /// answering failures with <c>419</c> (see <see cref="InertiaAntiforgeryOptions.OnRejected"/>). Endpoints opt out with
    /// <c>DisableAntiforgery()</c>. It also satisfies form-binding endpoints, so <c>app.UseAntiforgery()</c> is not needed.
    /// Needs <c>services.AddAntiforgery(o =&gt; o.HeaderName = "X-XSRF-TOKEN")</c>. Call it after authentication (the token is
    /// bound to the signed-in user) and after <c>app.UseInertia()</c>.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configure">Changes the cookie, the secure policy, which requests are validated, or the rejection.</param>
    public static IApplicationBuilder UseInertiaAntiforgery(this IApplicationBuilder app, Action<InertiaAntiforgeryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = new InertiaAntiforgeryOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrEmpty(options.CookieName, nameof(options.CookieName));
        var antiforgery = GetAntiforgery(app);
        var headerName = app.ApplicationServices.GetService<IOptions<AntiforgeryOptions>>()?.Value.HeaderName;
        return app.Use(async (httpContext, next) =>
        {
            var method = httpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
            {
                if (options.CookieRefresh == XsrfCookieRefresh.Always || headerName is null || !await HoldsValidTokenAsync(antiforgery, options, headerName, httpContext))
                {
                    IssueToken(antiforgery, options, httpContext);
                }

                MarkHandled(httpContext);
            }
            else if (options.ShouldValidate?.Invoke(httpContext) ?? RequiresValidation(httpContext))
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(httpContext);
                    httpContext.Features.Set<IAntiforgeryValidationFeature>(ValidationFeature.Valid);
                    MarkHandled(httpContext);
                }
                catch (AntiforgeryValidationException ex)
                {
                    httpContext.Features.Set<IAntiforgeryValidationFeature>(new ValidationFeature(ex));
                    await (options.OnRejected?.Invoke(httpContext, ex) ?? Rejected).ExecuteAsync(httpContext);
                    return;
                }
            }

            await next(httpContext);
        });
    }

    private static readonly IResult Rejected = Results.Text("The page expired. Reload the page and try again.", "text/plain", statusCode: PageExpired);

    // The key ASP.NET Core's own UseAntiforgery() sets, which endpoint routing checks before running an endpoint that binds a form.
    // Only set when this middleware took care of the request: a request exempted by ShouldValidate is left to UseAntiforgery().
    private const string AntiforgeryMiddlewareInvokedKey = "__AntiforgeryMiddlewareWithEndpointInvoked";

    private static void MarkHandled(HttpContext httpContext)
    {
        if (httpContext.GetEndpoint() is not null)
        {
            httpContext.Items[AntiforgeryMiddlewareInvokedKey] = true;
        }
    }

    private static IAntiforgery GetAntiforgery(IApplicationBuilder app) =>
        app.ApplicationServices.GetService<IAntiforgery>()
        ?? throw new InvalidOperationException("Inertia antiforgery needs the antiforgery services. Call services.AddAntiforgery(o => o.HeaderName = \"X-XSRF-TOKEN\") at startup.");

    // Endpoints opt out with DisableAntiforgery(); the last antiforgery metadata wins, as in UseAntiforgery().
    private static bool RequiresValidation(HttpContext httpContext) =>
        httpContext.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>() is not { RequiresValidation: false };

    private static bool IsPageLoad(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && (!string.IsNullOrEmpty(request.Headers[InertiaHeaders.Inertia]) || request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase));

    // Lazy refresh: validates the token the client holds in the cookie as if it had sent it in the header, so a token issued for
    // another user or another antiforgery cookie is replaced. Requests that are not page loads count as holding one (nothing is issued).
    private static async ValueTask<bool> HoldsValidTokenAsync(IAntiforgery antiforgery, InertiaAntiforgeryOptions options, string headerName, HttpContext httpContext)
    {
        var request = httpContext.Request;
        if (!IsPageLoad(request))
        {
            return true;
        }

        if (!request.Cookies.TryGetValue(options.CookieName, out var token) || string.IsNullOrEmpty(token) || request.HasFormContentType)
        {
            return false;
        }

        var headers = request.Headers;
        var sent = headers[headerName];
        headers[headerName] = token;
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
        finally
        {
            headers[headerName] = sent;
        }
    }

    private static void IssueToken(IAntiforgery antiforgery, InertiaAntiforgeryOptions options, HttpContext httpContext)
    {
        var request = httpContext.Request;
        if (IsPageLoad(request))
        {
            var tokens = antiforgery.GetAndStoreTokens(httpContext);
            httpContext.Response.Cookies.Append(options.CookieName, tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
                Secure = InertiaCookies.IsSecure(options.SecurePolicy, request),
                Path = "/",
                // Like the antiforgery cookie itself: a consent policy must not withhold it, or every form submission fails.
                IsEssential = true,
            });
        }
    }

    private sealed class ValidationFeature(Exception? error) : IAntiforgeryValidationFeature
    {
        public static readonly ValidationFeature Valid = new(null);

        public bool IsValid => error is null;

        public Exception? Error => error;
    }
}

internal static class InertiaCookies
{
    public static bool IsSecure(CookieSecurePolicy policy, HttpRequest request) => policy switch
    {
        CookieSecurePolicy.Always => true,
        CookieSecurePolicy.None => false,
        _ => request.IsHttps,
    };
}
