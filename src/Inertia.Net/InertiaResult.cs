using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net;

/// <summary>Renders an Inertia page: a JSON page for Inertia requests, otherwise the root view with the page embedded.</summary>
public sealed class InertiaResult : IResult, IStatusCodeHttpResult, IContentTypeHttpResult, IEndpointMetadataProvider
{
    private int _statusCode = StatusCodes.Status200OK;
    private string? _rootView;
    private Dictionary<string, object?>? _viewData;
    private SsrMode? _ssrMode;
    private SsrCaching? _ssrCache; // one reference, set only by WithSsrCache()

    internal InertiaResult(string component, object? props)
    {
        ArgumentNullException.ThrowIfNull(component);
        Component = component;
        Props = props;
    }

    /// <summary>The client-side page component.</summary>
    public string Component { get; }

    /// <summary>The page props.</summary>
    public object? Props { get; }

    /// <summary>The response status code. Default 200.</summary>
    public int? StatusCode => _statusCode;

    /// <summary>Always null: the content type depends on the request (JSON for Inertia requests, HTML otherwise).</summary>
    public string? ContentType => null;

    /// <summary>Sets the response status code (for error pages).</summary>
    public InertiaResult WithStatusCode(int statusCode)
    {
        _statusCode = statusCode;
        return this;
    }

    /// <summary>
    /// Overrides <see cref="InertiaOptions.RootView"/> for this response. The template view only accepts paths inside the content
    /// root; never pass a value taken from the request.
    /// </summary>
    public InertiaResult WithRootView(string rootView)
    {
        ArgumentNullException.ThrowIfNull(rootView);
        _rootView = rootView;
        return this;
    }

    /// <summary>Passes data to the root view (not to the page props).</summary>
    public InertiaResult WithViewData(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        (_viewData ??= new(StringComparer.Ordinal))[key] = value;
        return this;
    }

    /// <summary>
    /// Sets how this response uses server-side rendering, overriding <see cref="SsrOptions.Mode"/> and <see cref="SsrOptions.ExcludePaths"/>.
    /// A global <see cref="SsrMode.Disabled"/> still wins. Has no effect on Inertia (JSON) visits.
    /// </summary>
    public InertiaResult WithSsr(SsrMode mode)
    {
        _ssrMode = mode;
        return this;
    }

    /// <summary>Renders this response on the client only: same as <c>WithSsr(SsrMode.Disabled)</c>.</summary>
    public InertiaResult WithoutSsr() => WithSsr(SsrMode.Disabled);

    /// <summary>
    /// Reuses this page's server-rendered HTML for <paramref name="duration"/> when the page JSON is the same, overriding
    /// <see cref="SsrCacheOptions.Duration"/>; <see cref="TimeSpan.Zero"/> turns the cache off for this response.
    /// </summary>
    public InertiaResult WithSsrCache(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        _ssrCache = new SsrCaching(duration, null);
        return this;
    }

    /// <summary>
    /// Reuses this page's server-rendered HTML like <see cref="WithSsrCache(TimeSpan)"/>, and once <paramref name="duration"/> has passed
    /// keeps serving it for <paramref name="staleFor"/> more while the page is rendered again after the response has been sent
    /// (stale-while-revalidate), overriding <see cref="SsrCacheOptions.StaleFor"/>.
    /// </summary>
    public InertiaResult WithSsrCache(TimeSpan duration, TimeSpan staleFor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(staleFor, TimeSpan.Zero);
        _ssrCache = new SsrCaching(duration, staleFor);
        return this;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var services = httpContext.RequestServices;
        var pageWriter = services.GetService<InertiaPageWriter>()
            ?? throw new InvalidOperationException("Inertia.Net services are not registered. Call services.AddInertia() at startup.");
        var metrics = services.GetService<InertiaMetrics>();
        var started = metrics?.PageDurationEnabled == true ? Stopwatch.GetTimestamp() : 0;
        var request = new InertiaRequest(httpContext.Request.Headers);
        var response = httpContext.Response;
        pageWriter.Pages?.EnsureExists(Component);
        using var activity = InertiaDiagnostics.ActivitySource.StartActivity("inertia.render");
        activity?.SetTag("inertia.component", Component);

        // The page is fully buffered first, so a failing loader never produces a partial 200.
        using var page = await pageWriter.WritePageAsync(httpContext, Component, Props, request, htmlSafe: !request.IsInertia);
        var partial = string.Equals(request.PartialComponent, Component, StringComparison.Ordinal);
        var devTools = DevToolsRecorder.Recording ? httpContext.Features.Get<DevToolsCapture>() : null;
        if (devTools is not null)
        {
            devTools.Component = Component;
            devTools.PageJson = page.WrittenMemory.ToArray();
        }

        response.StatusCode = _statusCode;
        InertiaHeaders.AppendVary(response.Headers);

        if (request.IsInertia)
        {
            response.Headers[InertiaHeaders.Inertia] = "true";
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength = page.WrittenMemory.Length;
            await response.BodyWriter.WriteAsync(page.WrittenMemory, httpContext.RequestAborted);
            metrics?.RecordPage(started == 0 ? 0 : Stopwatch.GetElapsedTime(started).TotalSeconds, page.WrittenMemory.Length, json: true, partial);
            return;
        }

        response.ContentType = "text/html; charset=utf-8";
        var rootView = services.GetRequiredService<IInertiaRootView>();
        await rootView.RenderAsync(new InertiaRootViewContext(
            httpContext,
            Component,
            _rootView ?? pageWriter.Options.RootView,
            _viewData ?? (IReadOnlyDictionary<string, object?>)InertiaRootViewContext.EmptyViewData,
            page.WrittenMemory,
            pageWriter.Options.RootElementId,
            _ssrMode,
            CspNonce.Resolve(pageWriter.Options.CspNonce, httpContext),
            _ssrCache));
        if (devTools is not null)
        {
            // The extension reads the first page's entry id from this tag; after the document is fine for a script data block.
            await response.BodyWriter.WriteAsync(DevToolsRecorder.DiscoveryTag(devTools.Id), httpContext.RequestAborted);
        }

        metrics?.RecordPage(started == 0 ? 0 : Stopwatch.GetElapsedTime(started).TotalSeconds, page.WrittenMemory.Length, json: false, partial);
    }

    /// <inheritdoc />
    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(void), ["text/html", "application/json"]));
    }
}

/// <summary>A full page visit: 409 with <c>X-Inertia-Location</c> for Inertia requests, otherwise a 302 redirect.</summary>
public sealed class InertiaLocationResult : IResult
{
    internal InertiaLocationResult(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        Url = url;
    }

    /// <summary>The target URL.</summary>
    public string Url { get; }

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var response = httpContext.Response;
        InertiaHeaders.AppendVary(response.Headers);
        if (!string.IsNullOrEmpty(httpContext.Request.Headers[InertiaHeaders.Inertia]))
        {
            response.StatusCode = StatusCodes.Status409Conflict;
            response.Headers[InertiaHeaders.Location] = Url;
        }
        else
        {
            response.StatusCode = StatusCodes.Status302Found;
            response.Headers.Location = Url;
        }

        return Task.CompletedTask;
    }
}
