using Microsoft.AspNetCore.Http;

namespace Inertia.Net;

/// <summary>
/// Redirects back to the <c>Referer</c> (only when it has the request's own host, so it cannot become an open redirect),
/// otherwise to the fallback. A 302; <c>UseInertia()</c> turns it into a 303 for PUT/PATCH/DELETE and carries the errors
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

    /// <summary>Where to go when there is no usable <c>Referer</c>.</summary>
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
        httpContext.Response.Headers.Location = GetBackUrl(httpContext.Request, Fallback);
        return Task.CompletedTask;
    }

    /// <summary>The <c>Referer</c> (absolute, so a <c>//host</c> path cannot turn it into a protocol-relative redirect) when it is an http(s) URL with the request's host and port, otherwise <paramref name="fallback"/>.</summary>
    internal static string GetBackUrl(HttpRequest request, string fallback)
    {
        // The scheme is not compared, so this keeps working behind a TLS-terminating proxy.
        if (Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
            && (referer.Scheme == Uri.UriSchemeHttp || referer.Scheme == Uri.UriSchemeHttps)
            && string.Equals(referer.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && (request.Host.Port is { } port ? referer.Port == port : referer.IsDefaultPort))
        {
            return referer.AbsoluteUri;
        }

        return fallback;
    }
}
