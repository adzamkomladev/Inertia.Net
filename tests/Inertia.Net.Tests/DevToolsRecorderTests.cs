using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inertia.Net.Tests;

public sealed class DevToolsRecorderTests
{
    [Theory]
    [InlineData("Production", true, false, true)]
    [InlineData("Staging", true, false, true)]
    [InlineData("Development", true, false, false)]
    [InlineData("Production", false, false, false)]
    [InlineData("Production", null, false, false)]
    [InlineData("Production", true, true, false)]
    public void Recording_without_a_reader_warns_only_when_enabled_outside_development(string environment, bool? enabled, bool authorize, bool warns)
    {
        var harness = new Harness(environment: environment, configureServices: services => services.AddInertiaDevTools(options =>
        {
            options.Enabled = enabled;
            if (authorize) options.Authorize = _ => true;
        }));
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        Assert.Equal(enabled ?? environment == "Development", recorder.Enabled);
        Assert.Equal(warns ? 1 : 0, harness.Logs.Entries.Count(entry => entry.Level == LogLevel.Warning));
        if (warns)
        {
            Assert.Contains("InertiaDevToolsOptions.Authorize", Assert.Single(harness.Logs.Entries).Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("127.0.0.1", "localhost", StatusCodes.Status200OK)]
    [InlineData("::1", "[::1]:5000", StatusCodes.Status200OK)]
    [InlineData(null, "app.localhost", StatusCodes.Status200OK)]
    [InlineData("10.0.0.5", "localhost", StatusCodes.Status404NotFound)]
    [InlineData("127.0.0.1", "attacker.example", StatusCodes.Status404NotFound)]
    public async Task Without_a_reader_development_serves_entries_only_to_this_machine(string? remote, string host, int status)
    {
        var harness = new Harness(environment: "Development", configureServices: services => services.AddInertiaDevTools());
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        var context = new DefaultHttpContext { RequestServices = harness.Services };
        context.Connection.RemoteIpAddress = remote is null ? null : IPAddress.Parse(remote);
        context.Request.Host = HostString.FromUriComponent(host);
        context.Response.Body = new MemoryStream();

        await recorder.ListEntriesAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
    }
}
