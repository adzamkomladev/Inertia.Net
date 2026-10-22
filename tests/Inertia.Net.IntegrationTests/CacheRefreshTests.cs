using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Inertia.Net.IntegrationTests;

/// <summary>Stale-while-revalidate through the real request pipeline: the refresh runs after the response, with the request's services.</summary>
public sealed class CacheRefreshTests
{
    [Fact]
    public async Task A_stale_prop_is_refreshed_after_its_response_with_the_request_services_alive()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        var time = new FakeTimeProvider();
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddInertia();
        builder.Services.AddSingleton<Loads>();
        builder.Services.AddScoped<RequestDb>();
        await using var app = builder.Build();
        app.MapGet("/stats", (RequestDb db) => Render("Stats", new InertiaProps
        {
            ["n"] = Prop(ct => db.LoadAsync()).Cache("stats", TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromMinutes(5)),
        }));
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();
        var loads = app.Services.GetRequiredService<Loads>();

        async Task<int?> GetAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/stats") { Headers = { { InertiaHeaders.Inertia, "true" } } };
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            var page = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
            return (int?)page["props"]!["n"];
        }

        Assert.Equal(1, await GetAsync());
        time.Advance(TimeSpan.FromMinutes(2));

        // The refresh's loader waits on the gate, so this response arriving at all shows the loader runs after it.
        Assert.Equal(1, await GetAsync());
        loads.Gate.SetResult();
        await loads.Refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // The refresh stores the value once its loader has returned; until then requests still get the stale one. One of them may read it
        // just before the store and claim a refresh just after, which then finds the value fresh and does not run the loader again.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        int? n;
        while ((n = await GetAsync()) == 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, n);
        Assert.Equal(2, loads.Count);
    }

    private sealed class Loads
    {
        public int Count;

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Refreshed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RequestDb(Loads loads) : IDisposable
    {
        private bool _disposed;

        public async Task<int> LoadAsync()
        {
            var n = Interlocked.Increment(ref loads.Count);
            if (n > 1)
            {
                await loads.Gate.Task;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            if (n > 1)
            {
                loads.Refreshed.TrySetResult();
            }

            return n;
        }

        public void Dispose() => _disposed = true;
    }
}
