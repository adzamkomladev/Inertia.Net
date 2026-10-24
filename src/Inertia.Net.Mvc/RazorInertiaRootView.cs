using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace Inertia.Net.Mvc;

/// <summary>Renders <see cref="InertiaRootViewContext.RootView"/> as a Razor view with the context as its model.</summary>
internal sealed class RazorInertiaRootView(IActionResultExecutor<ViewResult> executor) : IInertiaRootView
{
    private static readonly object ContextKey = new();
    private static readonly object SsrKey = new();

    // Shared: the provider caches the metadata it builds, so one per render rebuilt the model's metadata on every render.
    private static readonly EmptyModelMetadataProvider MetadataProvider = new();

    public async ValueTask RenderAsync(InertiaRootViewContext context)
    {
        var http = context.HttpContext;
        http.Items[ContextKey] = context;
        var viewData = new ViewDataDictionary(MetadataProvider, new ModelStateDictionary()) { Model = context };
        foreach (var (key, value) in context.ViewData)
        {
            viewData[key] = value;
        }

        try
        {
            await executor.ExecuteAsync(
                new ActionContext(http, http.GetRouteData(), new ActionDescriptor()),
                new ViewResult { ViewName = context.RootView, ViewData = viewData });
        }
        finally
        {
            // The rendered HTML is in a pooled buffer that the tag helpers have written by now.
            if (http.Items[SsrKey] is Task<SsrRender?> { IsCompletedSuccessfully: true, Result: { } ssr })
            {
                ssr.Dispose();
            }
        }
    }

    /// <summary>The context of the page being rendered; the tag helpers only work inside the root view.</summary>
    internal static InertiaRootViewContext GetContext(HttpContext http) =>
        http.Items[ContextKey] as InertiaRootViewContext
        ?? throw new InvalidOperationException("The Inertia tag helpers can only be used in the Inertia root view (see InertiaMvcOptions.UseRazorRootView).");

    /// <summary>SSRs the page at most once per request, so <c>&lt;inertia-head /&gt;</c> and <c>&lt;inertia /&gt;</c> share one call.</summary>
    internal static Task<SsrRender?> GetSsrAsync(HttpContext http)
    {
        if (http.Items[SsrKey] is not Task<SsrRender?> ssr)
        {
            var renderer = http.RequestServices.GetService(typeof(IInertiaSsrRenderer)) as IInertiaSsrRenderer;
            http.Items[SsrKey] = ssr = renderer is null ? Task.FromResult<SsrRender?>(null) : renderer.RenderAsync(GetContext(http)).AsTask();
        }

        return ssr;
    }
}
