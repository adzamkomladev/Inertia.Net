using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Inertia.Net.IntegrationTests;

/// <summary>Refresh work must not hold Kestrel's HTTP/1.1 connection in response completion.</summary>
public sealed class SsrRefreshConnectionTests
{
    [Fact]
    public async Task A_stale_refresh_does_not_delay_the_next_request_on_the_same_connection()
    {
        var root = Path.Combine(Path.GetTempPath(), "inertia-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "app.html"), "<html><head>@inertiaHead</head><body>@inertia</body></html>", TestContext.Current.CancellationToken);
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = Environments.Production });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var time = new FakeTimeProvider();
            using var renderer = new GatedRenderer();
            builder.Services.AddSingleton<TimeProvider>(time);
            builder.Services.AddInertia(o =>
            {
                o.Ssr.Mode = SsrMode.Preferred;
                o.Ssr.Cache.Duration = TimeSpan.FromMinutes(1);
                o.Ssr.Cache.StaleFor = TimeSpan.FromMinutes(5);
            });
            builder.Services.AddHttpClient(InertiaSsr.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => renderer);
            await using var app = builder.Build();
            app.MapGet("/", () => Render("Home"));
            app.MapGet("/ping", () => Results.Text("pong"));
            await app.StartAsync(TestContext.Current.CancellationToken);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 1, UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(address), DefaultRequestVersion = HttpVersion.Version11, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
            try
            {
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
                time.Advance(TimeSpan.FromMinutes(2));
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
                await renderer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal("pong", await client.GetStringAsync("/ping", TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
                Assert.Equal(2, renderer.Calls);
            }
            finally
            {
                renderer.Release.TrySetResult();
                await app.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class GatedRenderer : HttpMessageHandler
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref Calls) > 1)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"head":[],"body":"<div>rendered</div>"}""") };
        }
    }
}
