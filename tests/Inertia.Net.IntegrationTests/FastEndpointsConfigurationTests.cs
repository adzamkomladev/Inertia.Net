using System.Collections.Concurrent;
using System.Net;
using FastEndpoints;
using Inertia.Net.FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.IntegrationTests;

[Collection("FastEndpoints")]
public sealed class FastEndpointsConfigurationTests
{
    [Fact]
    public async Task The_existing_configurator_runs_before_the_optional_Inertia_callback()
    {
        await using var app = await ConformanceApp.StartAsync<Host>();
        using var client = app.CreateClient();
        using var response = await client.InertiaGetAsync("/page");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = app.App.Services.GetRequiredService<Calls>().Events;
        Assert.NotEmpty(events);
        foreach (var endpoint in events.GroupBy(entry => entry.Definition))
            Assert.Equal(["previous", "optional"], endpoint.Select(entry => entry.Stage));
    }

    public sealed class Calls
    {
        public ConcurrentQueue<(EndpointDefinition Definition, string Stage)> Events { get; } = new();
    }

    public sealed class Host : IConformanceHost
    {
        public static void ConfigureServices(IServiceCollection services)
        {
            FastEndpointsHost.ConfigureServices(services);
            services.AddSingleton<Calls>();
        }

        public static void MapRoutes(WebApplication app)
        {
            var calls = app.Services.GetRequiredService<Calls>();
            app.UseFastEndpoints(config =>
            {
                config.Endpoints.Configurator = endpoint => calls.Events.Enqueue((endpoint, "previous"));
                config.UseInertia(endpoint =>
                {
                    calls.Events.Enqueue((endpoint, "optional"));
                    endpoint.AllowAnonymous();
                });
            });
        }
    }
}
