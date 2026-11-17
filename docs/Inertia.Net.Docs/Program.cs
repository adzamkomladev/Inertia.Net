using System.Collections.Concurrent;
using Microsoft.AspNetCore.HttpOverrides;

// Bun SSR is enabled by default; set INERTIA_SSR=0 for client-only rendering.
var ssr = Environment.GetEnvironmentVariable("INERTIA_SSR") != "0";
var development = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

var builder = WebApplication.CreateBuilder(args);

// Coolify's Traefik proxy terminates TLS. Trust its forwarded scheme so cookies are Secure and
// Inertia's absolute 409 locations use https. Do not publish the container port directly.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddResponseCompression();
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
builder.Services.AddInertia(o =>
{
    o.Share("appName", "Inertia.Net");
    o.Share("version", "1.9.4");
    o.Share("baseUrl", ctx => $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}");
    o.Vite.Prefetch = VitePrefetch.Waterfall;
    o.Pages.Directory = "ClientApp/pages";
    o.TypeScript.RoutesOutputPath = "ClientApp/types/routes.ts"; // written in Development, or with --inertia-types
    o.Ssr.BundlePath = "ssr/ssr.js";
    if (ssr)
    {
        // The SSR bundle is self-contained; Bun runs it without Node or node_modules.
        o.Ssr.UseNodeProcess(n =>
        {
            n.Executable = "bun";
            n.BasePort = int.Parse(Environment.GetEnvironmentVariable("INERTIA_SSR_PORT") ?? "13715", System.Globalization.CultureInfo.InvariantCulture);
        });
    }
});
if (development)
{
    builder.Services.AddInertiaDevTools();
}

var app = builder.Build();

app.UseForwardedHeaders();
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = c =>
    {
        if (c.Context.Request.Path.StartsWithSegments("/build/assets"))
        {
            c.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    },
});
app.UseInertia();
app.UseInertiaAntiforgery();

if (development)
{
    app.MapInertiaDevTools();
}

app.MapGet("/", (HttpContext http) => Render("Home").WithHead(Seo.Tags(http,
    "Inertia.Net — React, Vue and Svelte for ASP.NET Core",
    "Inertia.Net lets .NET developers build React, Vue and Svelte frontends on ASP.NET Core without writing a separate API.")))
    .WithName("home");
app.MapGet("/health", () => Results.Ok("ok"));
app.MapMethods("/docs", ["GET", "HEAD"], () => Results.Redirect("/docs/introduction"));
app.MapGet("/docs/{**slug}", (string slug, HttpContext http, IWebHostEnvironment environment) =>
    DocsCatalog.Find(environment, slug) is { } doc
        ? Render("Docs/Show", new InertiaProps { ["slug"] = slug })
            .WithHead(Seo.Tags(http, $"{doc.Title} — Inertia.Net Docs", doc.Description ?? $"Inertia.Net documentation: {doc.Title}", doc.Section))
        : NotFound(http)).WithName("docs.show");
app.MapFallback(NotFound);

app.Run();

static IResult NotFound(HttpContext http) => Render("NotFound").WithStatusCode(404).WithHead(Seo.Tags(http,
    "Page not found — Inertia.Net", "The requested page is not part of the Inertia.Net documentation.", noIndex: true));

/// <summary>A documentation page's front matter.</summary>
internal sealed record DocInfo(string Title, string? Description, string? Section);

/// <summary>The documentation pages that exist, read from <c>content/docs</c>, with their front matter.</summary>
internal static class DocsCatalog
{
    private static readonly ConcurrentDictionary<string, Dictionary<string, DocInfo>> Cache = new(StringComparer.Ordinal);

    public static DocInfo? Find(IWebHostEnvironment environment, string slug)
    {
        var root = Path.Combine(environment.ContentRootPath, "content", "docs");
        var pages = Cache.GetOrAdd(root, static path => !Directory.Exists(path)
            ? new Dictionary<string, DocInfo>(StringComparer.Ordinal)
            : Directory.EnumerateFiles(path, "*.mdx", SearchOption.AllDirectories).ToDictionary(
                file => Path.GetRelativePath(path, file).Replace('\\', '/')[..^4], Read, StringComparer.Ordinal));

        return pages.GetValueOrDefault(slug.Trim('/'));
    }

    // The "key: value" lines between the leading "---" fences; values are plain text in this site's pages.
    private static DocInfo Read(string file)
    {
        var fields = File.ReadLines(file).Skip(1).TakeWhile(line => line != "---")
            .Select(line => line.Split(':', 2)).Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim().Trim('"'), StringComparer.Ordinal);
        return new DocInfo(fields.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(file), fields.GetValueOrDefault("description"), fields.GetValueOrDefault("section"));
    }
}

/// <summary>The page's title, description, canonical URL, Open Graph and Twitter tags, set on the server (<c>serverHead</c>).</summary>
internal static class Seo
{
    public static Action<InertiaHead> Tags(HttpContext http, string title, string description, string? section = null, bool noIndex = false) => head =>
    {
        var request = http.Request;
        var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
        var canonical = baseUrl + request.Path;
        var image = baseUrl + "/og.png";
        head.Title(title)
            .Meta("description", description)
            .Link("canonical", canonical)
            .Meta("robots", noIndex ? "noindex, nofollow" : "index, follow")
            .Property("og:site_name", "Inertia.Net")
            .Property("og:type", section is null ? "website" : "article")
            .Property("og:title", title)
            .Property("og:description", description)
            .Property("og:url", canonical)
            .Property("og:image", image)
            .Property("og:image:type", "image/png")
            .Property("og:image:width", "1200")
            .Property("og:image:height", "630")
            .Property("og:image:alt", $"Inertia.Net: {description}")
            .Meta("twitter:card", "summary_large_image")
            .Meta("twitter:title", title)
            .Meta("twitter:description", description)
            .Meta("twitter:image", image);
        if (section is not null)
        {
            head.Property("article:section", section);
        }
    };
}
