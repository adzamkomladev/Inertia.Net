using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Benchmarks;

[MemoryDiagnoser]
public class CacheWriteBenchmarks
{
    [Params(1024, 1048576)] public int PayloadBytes { get; set; }
    private ServiceProvider _services = null!;
    private InertiaPageWriter _writer = null!;
    private string _value = null!;
    private readonly PropCaching _caching = PropCaching.Create("large", TimeSpan.FromMinutes(1), null, null);

    [GlobalSetup]
    public void Setup()
    {
        _services = (ServiceProvider)BenchApp.CreateServices();
        _writer = _services.GetRequiredService<InertiaPageWriter>();
        _value = new string('x', PayloadBytes);
    }

    [Benchmark] public byte[] CreateEntry() => _writer.CreateCacheEntry(typeof(string), _value, null, _caching, "value", out _);

    [GlobalCleanup] public void Cleanup() => _services.Dispose();
}

[MemoryDiagnoser]
public class DictionaryPropsBenchmarks
{
    [Params(100, 1000)] public int Count { get; set; }
    private ServiceProvider _services = null!;
    private DefaultHttpContext _context = null!;
    private InertiaProps _integers = null!;
    private InertiaProps _strings = null!;

    [GlobalSetup]
    public void Setup()
    {
        _services = (ServiceProvider)BenchApp.CreateServices();
        _context = BenchApp.CreateContext(_services, "/users", (InertiaHeaders.Inertia, "true"));
        var integers = new Dictionary<int, InertiaProp>(Count);
        var strings = new Dictionary<string, InertiaProp>(Count);
        for (var i = 0; i < Count; i++)
        {
            var prop = Prop(() => 1);
            integers[i] = prop;
            strings[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = prop;
        }

        _integers = new InertiaProps { ["rows"] = integers };
        _strings = new InertiaProps { ["rows"] = strings };
    }

    [Benchmark] public Task IntegerKeys() => Render(BenchApp.Component, _integers).ExecuteAsync(_context);
    [Benchmark(Baseline = true)] public Task StringKeys() => Render(BenchApp.Component, _strings).ExecuteAsync(_context);

    [GlobalCleanup] public void Cleanup() => _services.Dispose();
}

[MemoryDiagnoser]
public class PayloadBenchmarks
{
    [Params(20, 200, 2000)] public int PropCount { get; set; }
    [Params(1024, 102400, 1048576)] public int PayloadBytes { get; set; }
    [Params(2, 32)] public int SharedCount { get; set; }
    private DefaultHttpContext _context = null!;
    private InertiaProps _props = null!;
    private InertiaProps _rows = null!;
    private ServiceProvider _services = null!;

    [GlobalSetup]
    public void Setup()
    {
        _services = (ServiceProvider)BenchApp.CreateServices(o =>
        {
            for (var i = 2; i < SharedCount; i++) o.Share($"shared{i}", i);
        });
        _context = BenchApp.CreateContext(_services, "/users", (InertiaHeaders.Inertia, "true"));
        var text = new string('x', Math.Max(1, PayloadBytes / PropCount));
        _props = new InertiaProps();
        for (var i = 0; i < PropCount; i++) _props[$"prop{i}"] = text;
        _rows = new InertiaProps { ["rows"] = Enumerable.Range(0, PropCount).Select(i => new BenchRow(text, Prop(() => i))).ToArray() };
    }

    [GlobalCleanup] public void Cleanup() => _services.Dispose();

    [Benchmark] public Task Json() => Render(BenchApp.Component, _props).ExecuteAsync(_context);
    [Benchmark] public Task TypedRows() => Render(BenchApp.Component, _rows).ExecuteAsync(_context);
    [Benchmark]
    public Task Partial()
    {
        _context.Request.Headers[InertiaHeaders.PartialComponent] = BenchApp.Component;
        _context.Request.Headers[InertiaHeaders.PartialData] = "prop0";
        return Render(BenchApp.Component, _props).ExecuteAsync(_context);
    }
}

[MemoryDiagnoser]
[GcForce(false)]
public class SsrBufferingBenchmarks
{
    [Params(1024, 1048576)] public int PayloadBytes { get; set; }
    private byte[] _body = null!;
    private IHttpClientFactory _clients = null!;
    private IInertiaSsrRenderer _renderer = null!;
    private InertiaRootViewContext _context = null!;
    private InertiaRootViewContext _cancelled = null!;
    private IInertiaSsrRenderer _timeoutRenderer = null!;
    private IInertiaSsrRenderer _oversizedRenderer = null!;
    private readonly List<ServiceProvider> _services = [];

    [GlobalSetup]
    public void Setup()
    {
        _body = Encoding.UTF8.GetBytes("{\"head\":[],\"body\":\"" + new string('x', PayloadBytes) + "\"}");
        var services = CreateServices("http://127.0.0.1", TimeSpan.FromSeconds(5));
        _clients = services.GetRequiredService<IHttpClientFactory>();
        _renderer = services.GetRequiredService<IInertiaSsrRenderer>();
        _context = new InertiaRootViewContext(BenchApp.CreateContext(services), "Page", "app", new Dictionary<string, object?>(), "{}"u8.ToArray(), "");
        _timeoutRenderer = CreateServices("http://127.0.0.1/timeout", TimeSpan.FromMilliseconds(1)).GetRequiredService<IInertiaSsrRenderer>();
        _oversizedRenderer = CreateServices("http://127.0.0.1/oversized", TimeSpan.FromSeconds(5)).GetRequiredService<IInertiaSsrRenderer>();
        var cancelled = BenchApp.CreateContext(services);
        cancelled.RequestAborted = new CancellationToken(canceled: true);
        _cancelled = new(cancelled, "Page", "app", new Dictionary<string, object?>(), "{}"u8.ToArray(), "");
    }

    private ServiceProvider CreateServices(string url, TimeSpan timeout)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new BenchHostEnvironment(BenchApp.CreateContentRoot()));
        services.AddLogging();
        services.AddHttpClient(SsrGateway.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Handler(_body));
        services.AddInertia(o =>
        {
            o.Ssr.Mode = SsrMode.Preferred;
            o.Ssr.Urls.Add(url);
            o.Ssr.Timeout = timeout;
            o.Ssr.MaxResponseBytes = _body.Length;
            o.Ssr.CircuitBreaker.Enabled = false; // measure the failure paths themselves, not the open-circuit skip
        });
        var provider = services.BuildServiceProvider();
        _services.Add(provider);
        return provider;
    }

    [Benchmark(Baseline = true)]
    public async Task<int?> BufferedReference()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_context.HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:13714/render") { Content = new ReadOnlyMemoryContent(_context.PageJson) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await _clients.CreateClient(SsrGateway.HttpClientName).SendAsync(request, timeout.Token);
        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
        using var render = SsrGateway.ParseRender(bytes);
        return render?.Body.Length;
    }

    // Body lengths: the 1.1 gateway returned a string (1 char per ASCII byte); the gateway now returns UTF-8 bytes.
    [Benchmark] public ValueTask<int?> Streaming() => BodyLength(_renderer);
    [Benchmark] public ValueTask<int?> Oversized() => BodyLength(_oversizedRenderer);
    [Benchmark] public ValueTask<int?> Timeout() => BodyLength(_timeoutRenderer);

    private async ValueTask<int?> BodyLength(IInertiaSsrRenderer renderer)
    {
        using var render = await renderer.RenderAsync(_context);
        return render?.Body.Length;
    }
    [Benchmark]
    public async Task Cancellation()
    {
        try { await _renderer.RenderAsync(_cancelled); }
        catch (OperationCanceledException) { }
    }

    [GlobalCleanup] public void Cleanup() { foreach (var provider in _services) provider.Dispose(); }

    private sealed class Handler(byte[] body) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsolutePath.StartsWith("/timeout", StringComparison.Ordinal)) await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            var content = new StreamContent(new MemoryStream(body, writable: false));
            if (request.RequestUri.AbsolutePath.StartsWith("/oversized", StringComparison.Ordinal)) content.Headers.ContentLength = body.Length + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}

public sealed record BenchRow(string Name, InertiaProp Value);
