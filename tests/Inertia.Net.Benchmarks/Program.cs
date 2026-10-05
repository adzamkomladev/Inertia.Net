using System.Text;
using BenchmarkDotNet.Running;
using Inertia.Net.Benchmarks;

// `dotnet run -c Release -- --check` prints what each render benchmark produces, to verify the setup before measuring.
if (args is ["--check"])
{
    var bench = new RenderBenchmarks();
    bench.Setup();
    foreach (var (name, run) in new (string, Func<Microsoft.AspNetCore.Http.HttpContext, Task>)[]
    {
        ("json", c => Render(BenchApp.Component, BenchApp.Props20()).ExecuteAsync(c)),
        ("poco", c => Render(BenchApp.Component, BenchApp.Poco()).ExecuteAsync(c)),
    })
    {
        var context = BenchApp.CreateContext(BenchApp.CreateServices(), "/users", (InertiaHeaders.Inertia, "true"));
        context.Response.Body = new MemoryStream();
        await run(context);
        Console.WriteLine($"{name}: {context.Response.StatusCode} {Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())}\n");
    }

    var html = BenchApp.CreateContext(BenchApp.CreateServices());
    html.Response.Body = new MemoryStream();
    await Render(BenchApp.Component, BenchApp.Props20()).ExecuteAsync(html);
    Console.WriteLine($"html: {Encoding.UTF8.GetString(((MemoryStream)html.Response.Body).ToArray())}");
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
