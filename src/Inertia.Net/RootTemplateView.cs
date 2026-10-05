using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>The server-side-rendered head and body of a page. Supplied by the SSR phase; null means render on the client.</summary>
internal readonly record struct SsrRender(string Head, string Body);

/// <summary>Seam for SSR: when registered, <see cref="RootTemplateView"/> renders <c>@inertiaHead</c> and <c>@inertia</c> from the result.</summary>
internal interface IInertiaSsrRenderer
{
    /// <summary>Renders the page on the SSR server, or returns null to fall back to client-side rendering.</summary>
    ValueTask<SsrRender?> RenderAsync(InertiaRootViewContext context);
}

/// <summary>The default root view: an HTML file with <c>@inertia</c>, <c>@inertiaHead</c>, <c>@vite(...)</c>, <c>@viteReactRefresh</c> and <c>@viewData("key")</c> tokens.</summary>
internal sealed class RootTemplateView : IInertiaRootView
{
    private readonly string _contentRoot;
    private readonly ViteAssets _vite;
    private readonly IInertiaSsrRenderer? _ssr;
    private readonly FileCache<RootTemplate> _templates;
    private readonly ConcurrentDictionary<string, string> _paths = new(StringComparer.Ordinal); // root view name -> full path

    public RootTemplateView(IHostEnvironment environment, ViteAssets vite, TimeProvider timeProvider, IInertiaSsrRenderer? ssr = null)
    {
        _contentRoot = environment.ContentRootPath;
        _vite = vite;
        _ssr = ssr;
        _templates = new FileCache<RootTemplate>(path => RootTemplate.Parse(File.ReadAllText(path), path), environment.IsDevelopment(), timeProvider);
    }

    public async ValueTask RenderAsync(InertiaRootViewContext context)
    {
        var path = _paths.GetOrAdd(context.RootView, static (view, root) => Path.Combine(root, view), _contentRoot);
        var template = _templates.Get(path)
            ?? throw new FileNotFoundException($"The Inertia root template '{path}' was not found. Create it (it must contain @inertia), or set InertiaOptions.RootView / InertiaResult.WithRootView.", path);

        var http = context.HttpContext;
        var ssr = _ssr is null ? null : await _ssr.RenderAsync(context);
        var writer = http.Response.BodyWriter;
        foreach (var segment in template.Segments)
        {
            switch (segment.Kind)
            {
                case RootTemplate.Kind.Literal:
                    writer.Write(segment.Bytes);
                    break;
                case RootTemplate.Kind.Head:
                    if (ssr is { } head)
                    {
                        WriteString(writer, head.Head);
                    }

                    break;
                case RootTemplate.Kind.Body:
                    if (ssr is { } body)
                    {
                        WriteString(writer, body.Body);
                    }
                    else
                    {
                        context.WriteBody(writer);
                    }

                    break;
                case RootTemplate.Kind.Vite:
                    writer.Write(_vite.RenderTagsUtf8(http, segment.Args!));
                    break;
                case RootTemplate.Kind.ReactRefresh:
                    writer.Write(_vite.RenderReactRefreshUtf8(http));
                    break;
                case RootTemplate.Kind.ViewData:
                    if (context.ViewData.TryGetValue(segment.Args![0], out var value) && Convert.ToString(value, CultureInfo.InvariantCulture) is { } text)
                    {
                        WriteString(writer, HtmlEncoder.Default.Encode(text));
                    }

                    break;
            }
        }

        await writer.FlushAsync(http.RequestAborted);
    }

    private static void WriteString(IBufferWriter<byte> writer, string text)
    {
        var span = writer.GetSpan(Encoding.UTF8.GetMaxByteCount(text.Length));
        writer.Advance(Encoding.UTF8.GetBytes(text, span));
    }
}

/// <summary>A parsed root template: pre-encoded literal chunks and tokens.</summary>
internal sealed class RootTemplate
{
    internal enum Kind
    {
        Literal,
        Head,
        Body,
        Vite,
        ReactRefresh,
        ViewData,
    }

    internal readonly record struct Segment(Kind Kind, byte[]? Bytes = null, string[]? Args = null);

    private RootTemplate(Segment[] segments) => Segments = segments;

    public Segment[] Segments { get; }

    /// <summary>Parses the template text (a leading BOM is already stripped by <see cref="File.ReadAllText(string)"/>).</summary>
    public static RootTemplate Parse(string text, string path)
    {
        var segments = new List<Segment>();
        var literal = new StringBuilder();
        void Add(Segment segment)
        {
            if (literal.Length > 0)
            {
                segments.Add(new Segment(Kind.Literal, Encoding.UTF8.GetBytes(literal.ToString())));
                literal.Clear();
            }

            segments.Add(segment);
        }

        for (var i = 0; i < text.Length;)
        {
            if (text[i] != '@')
            {
                literal.Append(text[i++]);
                continue;
            }

            var rest = text.AsSpan(i + 1);
            if (rest.StartsWith("@", StringComparison.Ordinal))
            {
                literal.Append('@');
                i += 2;
            }
            else if (IsToken(rest, "inertiaHead"))
            {
                Add(new Segment(Kind.Head));
                i += 1 + "inertiaHead".Length;
            }
            else if (IsToken(rest, "inertia"))
            {
                Add(new Segment(Kind.Body));
                i += 1 + "inertia".Length;
            }
            else if (IsToken(rest, "viteReactRefresh"))
            {
                Add(new Segment(Kind.ReactRefresh));
                i += 1 + "viteReactRefresh".Length;
            }
            else if (IsToken(rest, "vite") || IsToken(rest, "viewData"))
            {
                var vite = IsToken(rest, "vite");
                var name = vite ? "vite" : "viewData";
                var start = i;
                i += 1 + name.Length;
                var args = ParseArguments(text, ref i)
                    ?? throw new InvalidOperationException($"Invalid @{name} in '{path}' at line {Line(text, start)}. Expected @{name}(\"...\"){(vite ? " with one or more comma-separated entries" : " with a single key")}.");
                if (args.Length == 0 || (!vite && args.Length != 1))
                {
                    throw new InvalidOperationException($"Invalid @{name} in '{path}' at line {Line(text, start)}: wrong number of arguments.");
                }

                Add(new Segment(vite ? Kind.Vite : Kind.ViewData, Args: args));
            }
            else
            {
                literal.Append(text[i++]); // CSS at-rules, e-mail addresses, ...
            }
        }

        if (literal.Length > 0)
        {
            segments.Add(new Segment(Kind.Literal, Encoding.UTF8.GetBytes(literal.ToString())));
        }

        if (!segments.Exists(s => s.Kind == Kind.Body))
        {
            throw new InvalidOperationException($"The Inertia root template '{path}' has no @inertia token. Add @inertia where the page should be rendered, inside <body>.");
        }

        return new RootTemplate([.. segments]);
    }

    private static bool IsToken(ReadOnlySpan<char> rest, string name) =>
        rest.StartsWith(name, StringComparison.Ordinal) && (rest.Length == name.Length || !(char.IsLetterOrDigit(rest[name.Length]) || rest[name.Length] == '_'));

    private static int Line(string text, int index) => 1 + text.AsSpan(0, index).Count('\n');

    /// <summary>Parses <c>("a", 'b')</c> at <paramref name="i"/>; returns null when malformed.</summary>
    private static string[]? ParseArguments(string text, ref int i)
    {
        var args = new List<string>();
        if (i >= text.Length || text[i] != '(')
        {
            return null;
        }

        i++;
        while (true)
        {
            SkipWhitespace(text, ref i);
            if (i >= text.Length || (text[i] != '"' && text[i] != '\''))
            {
                return null;
            }

            var quote = text[i++];
            var end = text.IndexOf(quote, i);
            if (end < 0 || text.AsSpan(i, end - i).ContainsAny('\r', '\n'))
            {
                return null;
            }

            args.Add(text[i..end]);
            i = end + 1;
            SkipWhitespace(text, ref i);
            if (i >= text.Length)
            {
                return null;
            }

            if (text[i++] == ')')
            {
                return [.. args];
            }

            if (text[i - 1] != ',')
            {
                return null;
            }
        }
    }

    private static void SkipWhitespace(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }
    }
}
