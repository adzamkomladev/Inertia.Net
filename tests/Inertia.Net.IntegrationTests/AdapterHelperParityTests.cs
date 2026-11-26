using System.Net;
using Inertia.Net.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

public sealed class AdapterHelperParityTests
{
    [Fact]
    public async Task Reload_helpers_exercise_real_optional_lazy_and_deferred_loaders()
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        using var client = app.CreateClient();
        using var initial = await client.InertiaGetAsync("/helpers");
        var page = await initial.GetInertiaPageAsync(TestContext.Current.CancellationToken);
        Assert.False(page.HasProp("optional"));
        var counts = app.App.Services.GetRequiredService<Counts>();
        Assert.Equal((1, 0, 0), (counts.Lazy, counts.Optional, counts.Deferred));
        var partial = await page.ReloadExceptAsync(client, "lazy", "stats");
        Assert.Equal("optional", (string?)partial.Prop("optional"));
        Assert.False(partial.HasProp("lazy"));
        Assert.Equal((1, 1, 0), (counts.Lazy, counts.Optional, counts.Deferred));
        var deferred = await page.LoadDeferredPropsAsync(client, "sidebar");
        Assert.Equal("deferred", (string?)deferred.Prop("stats"));
        Assert.Equal((1, 1, 1), (counts.Lazy, counts.Optional, counts.Deferred));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Final_page_flash_assertions_work_after_real_redirect_hops(int hops)
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        using var client = app.CreateClient();
        using var start = await client.InertiaGetAsync("/flash/" + hops);
        Assert.Equal(HttpStatusCode.Found, start.StatusCode);
        var location = start.Headers.Location!.OriginalString;
        while (true)
        {
            using var response = await client.InertiaGetAsync(location);
            if (response.Headers.Location is { } next) { location = next.OriginalString; continue; }
            await response.AssertInertiaAsync(page => page.Component("Helpers").HasFlash("toast", "saved"), TestContext.Current.CancellationToken);
            break;
        }
        using var consumed = await client.InertiaGetAsync("/helpers");
        await consumed.AssertInertiaAsync(page => page.MissingFlash("toast"), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Plain_empty_responses_and_html_are_not_changed_into_inertia_pages()
    {
        await using var app = await ConformanceApp.StartAsync<Host>(new ConformanceSetup
        {
            Services = services => services.AddInertiaDevTools(options => { options.Enabled = true; options.Authorize = _ => true; }),
        });
        using var client = app.CreateClient();
        using var empty = await client.GetAsync("/empty-helper", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Null(empty.Headers.Location);
        Assert.Null(empty.Header(InertiaHeaders.Redirect));
        Assert.Empty(await empty.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var html = await client.GetAsync("/html-helper", TestContext.Current.CancellationToken);
        Assert.Equal("<h1>ordinary</h1>", await html.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var json = await client.InertiaGetAsync("/helpers");
        Assert.DoesNotContain("data-inertia-devtools-id", await json.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    private sealed class Counts
    {
        internal int Lazy;
        internal int Optional;
        internal int Deferred;
    }

    public sealed class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services) => services.AddSingleton(new Counts());
        public static void MapRoutes(WebApplication app)
        {
            var counts = app.Services.GetRequiredService<Counts>();
            app.MapGet("/helpers", () => Render("Helpers", new InertiaProps
            {
                ["lazy"] = Prop(() => { counts.Lazy++; return "lazy"; }),
                ["optional"] = Optional(() => { counts.Optional++; return "optional"; }),
                ["stats"] = Defer(() => { counts.Deferred++; return "deferred"; }, "sidebar"),
            }));
            app.MapGet("/flash/{hops:int}", (int hops) => Back(hops > 1 ? "/hop-helper" : "/helpers").WithFlash("toast", "saved"));
            app.MapGet("/hop-helper", () => Results.Redirect("/helpers"));
            app.MapGet("/empty-helper", () => Results.Empty);
            app.MapGet("/html-helper", () => Results.Content("<h1>ordinary</h1>", "text/html"));
        }
    }
}
