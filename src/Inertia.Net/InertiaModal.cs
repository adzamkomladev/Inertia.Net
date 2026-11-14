using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Inertia.Net;

/// <summary>
/// A page opened in an InertiaUI Modal (<c>@inertiaui/modal-react</c>, <c>@inertiaui/modal-vue</c> 3.x): <c>Modal("Users/Edit", props).WithBaseUrl("/users")</c>.
/// <list type="bullet">
/// <item>The modal client's request (<c>X-InertiaUI-Modal</c>) gets the modal's page JSON, with <c>mergeProps</c>, <c>deferredProps</c> and
/// <c>cache</c> under <c>meta</c>.</item>
/// <item>Any other visit renders the page under the modal, with the modal in the <c>_inertiaui_modal</c> prop and the modal's URL as the page URL.
/// The base URL is the first same-origin one of <c>X-InertiaUI-Modal-Base-Url</c>, <c>Referer</c> and <see cref="WithBaseUrl"/> whose path is not
/// this request's. Its endpoint runs in-process as a GET through the middleware after <c>UseInertia()</c>, so put <c>UseAuthorization()</c>
/// after <c>UseInertia()</c>; an endpoint with authorization metadata otherwise fails instead of rendering. With no base URL, or no endpoint at it,
/// the modal renders as a page.</item>
/// </list>
/// </summary>
public sealed class InertiaModalResult : IResult
{
    internal const string ModalHeader = "X-InertiaUI-Modal";
    internal const string BaseUrlHeader = "X-InertiaUI-Modal-Base-Url";
    internal const string ModalProp = "_inertiaui_modal";
    private static readonly string[] MetaKeys = ["mergeProps", "deferredProps", "cache"];

    internal InertiaModalResult(string component, object? props)
    {
        ArgumentNullException.ThrowIfNull(component);
        Component = component;
        Props = props;
    }

    /// <summary>The modal's page component.</summary>
    public string Component { get; }

    /// <summary>The modal's props.</summary>
    public object? Props { get; }

    /// <summary>The page under the modal when the request names none (a direct visit without <c>Referer</c>), or null.</summary>
    public string? BaseUrl { get; private set; }

    /// <summary>
    /// Sets the page shown under the modal on a direct visit: an app path (<c>/users</c> or <c>~/users</c>, under <c>PathBase</c>) or an
    /// absolute URL on this host. Use a typed route's URL, e.g. <c>WithBaseUrl(Routes.UsersIndex())</c>, where the app has one.
    /// </summary>
    public InertiaModalResult WithBaseUrl(string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        BaseUrl = baseUrl;
        return this;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var page = new InertiaResult(Component, Props);
        var request = httpContext.Request;
        if (request.Headers.ContainsKey(ModalHeader))
        {
            page.ModalMeta = true;
            await page.ExecuteAsync(httpContext);
            return;
        }

        // A base page that is itself a modal renders as a page, so the base visit never recurses.
        if (httpContext.Inertia().PageUrl is not null
            || InertiaMountServices.PageWriter(httpContext) is not { Modals: { } modals } pageWriter
            || ResolveBaseUrl(request) is not { } target)
        {
            await page.ExecuteAsync(httpContext);
            return;
        }

        pageWriter.Pages?.EnsureExists(Component);
        JsonObject modal;
        using (var json = await pageWriter.WritePageAsync(httpContext, Component, Props, new InertiaRequest(request.Headers), htmlSafe: false))
        {
            modal = ModalNode(json.WrittenMemory.Span);
        }

        modal["id"] = null;
        modal["baseUrl"] = target.Url;
        if (!await modals.DispatchAsync(httpContext, target, modal))
        {
            await page.ExecuteAsync(httpContext);
        }
    }

    /// <summary>The candidate base URLs in order, each kept only when it is on this host, under <c>PathBase</c>, and not this request's path.</summary>
    internal ModalBase? ResolveBaseUrl(HttpRequest request)
    {
        foreach (var candidate in (ReadOnlySpan<string?>)[request.Headers[BaseUrlHeader].ToString(), request.Headers.Referer.ToString(), Configured(request)])
        {
            if (string.IsNullOrEmpty(candidate)
                || InertiaBackResult.SameHostUri(request, candidate) is not { } uri
                || !PathString.FromUriComponent(uri).StartsWithSegments(request.PathBase, out var path)
                || string.Equals(path.Value?.TrimEnd('/'), request.Path.Value?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new ModalBase(uri.AbsoluteUri, path, QueryString.FromUriComponent(uri));
        }

        return null;
    }

    private string? Configured(HttpRequest request)
    {
        if (BaseUrl is null || Uri.IsWellFormedUriString(BaseUrl, UriKind.Absolute))
        {
            return BaseUrl;
        }

        var path = BaseUrl.StartsWith("~/", StringComparison.Ordinal) ? BaseUrl[1..] : BaseUrl.StartsWith('/') ? BaseUrl : "/" + BaseUrl;
        return request.Scheme + "://" + request.Host.ToUriComponent() + request.PathBase.ToUriComponent() + path;
    }

    /// <summary>The page JSON as the modal client reads it: <c>mergeProps</c>, <c>deferredProps</c> and <c>cache</c> moved under <c>meta</c>.</summary>
    internal static JsonObject ModalNode(ReadOnlySpan<byte> page)
    {
        var node = JsonNode.Parse(page)!.AsObject();
        var meta = new JsonObject();
        foreach (var key in MetaKeys)
        {
            if (node.TryGetPropertyValue(key, out var value))
            {
                node.Remove(key);
                meta[key] = value;
            }
        }

        node["meta"] = meta;
        return node;
    }

    internal static ReadOnlyMemory<byte> ModalPage(ReadOnlySpan<byte> page, JsonWriterOptions options)
    {
        var buffer = new ArrayBufferWriter<byte>(page.Length + 16);
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            ModalNode(page).WriteTo(writer);
        }

        return buffer.WrittenMemory;
    }
}

/// <summary>A resolved base URL: what the client is told, and the app path and query its endpoint runs with.</summary>
internal readonly record struct ModalBase(string Url, PathString Path, QueryString Query);

/// <summary>Runs a modal's base URL endpoint in-process: routing on a copy of the app's endpoints, then the middleware after <c>UseInertia()</c>.</summary>
internal sealed class ModalDispatcher(IApplicationBuilder app, RequestDelegate next)
{
    // Set by UseAuthorization()/UseCors() for the endpoint they checked; cleared so they check the base endpoint, or EndpointMiddleware refuses it.
    private const string AuthorizationInvoked = "__AuthorizationMiddlewareWithEndpointInvoked";
    private const string CorsInvoked = "__CorsMiddlewareWithEndpointInvoked";

    // FastEndpoints' "response started" mark, set before it runs the modal result: the base endpoint would skip sending its page.
    private const string FastEndpointsStarted = "0";

    // Routing over the endpoints of this pipeline: a branch's own UseRouting()/UseEndpoints(), else the WebApplication's, which New() leaves out.
    private readonly Lazy<RequestDelegate> _match = new(() =>
    {
        if (!app.Properties.TryGetValue("__EndpointRouteBuilder", out var routes)) app.Properties.TryGetValue("__GlobalEndpointRouteBuilder", out routes);
        var routing = app.New();
        if (routes is not null)
        {
            routing.Properties["__GlobalEndpointRouteBuilder"] = routes;
        }

        routing.UseRouting();
        routing.Run(static _ => Task.CompletedTask);
        return routing.Build();
    });

    /// <summary>Renders the base page with the modal shared; false (with the request unchanged) when no endpoint serves the base URL.</summary>
    public async Task<bool> DispatchAsync(HttpContext context, ModalBase target, JsonObject modal)
    {
        var request = context.Request;
        var (path, query, method, endpoint, routeValues) = (request.Path, request.QueryString, request.Method, context.GetEndpoint(), request.RouteValues);
        var feature = context.Inertia();
        var started = context.Items.TryGetValue(FastEndpointsStarted, out var startedMark);
        try
        {
            request.Path = target.Path;
            request.QueryString = target.Query;
            request.Method = HttpMethods.Get;
            context.SetEndpoint(null);
            await _match.Value(context);
            if (context.GetEndpoint() is null)
            {
                return false;
            }

            feature.Share(InertiaModalResult.ModalProp, modal);
            feature.PageUrl = (string?)modal["url"];
            context.Items.Remove(AuthorizationInvoked);
            context.Items.Remove(CorsInvoked);
            context.Items.Remove(FastEndpointsStarted);
            await next(context);
            return true;
        }
        finally
        {
            feature.PageUrl = null;
            request.Path = path;
            request.QueryString = query;
            request.Method = method;
            context.SetEndpoint(endpoint);
            request.RouteValues = routeValues;
            if (started)
            {
                context.Items[FastEndpointsStarted] = startedMark;
            }
        }
    }
}
