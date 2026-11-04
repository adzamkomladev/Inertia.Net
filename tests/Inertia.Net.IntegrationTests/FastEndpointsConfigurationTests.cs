using System.Runtime.CompilerServices;
using FastEndpoints;
using Inertia.Net.FastEndpoints;

namespace Inertia.Net.IntegrationTests;

[Collection("FastEndpoints")]
public sealed class FastEndpointsConfigurationTests
{
    [Fact]
    public void The_existing_configurator_runs_before_the_optional_Inertia_callback()
    {
        var config = new Config();
        var original = GetConfigurator(config.Endpoints);
        try
        {
            var events = new List<string>();
            var endpoint = new EndpointDefinition(typeof(FastEndpointsRoutes.PageEndpoint), typeof(EmptyRequest), typeof(object));
            config.Endpoints.Configurator = definition =>
            {
                Assert.Same(endpoint, definition);
                events.Add("previous");
            };
            config.ConfigureInertia(definition =>
            {
                Assert.Same(endpoint, definition);
                events.Add("optional");
            });
            // Endpoint discovery is cached across test hosts. A fresh definition avoids depending on which host started first.
            GetConfigurator(config.Endpoints)!(endpoint);
            Assert.Equal(["previous", "optional"], events);
        }
        finally
        {
            config.Endpoints.Configurator = original;
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_" + nameof(EndpointOptions.Configurator))]
    private static extern Action<EndpointDefinition>? GetConfigurator(EndpointOptions options);
}
