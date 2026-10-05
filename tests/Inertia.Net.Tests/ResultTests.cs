using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Tests;

/// <summary>HTTP behavior of <see cref="InertiaResult"/> and <see cref="InertiaLocationResult"/>.</summary>
public class ResultTests
{
    private readonly Harness _harness = new();

    [Fact]
    public async Task Inertia_requests_get_a_json_page()
    {
        var context = _harness.Context().AsInertia();
        var body = await Harness.ExecuteAsync(context, Inertia.Render("Home", new InertiaProps { ["a"] = 1 }));

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        Assert.Equal("true", context.Response.Headers[InertiaHeaders.Inertia]);
        Assert.Equal("X-Inertia", context.Response.Headers.Vary);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(body), context.Response.ContentLength);
        Assert.Equal("Home", (string?)Harness.ParsePage(body)["component"]);
    }

    [Fact]
    public async Task Other_requests_get_the_root_view_with_the_embedded_page()
    {
        var context = _harness.Context();
        var body = await Harness.ExecuteAsync(context, Inertia.Render("Home", new InertiaProps { ["a"] = 1 }));

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
        Assert.False(context.Response.Headers.ContainsKey(InertiaHeaders.Inertia));
        Assert.Equal("X-Inertia", context.Response.Headers.Vary);
        Assert.StartsWith("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body><script data-page=\"app\" type=\"application/json\">{", body, StringComparison.Ordinal);
        Assert.EndsWith("}</script><div id=\"app\"></div></body></html>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_element_id_is_configurable_and_html_encoded()
    {
        var context = new Harness(o => o.RootElementId = "my\"root").Context();
        var body = await Harness.ExecuteAsync(context, Inertia.Render("Home"));
        Assert.Contains("<script data-page=\"my&quot;root\" type=\"application/json\">", body, StringComparison.Ordinal);
        Assert.Contains("<div id=\"my&quot;root\"></div>", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Status_code_can_be_overridden(bool inertia)
    {
        var context = _harness.Context();
        if (inertia)
        {
            context.AsInertia();
        }

        var result = Inertia.Render("Error", new InertiaProps { ["status"] = 503 }).WithStatusCode(503);
        await Harness.ExecuteAsync(context, result);

        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal(503, result.StatusCode);
    }

    [Fact]
    public async Task Existing_vary_values_are_kept()
    {
        var context = _harness.Context().AsInertia();
        context.Response.Headers.Vary = "Accept-Encoding";
        await Harness.ExecuteAsync(context, Inertia.Render("Home"));
        Assert.Equal(["Accept-Encoding", "X-Inertia"], context.Response.Headers.Vary.ToArray());

        var again = _harness.Context();
        again.Response.Headers.Vary = "accept, x-inertia";
        await Harness.ExecuteAsync(again, Inertia.Render("Home"));
        Assert.Equal("accept, x-inertia", again.Response.Headers.Vary);
    }

    [Fact]
    public async Task Root_view_receives_the_view_name_view_data_and_page()
    {
        var view = new CapturingRootView();
        var harness = new Harness(o => o.RootView = "default.html", configureServices: s => s.AddSingleton<IInertiaRootView>(view));

        await Harness.ExecuteAsync(harness.Context(), Inertia.Render("Home", new InertiaProps { ["a"] = 1 }).WithRootView("admin.html").WithViewData("title", "Admin"));

        Assert.Equal("admin.html", view.RootView);
        Assert.Equal("Home", view.Component);
        Assert.Equal("Admin", view.ViewData!["title"]);
        Assert.Equal(1, (int)Harness.ParsePage(view.PageJson!)["props"]!["a"]!);
        Assert.Equal($"<script data-page=\"app\" type=\"application/json\">{view.PageJson}</script><div id=\"app\"></div>", view.BodyHtml);

        await Harness.ExecuteAsync(harness.Context(), Inertia.Render("Home"));
        Assert.Equal("default.html", view.RootView);
        Assert.Empty(view.ViewData!);
    }

    [Fact]
    public async Task Missing_registration_gives_a_helpful_error()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Inertia.Render("Home").ExecuteAsync(context));
        Assert.Contains("AddInertia", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Location_is_a_409_for_inertia_requests_and_a_302_otherwise()
    {
        var inertia = _harness.Context().AsInertia();
        await Harness.ExecuteAsync(inertia, Inertia.Location("https://example.com/away"));
        Assert.Equal(409, inertia.Response.StatusCode);
        Assert.Equal("https://example.com/away", inertia.Response.Headers[InertiaHeaders.Location]);
        Assert.Equal(0, inertia.Response.Body.Length);

        var plain = _harness.Context();
        await Harness.ExecuteAsync(plain, Inertia.Location("https://example.com/away"));
        Assert.Equal(302, plain.Response.StatusCode);
        Assert.Equal("https://example.com/away", plain.Response.Headers.Location);
        Assert.False(plain.Response.Headers.ContainsKey(InertiaHeaders.Location));
    }

    [Fact]
    public async Task Parallel_requests_do_not_share_per_request_state()
    {
        var harness = new Harness(o => o.Share("shared", Inertia.Prop(async ct =>
        {
            await Task.Yield();
            return "static";
        })));

        var pages = await Task.WhenAll(Enumerable.Range(0, 64).Select(async i =>
        {
            await Task.Yield();
            var page = await harness.PageAsync(
                new InertiaProps { ["n"] = Inertia.Prop(async ct => { await Task.Yield(); return i; }) },
                c =>
                {
                    var feature = c.Inertia().Share("mine", i).Flash("n", i).EncryptHistory(i % 2 == 0);
                    if (i % 3 == 0)
                    {
                        feature.WithErrors(new Dictionary<string, string> { ["field"] = $"error {i}" });
                    }

                    if (i % 2 == 1)
                    {
                        c.AsInertia();
                    }
                });
            return (i, page);
        }));

        foreach (var (i, page) in pages)
        {
            Assert.Equal(i, (int)page["props"]!["n"]!);
            Assert.Equal(i, (int)page["props"]!["mine"]!);
            Assert.Equal("static", (string?)page["props"]!["shared"]);
            Assert.Equal(i, (int)page["flash"]!["n"]!);
            Assert.Equal(i % 2 == 0, page.ContainsKey("encryptHistory"));
            JsonAssert.Equal(i % 3 == 0 ? $$"""{"field":"error {{i}}"}""" : "{}", page["props"]!["errors"]);
        }
    }

    [Fact]
    public void Endpoint_metadata_describes_html_and_json()
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);
        Populate<InertiaResult>(typeof(ResultTests).GetMethod(nameof(Endpoint_metadata_describes_html_and_json))!, builder);

        var metadata = Assert.Single(builder.Metadata.OfType<IProducesResponseTypeMetadata>());
        Assert.Equal(200, metadata.StatusCode);
        Assert.Equal(["text/html", "application/json"], metadata.ContentTypes);
    }

    private static void Populate<T>(MethodInfo method, Microsoft.AspNetCore.Builder.EndpointBuilder builder)
        where T : IEndpointMetadataProvider => T.PopulateMetadata(method, builder);

    private sealed class CapturingRootView : IInertiaRootView
    {
        public string? RootView { get; private set; }

        public string? Component { get; private set; }

        public IReadOnlyDictionary<string, object?>? ViewData { get; private set; }

        public string? PageJson { get; private set; }

        public string? BodyHtml { get; private set; }

        public ValueTask RenderAsync(InertiaRootViewContext context)
        {
            RootView = context.RootView;
            Component = context.Component;
            ViewData = context.ViewData;
            PageJson = System.Text.Encoding.UTF8.GetString(context.PageJson.Span);
            BodyHtml = context.GetBodyHtml();
            return ValueTask.CompletedTask;
        }
    }
}
