using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net;

/// <summary>
/// The Inertia protocol middleware (protocol §3):
/// <list type="number">
/// <item>An Inertia GET with a stale <c>X-Inertia-Version</c> gets a 409 with <c>X-Inertia-Location</c> before the handler runs; the stored state is kept.</item>
/// <item>The stored flash/errors/history flags are loaded into <see cref="InertiaFeature"/> (and consumed).</item>
/// <item>After the handler, an Inertia request answered with an empty 200 is redirected back.</item>
/// <item>When the response starts: <c>Vary: X-Inertia</c>; for Inertia requests a 302 becomes a 303 for PUT/PATCH/DELETE and a
/// redirect to a URL with a fragment becomes a 409 with <c>X-Inertia-Redirect</c> (except prefetches); when the response is a redirect,
/// the state set during the request plus the loaded state not rendered yet is saved, otherwise the loaded state is cleared (consumed).</item>
/// </list>
/// </summary>
internal sealed class InertiaMiddleware
{
    private readonly RequestDelegate _next;
    private readonly VersionProvider _versionProvider;
    private readonly IInertiaStateStore _store;
    private readonly InertiaPageWriter _pageWriter;
    private readonly Func<object, Task> _onStarting;
    private readonly DevToolsRecorder? _devTools;

    public InertiaMiddleware(RequestDelegate next, VersionProvider versionProvider, IInertiaStateStore store, InertiaPageWriter pageWriter, DevToolsRecorder? devTools = null)
    {
        _next = next;
        _devTools = devTools;
        _versionProvider = versionProvider;
        _store = store;
        _pageWriter = pageWriter;
        _onStarting = state => FinishAsync((HttpContext)state); // cached: no per-request closure
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var request = httpContext.Request;
        var response = httpContext.Response;
        var isInertia = IsInertia(request);

        if (isInertia && HttpMethods.IsGet(request.Method) && !_versionProvider.Matches(httpContext))
        {
            InertiaHeaders.AppendVary(response.Headers);
            response.StatusCode = StatusCodes.Status409Conflict;
            response.Headers[InertiaHeaders.Location] = request.GetEncodedUrl();
            response.Headers[InertiaHeaders.Version] = _versionProvider.GetVersion(httpContext);
            return;
        }

        if (await _store.LoadAsync(httpContext) is { } state)
        {
            var feature = httpContext.Inertia();
            feature.StateLoaded = true;
            InertiaStateSerializer.TryRestore(feature, state);
        }

        response.OnStarting(_onStarting, httpContext);

        var originalBody = httpContext.Features.Get<IHttpResponseBodyFeature>();
        var tracker = isInertia && originalBody is not null ? new ResponseWriteTracker(originalBody) : null;
        if (tracker is not null) httpContext.Features.Set<IHttpResponseBodyFeature>(tracker);
        try
        {
            await _next(httpContext);

            if (isInertia
                && response.StatusCode == StatusCodes.Status200OK
                && !response.HasStarted
                && tracker?.HasWritten != true
                && response.ContentLength is null or 0)
            {
                response.StatusCode = IsPutPatchDelete(request.Method) ? StatusCodes.Status303SeeOther : StatusCodes.Status302Found;
                response.Headers.Location = InertiaBackResult.GetBackUrl(request, "~/");
            }

            // Finish while outer middleware such as UseSession is still active.
            if (!response.HasStarted)
            {
                await FinishAsync(httpContext);
            }
        }
        finally
        {
            if (tracker is not null && ReferenceEquals(httpContext.Features.Get<IHttpResponseBodyFeature>(), tracker))
                httpContext.Features.Set(originalBody);
        }
    }

    /// <summary>The middleware with DevTools recording around it; only used while the recorder is on.</summary>
    public async Task InvokeRecordedAsync(HttpContext httpContext)
    {
        if (_devTools!.Begin(httpContext) is not { } capture)
        {
            await InvokeAsync(httpContext);
            return;
        }

        var failed = false;
        try
        {
            await InvokeAsync(httpContext);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            await _devTools.CompleteAsync(httpContext, capture, failed);
        }
    }

    private static bool IsInertia(HttpRequest request) => !string.IsNullOrEmpty(request.Headers[InertiaHeaders.Inertia]);

    private static bool IsPutPatchDelete(string method) => HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    // An absolute (or protocol-relative) http(s) URL whose scheme, host or port differs from the request's. Relative URLs are same-origin.
    internal static bool IsOtherOrigin(HttpRequest request, string location)
    {
        if (location.StartsWith("//", StringComparison.Ordinal))
        {
            location = request.Scheme + ":" + location;
        }

        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        // Host is bracketed for IPv6 like HostString.Host; IdnHost is the punycode form a Host header carries.
        var host = request.Host.Host;
        var requestPort = request.Host.Port ?? (request.IsHttps ? 443 : 80);
        return !string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) && !string.Equals(uri.IdnHost, host, StringComparison.OrdinalIgnoreCase))
            || uri.Port != requestPort;
    }

    // Symfony's Response::isRedirect() codes (minus 201): a 304 or 300 is not a redirect.
    private static bool IsRedirect(int statusCode) => statusCode is 301 or 302 or 303 or 307 or 308;

    // Idempotent: runs after the handler when the response has not started, and again (or only) when it starts.
    private async Task FinishAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        var headers = response.Headers;
        InertiaHeaders.AppendVary(headers);

        if (IsInertia(httpContext.Request))
        {
            if (response.StatusCode == StatusCodes.Status302Found && IsPutPatchDelete(httpContext.Request.Method))
            {
                response.StatusCode = StatusCodes.Status303SeeOther;
            }

            if (IsRedirect(response.StatusCode)
                && headers.Location.ToString() is { Length: > 0 } location
                && !string.Equals(httpContext.Request.Headers[InertiaHeaders.Purpose], "prefetch", StringComparison.OrdinalIgnoreCase))
            {
                // Another origin: XHR cannot follow it, so the client visits it in full (which keeps any fragment, too).
                if (_pageWriter.Options.ConvertExternalRedirects && IsOtherOrigin(httpContext.Request, location))
                {
                    response.StatusCode = StatusCodes.Status409Conflict;
                    headers.Location = default;
                    headers[InertiaHeaders.Location] = location;
                }
                else if (location.Contains('#', StringComparison.Ordinal))
                {
                    response.StatusCode = StatusCodes.Status409Conflict;
                    headers.Location = default;
                    headers[InertiaHeaders.Redirect] = location;
                }
            }
        }

        if (httpContext.Features.Get<InertiaFeature>() is not { StateFinished: false } feature)
        {
            return;
        }

        feature.StateFinished = true;

        // A redirect, or a 409 the client turns into a visit (Location()/fragment redirect), carries the state forward.
        var redirects = IsRedirect(response.StatusCode)
            || (response.StatusCode == StatusCodes.Status409Conflict
                && (headers.ContainsKey(InertiaHeaders.Location) || headers.ContainsKey(InertiaHeaders.Redirect)));
        if (redirects && feature.HasState)
        {
            await _store.SaveAsync(httpContext, InertiaStateSerializer.Serialize(feature, _pageWriter.SerializerOptions));
        }
        else if (feature.StateLoaded)
        {
            await _store.ClearAsync(httpContext);
        }
    }
}

/// <summary>Adds the Inertia middleware.</summary>
public static class InertiaApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the Inertia protocol middleware: asset version check, <c>Vary</c>, 303 and fragment redirects, empty-response
    /// redirects, and carrying flash data/errors/history flags across redirects. Call it before the endpoints
    /// (and after <c>UseSession()</c> when the session state store is used).
    /// </summary>
    public static IApplicationBuilder UseInertia(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var services = app.ApplicationServices;
        var versionProvider = services.GetService<VersionProvider>()
            ?? throw new InvalidOperationException("Inertia.Net services are not registered. Call services.AddInertia() at startup.");
        var store = services.GetRequiredService<IInertiaStateStore>();
        var pageWriter = services.GetRequiredService<InertiaPageWriter>();
        var devTools = services.GetService<DevToolsRecorder>() is { Enabled: true } recorder ? recorder : null;
        return app.Use(next =>
        {
            var middleware = new InertiaMiddleware(next, versionProvider, store, pageWriter, devTools);
            return devTools is null ? middleware.InvokeAsync : middleware.InvokeRecordedAsync;
        });
    }
}
