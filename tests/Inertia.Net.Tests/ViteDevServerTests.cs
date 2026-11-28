using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Inertia.Net.Tests;

public sealed class ViteDevServerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("inertia-vite-dev-").FullName;

    // Stands in for Vite: writes the hot file, then keeps running until it is killed.
    private const string Script = "node -e \\\"const fs=require('fs');fs.mkdirSync('wwwroot',{recursive:true});fs.writeFileSync('wwwroot/hot','http://127.0.0.1:1');setInterval(()=>{},1000)\\\"";

    public void Dispose()
    {
        // On Windows the killed node process can hold the folder for a moment after the tree is gone.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(_root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(250);
            }
        }
    }

    private ViteDevServer Create(string environment, bool start = true)
    {
        File.WriteAllText(Path.Join(_root, "package.json"), $$"""{ "scripts": { "dev": "{{Script}}" } }""");
        var options = Options.Create(new InertiaOptions { Vite = { StartDevServer = start } });
        var host = new HostingEnvironment { EnvironmentName = environment, ContentRootPath = _root };
        return new ViteDevServer(options, new ViteAssets(options, host, TimeProvider.System), host, NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task Starts_the_dev_script_waits_for_the_hot_file_and_removes_it_on_stop()
    {
        using var server = Create("Development");
        var hot = Path.Join(_root, "wwwroot", "hot");
        Directory.CreateDirectory(Path.GetDirectoryName(hot)!);
        File.WriteAllText(hot, "http://127.0.0.1:1"); // stale: nothing listens there

        await server.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(hot));

        await server.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(File.Exists(hot));
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task Does_nothing_outside_development_or_when_off(string environment, bool start)
    {
        using var server = Create(environment, start);
        await server.StartAsync(TestContext.Current.CancellationToken);
        Assert.False(File.Exists(Path.Join(_root, "wwwroot", "hot")));
    }
}
