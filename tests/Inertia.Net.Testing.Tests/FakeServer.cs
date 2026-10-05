using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Inertia.Net.Testing.Tests;

/// <summary>A hand-written fake Inertia v3 server (no dependency on Inertia.Net core) that honors partial headers.</summary>
internal sealed class FakeServer : IAsyncDisposable
{
    public sealed record Recorded(string Method, string PathAndQuery, Dictionary<string, string> Headers);

    private readonly WebApplication _app;
    public HttpClient Client { get; }
    public List<Recorded> Requests { get; } = [];

    private FakeServer(WebApplication app) { _app = app; Client = app.GetTestClient(); }

    public static async Task<FakeServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        FakeServer? self = null;
        app.Run(c => { self!.Requests.Add(new(c.Request.Method, c.Request.Path + c.Request.QueryString.Value,
            c.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase))); return Handle(c); });
        await app.StartAsync();
        return self = new FakeServer(app);
    }

    public ValueTask DisposeAsync() { Client.Dispose(); return _app.DisposeAsync(); }

    private static string[]? List(HttpContext c, string header) =>
        c.Request.Headers.TryGetValue(header, out var v) && v.ToString() is { Length: > 0 } s ? s.Split(',') : null;

    private static async Task Handle(HttpContext c)
    {
        switch (c.Request.Path.Value)
        {
            case "/users": await Page(c, honorPartial: true); break;
            case "/stubborn": await Page(c, honorPartial: false); break;
            case "/html/attr-order": await Html(c, "<script type=\"application/json\" data-page=\"app\">{0}</script><div id=\"app\"></div>"); break;
            case "/html/custom-id": await Html(c, "<script id=\"x\" data-page='custom' type=\"application/json\">{0}</script>"); break;
            case "/html/after-other-scripts":
                await Html(c, "<script src=\"/app.js\"></script><script>var a = 1;</script>\n<script data-page=\"app\" type=\"application/json\">\n{0}\n</script>"); break;
            case "/plain": await c.Response.WriteAsync("hello"); break;
            case "/bad-json": c.Response.Headers["X-Inertia"] = "true"; await c.Response.WriteAsync("oops"); break;
            case "/incomplete": c.Response.Headers["X-Inertia"] = "true"; await c.Response.WriteAsync("{\"foo\":1}"); break;
            default: c.Response.StatusCode = 404; await c.Response.WriteAsync("Not here"); break;
        }
    }

    private static JsonObject Build(HttpContext c, bool honorPartial)
    {
        var partial = honorPartial && c.Request.Headers["X-Inertia-Partial-Component"] == "Users/Index";
        var only = List(c, "X-Inertia-Partial-Data");
        var except = List(c, "X-Inertia-Partial-Except");
        var all = new JsonObject
        {
            ["errors"] = new JsonObject(),
            ["users"] = new JsonArray(new JsonObject { ["id"] = 1, ["name"] = "Ann" }, new JsonObject { ["id"] = 2, ["name"] = "Bob" }),
            ["title"] = "Users",
            ["evil"] = "</script><b>&",
            ["big"] = new JsonObject { ["$bigint"] = "9007199254740993" },
            ["stats"] = new JsonObject { ["total"] = 2 },
            ["plans"] = new JsonArray("free", "pro"),
        };
        var props = new JsonObject();
        foreach (var (key, value) in all.ToArray())
        {
            var deferred = key is "stats" or "plans";
            var include = partial
                ? (only is null || only.Contains(key)) && (except is null || !except.Contains(key))
                : !deferred;
            if (include) { all.Remove(key); props[key] = value; }
        }
        var page = new JsonObject
        {
            ["component"] = "Users/Index",
            ["props"] = props,
            ["url"] = c.Request.Path + c.Request.QueryString.Value,
            ["version"] = "v1",
        };
        if (!partial) page["deferredProps"] = new JsonObject { ["sidebar"] = new JsonArray("stats"), ["default"] = new JsonArray("plans") };
        page["preserveBigIntegers"] = true;
        page["flash"] = new JsonObject { ["toast"] = "Saved" };
        return page;
    }

    private static async Task Page(HttpContext c, bool honorPartial)
    {
        var json = Build(c, honorPartial).ToJsonString();
        if (c.Request.Headers.ContainsKey("X-Inertia"))
        {
            c.Response.ContentType = "application/json";
            c.Response.Headers["X-Inertia"] = "true";
            await c.Response.WriteAsync(json);
        }
        else await Html(c, "<div id=\"app\"></div><script data-page=\"app\" type=\"application/json\">{0}</script>", json);
    }

    private static Task Html(HttpContext c, string template, string? json = null)
    {
        c.Response.ContentType = "text/html";
        return c.Response.WriteAsync("<html><body>" + template.Replace("{0}", json ?? Build(c, true).ToJsonString()) + "</body></html>");
    }
}
