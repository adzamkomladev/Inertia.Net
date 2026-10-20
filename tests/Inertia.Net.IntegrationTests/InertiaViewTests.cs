using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Inertia.Net.Mvc;

namespace Inertia.Net.IntegrationTests;

/// <summary><c>this.InertiaView(props)</c> names the component after the controller action.</summary>
public sealed class InertiaViewTests
{
    [Fact]
    public async Task The_component_is_controller_slash_action_with_the_area_first()
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        var client = app.CreateClient();

        var page = await (await client.InertiaGetAsync("/view/users")).PageAsync();
        Assert.Equal("InertiaViewUsers/Index", (string?)page["component"]);
        Assert.Equal(3, (int?)page["props"]!["count"]);
        Assert.Equal("Admin/InertiaViewReports/Show", (string?)(await (await client.InertiaGetAsync("/view/reports")).PageAsync())["component"]);
    }

    [Fact]
    public async Task The_resolver_is_configurable()
    {
        await using var app = await ConformanceApp.StartAsync<CustomHost>();
        var page = await (await app.CreateClient().InertiaGetAsync("/view/users")).PageAsync();
        Assert.Equal("pages/inertiaviewusers.index", (string?)page["component"]);
    }

    public sealed class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services) => services.AddControllers().AddInertiaMvc();

        public static void MapRoutes(WebApplication app) => app.MapControllers();
    }

    public sealed class CustomHost : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services) => services.AddControllers().AddInertiaMvc(o =>
            o.ComponentResolver = c => $"pages/{c.ActionDescriptor.ControllerName}.{c.ActionDescriptor.ActionName}".ToLowerInvariant());

        public static void MapRoutes(WebApplication app) => app.MapControllers();
    }
}

public sealed class InertiaViewUsersController : ControllerBase
{
    [HttpGet("/view/users")]
    public IResult Index() => this.InertiaView(new InertiaProps { ["count"] = 3 });
}

[Area("Admin")]
public sealed class InertiaViewReportsController : ControllerBase
{
    [HttpGet("/view/reports")]
    public IResult Show() => this.InertiaView();
}
