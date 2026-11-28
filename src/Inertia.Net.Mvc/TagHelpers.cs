using System.Collections.Concurrent;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Mvc.TagHelpers;

/// <summary><c>&lt;inertia /&gt;</c>: the server-rendered body when SSR is on, otherwise the page <c>&lt;script data-page&gt;</c> and the root element.</summary>
[HtmlTargetElement("inertia")]
public sealed class InertiaTagHelper : TagHelper
{
    /// <summary>The view being rendered.</summary>
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var http = ViewContext.HttpContext;
        var ssr = await RazorInertiaRootView.GetSsrAsync(http);
        output.TagName = null;
        if (ssr is { } render)
        {
            output.Content.SetHtmlContent(new Utf8HtmlContent(render.Body));
        }
        else
        {
            output.Content.SetHtmlContent(new PageBodyHtmlContent(RazorInertiaRootView.GetContext(http)));
        }
    }
}

/// <summary>
/// <c>&lt;inertia-head /&gt;</c>: the head tags from the SSR server; when SSR is off or failed, the server-managed head tags
/// (<see cref="InertiaHead"/>), if any.
/// </summary>
[HtmlTargetElement("inertia-head")]
public sealed class InertiaHeadTagHelper : TagHelper
{
    /// <summary>The view being rendered.</summary>
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var ssr = await RazorInertiaRootView.GetSsrAsync(ViewContext.HttpContext);
        output.TagName = null;
        output.Content.SetHtmlContent(ssr is { } render ? new Utf8HtmlContent(render.Head)
            : ViewContext.HttpContext.Features.Get<InertiaFeature>()?.HeadHtml is { } tags ? new HtmlString(string.Join('\n', tags))
            : new Utf8HtmlContent(ReadOnlyMemory<byte>.Empty));
    }
}

/// <summary><c>&lt;vite entry="src/app.ts" /&gt;</c>: the Vite tags for one or more comma-separated entries (see <see cref="ViteAssets"/>).</summary>
[HtmlTargetElement("vite", Attributes = "entry")]
public sealed class ViteTagHelper(ViteAssets vite) : TagHelper
{
    /// <summary>The entries, as in the Vite manifest, comma-separated.</summary>
    public string Entry { get; set; } = "";

    /// <summary>The view being rendered.</summary>
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        var (entries, key) = SplitEntries(Entry);
        var http = ViewContext.HttpContext;
        var assets = InertiaMountServices.Mount(http) is { } mount
            ? http.RequestServices.GetRequiredKeyedService<ViteAssets>(mount) : vite;
        output.Content.SetHtmlContent(new Utf8HtmlContent(
            assets.RenderTagsUtf8(http, entries, key, RazorInertiaRootView.TryGetContext(http)?.Component)));
    }

    private const int MaxCachedEntryLists = 256;
    private static readonly ConcurrentDictionary<string, (string[] Entries, string Key)> EntryLists = new(StringComparer.Ordinal);

    // The entry attribute is a constant in the view, so its split and tag cache key are cached instead of rebuilt on every render.
    private static (string[] Entries, string Key) SplitEntries(string entry)
    {
        if (EntryLists.TryGetValue(entry, out var split))
        {
            return split;
        }

        var entries = entry.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        split = (entries, string.Join('\n', entries));
        if (EntryLists.Count < MaxCachedEntryLists)
        {
            EntryLists.TryAdd(entry, split);
        }

        return split;
    }
}

/// <summary><c>&lt;vite-react-refresh /&gt;</c>: the React Fast Refresh preamble while the Vite dev server runs. Put it before <c>&lt;vite /&gt;</c>.</summary>
[HtmlTargetElement("vite-react-refresh")]
public sealed class ViteReactRefreshTagHelper(ViteAssets vite) : TagHelper
{
    /// <summary>The view being rendered.</summary>
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        output.Content.SetHtmlContent(new Utf8HtmlContent(vite.RenderReactRefreshUtf8(ViewContext.HttpContext)));
    }
}
