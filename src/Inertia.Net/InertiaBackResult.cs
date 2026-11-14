using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>
/// Redirects back to the <c>Referer</c> (only when it has the request's own host, so it cannot become an open redirect),
/// otherwise to the opt-in previous-URL cookie or the fallback. A 302; <c>UseInertia()</c> turns it into a 303 for PUT/PATCH/DELETE and carries the errors
/// and flash data to the next request.
/// </summary>
public sealed class InertiaBackResult : IResult, IStatusCodeHttpResult
{
    private List<(IDictionary<string, string[]> Errors, string Bag)>? _errors;
    private Dictionary<string, object?>? _flash;

    internal InertiaBackResult(string fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        Fallback = fallback;
    }

    /// <summary>Where to go when there is no usable <c>Referer</c> or remembered URL. A leading <c>~/</c> is the app root (<c>PathBase</c>).</summary>
    public string Fallback { get; }

    /// <summary>Always 302.</summary>
    public int? StatusCode => StatusCodes.Status302Found;

    /// <summary>Adds validation errors (field to messages) to <paramref name="bag"/> for the next page.</summary>
    public InertiaBackResult WithErrors(IDictionary<string, string[]> errors, string bag = "default")
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(bag);
        (_errors ??= []).Add((errors, bag));
        return this;
    }

    /// <summary>Adds validation errors (field to message) to <paramref name="bag"/> for the next page.</summary>
    public InertiaBackResult WithErrors(IDictionary<string, string> errors, string bag = "default")
    {
        ArgumentNullException.ThrowIfNull(errors);
        return WithErrors(errors.ToDictionary(e => e.Key, e => new[] { e.Value }, StringComparer.Ordinal), bag);
    }

    /// <summary>Adds flash data for the next page.</summary>
    public InertiaBackResult WithFlash(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        (_flash ??= new(StringComparer.Ordinal))[key] = value;
        return this;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (_errors is not null || _flash is not null)
        {
            var feature = httpContext.Inertia();
            foreach (var (errors, bag) in _errors ?? [])
            {
                feature.WithErrors(errors, bag);
            }

            foreach (var (key, value) in _flash ?? [])
            {
                feature.Flash(key, value);
            }
        }

        httpContext.Response.StatusCode = StatusCodes.Status302Found;
        httpContext.Response.Headers.Location = GetBackUrl(httpContext.Request, Fallback, InertiaMountServices.Options(httpContext)?.State);
        return Task.CompletedTask;
    }

    /// <summary>
    /// InertiaUI Modal's <c>X-InertiaUI-Modal-Base-Url</c>, then the <c>Referer</c> (absolute, so a <c>//host</c> path cannot turn it into a protocol-relative redirect) when it is an http(s) URL
    /// with the request's host and port, otherwise the validated previous-URL cookie when enabled, then <paramref name="fallback"/>, where a leading <c>~/</c> is replaced by <c>PathBase/</c>.
    /// An <c>http</c> Referer on an HTTPS request is upgraded to <c>https</c>, so going back never downgrades the connection.
    /// </summary>
    internal static string GetBackUrl(HttpRequest request, string fallback, InertiaStateOptions? options)
    {
        // A form inside a modal goes back to the page under the modal (InertiaUI Modal's base URL header), then to the Referer.
        if (SameHostUrl(request, request.Headers[InertiaModalResult.BaseUrlHeader].ToString()) is { } modalBase) return modalBase;
        if (SameHostUrl(request, request.Headers.Referer.ToString()) is { } referer) return referer;

        if (PreviousUrlCookie.Read(request, options) is { } previous) return previous;

        return fallback.StartsWith("~/", StringComparison.Ordinal) ? request.PathBase.ToUriComponent() + fallback[1..] : fallback;
    }

    /// <summary>
    /// <paramref name="url"/> as an absolute URL when it is an http(s) URL with the request's host and port (the scheme is not compared, so this
    /// keeps working behind a TLS-terminating proxy); an <c>http</c> URL on an HTTPS request is upgraded to <c>https</c>. Otherwise null.
    /// </summary>
    internal static string? SameHostUrl(HttpRequest request, string url) => SameHostUri(request, url) is { } uri ? uri.AbsoluteUri : null;

    internal static Uri? SameHostUri(HttpRequest request, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            || (request.Host.Port is { } port ? uri.Port != port : !uri.IsDefaultPort))
        {
            return null;
        }

        return request.IsHttps && uri.Scheme == Uri.UriSchemeHttp
            ? new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = uri.IsDefaultPort ? -1 : uri.Port }.Uri
            : uri;
    }
}
