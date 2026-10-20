using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;

namespace Inertia.Net.Mvc;

/// <summary>Pre-encoded UTF-8 HTML, decoded to the view's writer in pooled chunks instead of being materialized as a string.</summary>
internal sealed class Utf8HtmlContent(ReadOnlyMemory<byte> html) : IHtmlContent
{
    public void WriteTo(TextWriter writer, HtmlEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Write(writer, html.Span);
    }

    internal static void Write(TextWriter writer, ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return;
        }

        const int ChunkBytes = 4096;
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(ChunkBytes));
        try
        {
            while (!utf8.IsEmpty)
            {
                var chunk = utf8[..Math.Min(ChunkBytes, utf8.Length)];
                // The decoder carries a code point split across chunk boundaries over to the next call.
                decoder.Convert(chunk, chars, flush: chunk.Length == utf8.Length, out var bytesUsed, out var charsUsed, out _);
                writer.Write(chars, 0, charsUsed);
                utf8 = utf8[bytesUsed..];
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }
}

/// <summary>
/// The <c>&lt;inertia /&gt;</c> markup without SSR: the page <c>&lt;script data-page&gt;</c> and the root element, written straight
/// from the buffered page JSON (valid for the whole Razor render, which completes inside <see cref="IInertiaRootView.RenderAsync"/>).
/// </summary>
internal sealed class PageBodyHtmlContent(InertiaRootViewContext context) : IHtmlContent
{
    public void WriteTo(TextWriter writer, HtmlEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var id = HtmlEncoder.Default.Encode(context.RootElementId);
        writer.Write("<script data-page=\"");
        writer.Write(id);
        if (context.CspNonce is { } nonce)
        {
            writer.Write("\" nonce=\"");
            writer.Write(nonce);
        }

        writer.Write("\" type=\"application/json\">");
        Utf8HtmlContent.Write(writer, context.PageJson.Span);
        writer.Write("</script><div id=\"");
        writer.Write(id);
        writer.Write("\"></div>");
    }
}
