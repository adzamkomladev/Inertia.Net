using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Inertia.Net.Benchmarks;

/// <summary>
/// Full <see cref="InertiaResult.ExecuteAsync"/> runs (headers, prop resolution, page buffer, copy to the body). The props are built
/// once, so the numbers are Inertia.Net's own cost; "Build 20 props" is what a handler adds by creating them per request.
/// </summary>
[MemoryDiagnoser]
public class RenderBenchmarks
{
    private static readonly InertiaProps Props = BenchApp.Props20();
    private static readonly UsersPage Page = BenchApp.Poco();
    private static readonly Dictionary<string, object?> PlainPage = BenchApp.PlainPage();

    private DefaultHttpContext _json = null!;
    private DefaultHttpContext _partial = null!;
    private DefaultHttpContext _deferred = null!;
    private DefaultHttpContext _html = null!;
    private readonly ArrayBufferWriter<byte> _buffer = new(16 * 1024);
    private Utf8JsonWriter _writer = null!;
    private JsonTypeInfo _plainInfo = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = BenchApp.CreateServices();
        var options = services.GetRequiredService<InertiaPageWriter>().SerializerOptions;
        _plainInfo = options.GetTypeInfo(typeof(Dictionary<string, object>));
        _writer = new Utf8JsonWriter(_buffer);
        var version = BenchApp.Version(services);
        (string, string) inertia = (InertiaHeaders.Inertia, "true"), versionHeader = (InertiaHeaders.Version, version);
        _json = BenchApp.CreateContext(services, "/users", inertia, versionHeader);
        _partial = BenchApp.CreateContext(services, "/users", inertia, versionHeader,
            (InertiaHeaders.PartialComponent, BenchApp.Component), (InertiaHeaders.PartialData, "users,count"));
        _deferred = BenchApp.CreateContext(services, "/users", inertia, versionHeader,
            (InertiaHeaders.PartialComponent, BenchApp.Component), (InertiaHeaders.PartialData, "stats,activity"));
        _html = BenchApp.CreateContext(services);
    }

    [Benchmark(Description = "JSON, 20 mixed props")]
    public Task Json20Props() => Execute(_json, Render(BenchApp.Component, Props));

    [Benchmark(Description = "JSON, typed POCO props")]
    public Task JsonTypedPoco() => Execute(_json, Render(BenchApp.Component, Page));

    [Benchmark(Description = "Partial reload, 2 of 20")]
    public Task PartialReload() => Execute(_partial, Render(BenchApp.Component, Props));

    [Benchmark(Description = "Deferred group load")]
    public Task DeferredGroup() => Execute(_deferred, Render(BenchApp.Component, Props));

    [Benchmark(Description = "HTML + Vite tags, 20 props")]
    public Task Html20Props() => Execute(_html, Render(BenchApp.Component, Props));

    [Benchmark(Description = "Reference: STJ, same page as plain data")]
    public void PlainSerialize()
    {
        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);
        JsonSerializer.Serialize(_writer, PlainPage, _plainInfo);
        _writer.Flush();
    }

    [Benchmark(Description = "Build 20 props (handler side)")]
    public InertiaProps BuildProps() => BenchApp.Props20();

    private static Task Execute(DefaultHttpContext context, InertiaResult result)
    {
        context.Response.Headers.Clear();
        return result.ExecuteAsync(context);
    }
}

/// <summary>What <c>UseInertia()</c> adds to a request that a plain endpoint answers.</summary>
[MemoryDiagnoser]
public class MiddlewareBenchmarks
{
    private static readonly RequestDelegate Endpoint = context =>
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentLength = 2;
        return Task.CompletedTask;
    };

    private RequestDelegate _pipeline = null!;
    private DefaultHttpContext _plain = null!;
    private DefaultHttpContext _inertia = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = BenchApp.CreateServices();
        var app = new ApplicationBuilder(services);
        app.UseInertia();
        app.Run(Endpoint);
        _pipeline = app.Build();
        _plain = BenchApp.CreateContext(services, "/api/ping");
        _inertia = BenchApp.CreateContext(services, "/users", (InertiaHeaders.Inertia, "true"), (InertiaHeaders.Version, BenchApp.Version(services)));
    }

    [Benchmark(Baseline = true, Description = "Endpoint only")]
    public Task EndpointOnly() => Run(Endpoint, _plain);

    [Benchmark(Description = "UseInertia, non-Inertia request")]
    public Task NonInertiaRequest() => Run(_pipeline, _plain);

    [Benchmark(Description = "UseInertia, Inertia request")]
    public Task InertiaRequest() => Run(_pipeline, _inertia);

    private static Task Run(RequestDelegate pipeline, DefaultHttpContext context)
    {
        context.Response.Headers.Clear();
        return pipeline(context);
    }
}

/// <summary>The partial-reload filter primitives: header parsing and dot-path matching over 20 paths.</summary>
[MemoryDiagnoser]
public class PathMatchingBenchmarks
{
    private static readonly string[] Paths =
    [
        "title", "count", "enabled", "price", "createdAt", "tags", "ids", "user", "user.name", "user.email",
        "users", "filters", "stats", "activity", "chart", "plans", "feed", "settings", "settings.theme", "notifications",
    ];

    private static readonly string[] Only = ["users", "settings.theme"];
    private static readonly string[] Except = ["user.email"];

    private readonly HeaderDictionary _headers = new()
    {
        [InertiaHeaders.Inertia] = "true",
        [InertiaHeaders.PartialComponent] = BenchApp.Component,
        [InertiaHeaders.PartialData] = "users, settings.theme",
        [InertiaHeaders.PartialExcept] = "user.email",
        [InertiaHeaders.Reset] = new StringValues("feed"),
    };

    [Benchmark(Description = "Parse partial headers")]
    public InertiaRequest ParseHeaders() => new(_headers);

    [Benchmark(Description = "Match 20 paths (only + except)")]
    public int MatchPaths()
    {
        var matched = 0;
        foreach (var path in Paths)
        {
            if ((PropsWriter.MatchesAny(Only, path) || PropsWriter.LeadsToAny(Only, path)) && !PropsWriter.MatchesAny(Except, path))
            {
                matched++;
            }
        }

        return matched;
    }
}
