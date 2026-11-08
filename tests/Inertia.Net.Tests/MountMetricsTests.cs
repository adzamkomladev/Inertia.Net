using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Tests;

public sealed class MountMetricsTests
{
    [Fact]
    public void Page_metrics_carry_the_mount_tag_only_for_named_mounts()
    {
        var recorded = new ConcurrentDictionary<long, string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Name == "inertia.page.size") current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, bytes, tags, _) =>
        {
            string? mount = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "inertia.mount") mount = tag.Value as string;
            }

            recorded[bytes] = mount;
        });
        listener.Start();
        using var services = new ServiceCollection().AddInertia().BuildServiceProvider();
        var metrics = services.GetRequiredService<InertiaMetrics>();

        metrics.RecordPage(0, 987_654_321, json: true, partial: false, "sp");
        metrics.RecordPage(0, 987_654_322, json: true, partial: false);

        Assert.Equal("sp", recorded[987_654_321]);
        Assert.Null(recorded[987_654_322]);
    }

    [Fact]
    public void Each_named_mount_gets_its_own_page_writer_and_ssr_gateway()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ContentRootPath = Path.GetTempPath(), EnvironmentName = "Production" })
            .AddInertia(o => o.Version = "host")
            .AddInertia("a", o => o.Version = "a")
            .AddInertia("b", o => o.Version = "b")
            .AddInertia("a", o => o.RootView = "a.html") // a second call adds to the mount
            .BuildServiceProvider();

        var a = services.GetRequiredKeyedService<InertiaPageWriter>("a");
        Assert.Equal("a", a.Options.Version);
        Assert.Equal("a.html", a.Options.RootView);
        Assert.Equal("a", a.Options.Mount);
        Assert.Equal("b", services.GetRequiredKeyedService<InertiaPageWriter>("b").Options.Version);
        Assert.Null(services.GetRequiredService<InertiaPageWriter>().Options.Mount);
        Assert.NotSame(services.GetRequiredKeyedService<SsrGateway>("a"), services.GetRequiredKeyedService<SsrGateway>("b"));
        // One SSR process supervisor and TypeScript writer per mount, plus the default mount's.
        Assert.Equal(3, services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<SsrNodeProcess>().Count());
        Assert.Equal(3, services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<InertiaTypeScriptWriter>().Count());
    }
}
