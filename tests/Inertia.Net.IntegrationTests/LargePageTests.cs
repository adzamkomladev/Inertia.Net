using System.Text.Json.Nodes;
using Inertia.Net.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Inertia.Net.IntegrationTests;

/// <summary>A JSON page larger than one write slice arrives whole, with a matching Content-Length.</summary>
public sealed class LargePageTests
{
    [Theory]
    [InlineData(32 * 1024 - 200)]
    [InlineData(200 * 1024 + 7)]
    [InlineData(1536 * 1024 + 3)] // past the pooled size: the page buffer is allocated, not rented
    public async Task A_large_json_page_arrives_whole(int length)
    {
        var text = string.Create(length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = (char)('a' + i % 26);
        });
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddInertia();
        await using var app = builder.Build();
        app.UseInertia();
        app.MapGet("/big", () => Render("Big", new InertiaProps { ["text"] = text }));
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await app.GetTestClient().InertiaGetAsync("/big", cancellationToken: TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
        Assert.Equal(text, JsonNode.Parse(body)!["props"]!["text"]!.GetValue<string>());
    }
}
