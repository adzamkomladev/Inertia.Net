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
}
