using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Inertia.Net;

/// <summary>Cookie authentication for Inertia apps.</summary>
public static class InertiaCookieAuthenticationExtensions
{
    /// <summary>
    /// Makes cookie authentication redirect Inertia visits to the login, logout and return URLs as it does browser navigations.
    /// Every Inertia visit sends <c>X-Requested-With: XMLHttpRequest</c>, which the cookie handler otherwise answers with a 401 the
    /// client cannot follow. Access denied answers an Inertia visit with 403 when the mount's error pages render 403 (so the error
    /// component shows), and redirects to <c>AccessDeniedPath</c> otherwise. Other requests, including <c>fetch</c> calls and
    /// Precognition validation, keep the handlers set on <see cref="CookieAuthenticationOptions.Events"/>. Call it after assigning
    /// <c>Events</c>: <c>AddCookie(o =&gt; { o.Events = new MyEvents(); o.UseInertiaRedirects(); })</c>.
    /// </summary>
    /// <param name="options">The cookie options.</param>
    public static CookieAuthenticationOptions UseInertiaRedirects(this CookieAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var events = options.Events;
        events.OnRedirectToLogin = Wrap(events.OnRedirectToLogin, accessDenied: false);
        events.OnRedirectToLogout = Wrap(events.OnRedirectToLogout, accessDenied: false);
        events.OnRedirectToReturnUrl = Wrap(events.OnRedirectToReturnUrl, accessDenied: false);
        events.OnRedirectToAccessDenied = Wrap(events.OnRedirectToAccessDenied, accessDenied: true);
        return options;
    }

    private static Func<RedirectContext<CookieAuthenticationOptions>, Task> Wrap(Func<RedirectContext<CookieAuthenticationOptions>, Task> inner, bool accessDenied) => context =>
    {
        var http = context.HttpContext;
        if (string.IsNullOrEmpty(http.Request.Headers[InertiaHeaders.Inertia]) || InertiaPrecognition.IsPrecognitive(http.Request))
        {
            return inner(context);
        }

        if (accessDenied && InertiaErrorPages.Handles(http, StatusCodes.Status403Forbidden))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden; // the error pages render the 403 component
            return Task.CompletedTask;
        }

        // 303 makes the client follow a redirect after PUT, PATCH, DELETE (and POST) with a GET, as the Inertia protocol asks.
        context.Response.StatusCode = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
            ? StatusCodes.Status302Found
            : StatusCodes.Status303SeeOther;
        context.Response.Headers.Location = context.RedirectUri;
        return Task.CompletedTask;
    };
}

/// <summary>Rate limiter rejections for Inertia apps.</summary>
public static class InertiaRateLimiting
{
    /// <summary>
    /// A rejection handler for <see cref="RateLimiterOptions.OnRejected"/>: <c>options.OnRejected = InertiaRateLimiting.OnRejected();</c>.
    /// A Precognition validation request gets 429 with <c>Precognition: true</c>. An Inertia form submission (not GET) gets the 429
    /// error component when <c>AddInertiaErrorPages</c> renders 429 for the mount, and is otherwise redirected back with the flash entry
    /// <paramref name="flashKey"/> = <paramref name="message"/>. Any other request gets a plain 429. <c>Retry-After</c> is set whenever
    /// the limiter reports a retry time. Call <c>UseRateLimiter()</c> after <c>UseInertia()</c>, which saves the flash data.
    /// </summary>
    /// <param name="flashKey">The flash key of the message on a redirect back.</param>
    /// <param name="message">The flash message.</param>
    public static Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected(string flashKey = "error", string message = "Too many attempts. Try again later.")
    {
        ArgumentException.ThrowIfNullOrEmpty(flashKey);
        ArgumentNullException.ThrowIfNull(message);
        return async (context, _) =>
        {
            var http = context.HttpContext;
            var response = http.Response;
            response.StatusCode = StatusCodes.Status429TooManyRequests;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }

            if (InertiaPrecognition.IsPrecognitive(http.Request))
            {
                response.Headers[InertiaPrecognition.Header] = "true";
                response.Headers.Append("Vary", InertiaPrecognition.Header);
                return;
            }

            if (!string.IsNullOrEmpty(http.Request.Headers[InertiaHeaders.Inertia])
                && !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method)
                && !InertiaErrorPages.Handles(http, StatusCodes.Status429TooManyRequests))
            {
                await Inertia.Back().WithFlash(flashKey, message).ExecuteAsync(http);
            }
        };
    }
}
