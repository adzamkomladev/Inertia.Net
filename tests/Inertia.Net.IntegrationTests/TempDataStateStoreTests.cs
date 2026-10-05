using System.Net;
using System.Text;
using Inertia.Net.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

/// <summary>Conformance hosts that keep the redirect state in TempData (cookie or session provider), for MVC controllers and Minimal API endpoints.</summary>
public sealed class MvcTempDataCookieHost : IConformanceHost
{
    public static bool StateInCookie => false;

    public static void ConfigureServices(IServiceCollection services) =>
        services.AddControllersWithViews().AddInertiaMvc(o => o.UseTempDataStateStore());

    public static void MapRoutes(WebApplication app) => app.MapControllers();
}

public sealed class MvcTempDataSessionHost : IConformanceHost
{
    public static bool UsesSession => true;

    public static bool StateInCookie => false;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddDistributedMemoryCache();
        services.AddSession();
        services.AddControllersWithViews().AddSessionStateTempDataProvider().AddInertiaMvc(o => o.UseTempDataStateStore());
    }

    public static void MapRoutes(WebApplication app) => app.MapControllers();
}

public sealed class MinimalApiTempDataCookieHost : IConformanceHost
{
    public static bool StateInCookie => false;

    public static void ConfigureServices(IServiceCollection services)
    {
        MinimalApiHost.ConfigureServices(services);
        services.AddControllersWithViews().AddInertiaMvc(o => o.UseTempDataStateStore());
    }

    public static void MapRoutes(WebApplication app) => MinimalApiHost.MapRoutes(app);
}

public sealed class MinimalApiTempDataSessionHost : IConformanceHost
{
    public static bool UsesSession => true;

    public static bool StateInCookie => false;

    public static void ConfigureServices(IServiceCollection services)
    {
        MinimalApiHost.ConfigureServices(services);
        services.AddDistributedMemoryCache();
        services.AddSession();
        services.AddControllersWithViews().AddSessionStateTempDataProvider().AddInertiaMvc(o => o.UseTempDataStateStore());
    }

    public static void MapRoutes(WebApplication app) => MinimalApiHost.MapRoutes(app);
}

public sealed class MvcTempDataCookieConformanceTests : ProtocolConformanceTests<MvcTempDataCookieHost>;

public sealed class MvcTempDataSessionConformanceTests : ProtocolConformanceTests<MvcTempDataSessionHost>;

public sealed class MinimalApiTempDataCookieConformanceTests : ProtocolConformanceTests<MinimalApiTempDataCookieHost>;

public sealed class MinimalApiTempDataSessionConformanceTests : ProtocolConformanceTests<MinimalApiTempDataSessionHost>;

/// <summary>The TempData store itself, and the Minimal API flow (no MVC filter saves TempData there) with both providers.</summary>
public sealed class TempDataStateStoreTests
{
    [Fact]
    public void Option_replaces_the_default_store()
    {
        var services = new ServiceCollection().AddInertia();
        services.AddControllersWithViews().AddInertiaMvc(o => o.UseTempDataStateStore());
        using var provider = services.BuildServiceProvider();
        Assert.IsType<TempDataInertiaStateStore>(provider.GetRequiredService<IInertiaStateStore>());
    }

    [Fact]
    public async Task Save_load_and_clear_round_trip_through_the_request_tempdata()
    {
        await using var provider = new ServiceCollection().AddControllersWithViews().Services.BuildServiceProvider();
        var store = new TempDataInertiaStateStore(provider.GetRequiredService<ITempDataDictionaryFactory>());
        var context = new DefaultHttpContext { RequestServices = provider };
        Assert.Null(await store.LoadAsync(context));

        await store.SaveAsync(context, Encoding.UTF8.GetBytes("""{"flash":{"a":1}}"""));
        Assert.Equal("""{"flash":{"a":1}}""", Encoding.UTF8.GetString((await store.LoadAsync(context))!));

        await store.ClearAsync(context);
        Assert.Null(await store.LoadAsync(context));
    }

    [Fact]
    public async Task Undecodable_state_loads_as_empty_so_it_is_cleared()
    {
        await using var provider = new ServiceCollection().AddControllersWithViews().Services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ITempDataDictionaryFactory>();
        var context = new DefaultHttpContext { RequestServices = provider };
        factory.GetTempData(context)[TempDataInertiaStateStore.Key] = "%%not base64%%";
        Assert.Empty((await new TempDataInertiaStateStore(factory).LoadAsync(context))!);
    }

    [Fact]
    public async Task Cookie_provider_emits_the_set_cookie_on_a_bodyless_redirect()
    {
        await using var app = await ConformanceApp.StartAsync<MinimalApiTempDataCookieHost>();
        var response = await app.CreateClient().InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" });
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(response.Headers.GetValues("Set-Cookie"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Minimal_api_validation_redirect_renders_errors_then_consumes_them(bool session)
    {
        await using var app = await StartAsync(session);
        var client = app.CreateClient();

        var failed = await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "" });
        Assert.Equal(HttpStatusCode.Found, failed.StatusCode);
        Assert.Single((await (await client.InertiaGetAsync("/page")).PageAsync())["props"]!["errors"]!.AsObject());
        JsonAssert.Equal("{}", (await (await client.InertiaGetAsync("/page")).PageAsync())["props"]!["errors"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Flash_survives_chained_redirects_and_a_409_keeps_state(bool session)
    {
        await using var app = await StartAsync(session);
        var client = app.CreateClient();

        await client.InertiaAsync(HttpMethod.Post, "/form", new { name = "Ann" });

        // A fragment redirect is a 409 that carries the state on, like a redirect chain.
        var chained = await client.InertiaGetAsync("/fragment-redirect");
        Assert.Equal(HttpStatusCode.Conflict, chained.StatusCode);

        // A version mismatch answers 409 before the handler: the state must survive it.
        var stale = await client.InertiaGetAsync("/page", r => r.Headers.Add(InertiaHeaders.Version, "stale"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var page = await (await client.InertiaGetAsync("/page")).PageAsync();
        JsonAssert.Equal("""{"success":"Saved Ann"}""", page["flash"]);
        JsonAssert.Missing(await (await client.InertiaGetAsync("/page")).PageAsync(), "flash");
    }

    private static Task<ConformanceApp> StartAsync(bool session) =>
        session ? ConformanceApp.StartAsync<MinimalApiTempDataSessionHost>() : ConformanceApp.StartAsync<MinimalApiTempDataCookieHost>();
}
