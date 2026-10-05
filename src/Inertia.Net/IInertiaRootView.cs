using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;

namespace Inertia.Net;

/// <summary>Renders the HTML document of the first (non-Inertia) visit.</summary>
public interface IInertiaRootView
{
    /// <summary>Writes the HTML response. Status code and content type are already set.</summary>
    ValueTask RenderAsync(InertiaRootViewContext context);
}

/// <summary>What a root view needs to render the page.</summary>
public sealed class InertiaRootViewContext
{
    internal static readonly Dictionary<string, object?> EmptyViewData = [];

    internal InertiaRootViewContext(HttpContext httpContext, string component, string rootView, IReadOnlyDictionary<string, object?> viewData, ReadOnlyMemory<byte> pageJson, string rootElementId)
    {
        HttpContext = httpContext;
        Component = component;
        RootView = rootView;
        ViewData = viewData;
        PageJson = pageJson;
        RootElementId = rootElementId;
    }

    /// <summary>The current request.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The page component.</summary>
    public string Component { get; }

    /// <summary>The root view name (<see cref="InertiaOptions.RootView"/> or the per-result override).</summary>
    public string RootView { get; }

    /// <summary>Data passed with <see cref="InertiaResult.WithViewData"/>.</summary>
    public IReadOnlyDictionary<string, object?> ViewData { get; }

    /// <summary>The UTF-8 page JSON, already escaped so it can be embedded in a <c>&lt;script&gt;</c> element. Only valid during <see cref="IInertiaRootView.RenderAsync"/>.</summary>
    public ReadOnlyMemory<byte> PageJson { get; }

    /// <summary>The root element id.</summary>
    public string RootElementId { get; }

    /// <summary>Writes <c>&lt;script data-page="{id}" type="application/json"&gt;{page}&lt;/script&gt;&lt;div id="{id}"&gt;&lt;/div&gt;</c>.</summary>
    public void WriteBody(IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var id = HtmlEncoder.Default.Encode(RootElementId);
        writer.Write("<script data-page=\""u8);
        Write(writer, id);
        writer.Write("\" type=\"application/json\">"u8);
        writer.Write(PageJson.Span);
        writer.Write("</script><div id=\""u8);
        Write(writer, id);
        writer.Write("\"></div>"u8);
    }

    /// <summary>Returns the same markup as <see cref="WriteBody"/> as a string.</summary>
    public string GetBodyHtml()
    {
        var buffer = new ArrayBufferWriter<byte>(PageJson.Length + 128);
        WriteBody(buffer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Write(IBufferWriter<byte> writer, string text)
    {
        var span = writer.GetSpan(Encoding.UTF8.GetMaxByteCount(text.Length));
        writer.Advance(Encoding.UTF8.GetBytes(text, span));
    }
}

/// <summary>Fallback root view: a bare HTML document around the page. Replaced by the template root view.</summary>
internal sealed class MinimalRootView : IInertiaRootView
{
    public async ValueTask RenderAsync(InertiaRootViewContext context)
    {
        var writer = context.HttpContext.Response.BodyWriter;
        writer.Write("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>"u8);
        context.WriteBody(writer);
        writer.Write("</body></html>"u8);
        await writer.FlushAsync(context.HttpContext.RequestAborted);
    }
}
