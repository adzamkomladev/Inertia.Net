using System.Net;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Benchmarks;

/// <summary>Identical before/after fixtures for cache-store failures and diagnostic body buffering.</summary>
[MemoryDiagnoser]
public class FixPerformanceBenchmarks
{
    private const int BurstSize = 8, BodyBytes = 1024 * 1024;
    private readonly RenderBenchmarks _render = new();
    private readonly byte[] _body = new byte[BodyBytes];
    private readonly CountingSink _sink = new();
    private ServiceProvider _props = null!, _ssr = null!, _diagnostics = null!;
    private DefaultHttpContext[] _propRequests = null!, _ssrRequests = null!;
    private InertiaProps _page = null!;
    private DevToolsRecorder _recorder = null!;
    private int _propLoads, _ssrCalls;

    /// <summary>Successful loader executions in the most recent eight-request burst.</summary>
    public int LastPropLoads { get; private set; }

    /// <summary>Renderer calls in the most recent eight-request burst.</summary>
    public int LastSsrCalls { get; private set; }

    /// <summary>Bytes spooled to disk by the most recent diagnostic capture.</summary>
    public long LastBufferedBytes { get; private set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _render.Setup();
        _props = (ServiceProvider)BenchApp.CreateServices(configureServices: s => s.AddSingleton<IInertiaCacheStore>(new FailingWriteStore()));
        _propRequests = Requests(_props, json: true);
        _page = new InertiaProps
        {
            ["value"] = Prop(async ct =>
            {
                Interlocked.Increment(ref _propLoads);
                await Task.Delay(10, ct);
                return 42;
            }).Cache("failed-write", TimeSpan.FromMinutes(1)),
        };
        var response = Encoding.UTF8.GetBytes("{\"head\":[],\"body\":\"<div>" + new string('x', 8192) + "</div>\"}");
        _ssr = (ServiceProvider)BenchApp.CreateServices(o =>
        {
            o.Ssr.Mode = SsrMode.Required;
            o.Ssr.Cache.Duration = TimeSpan.FromMinutes(1);
            o.Ssr.Cache.MaxBytes = 1;
        }, s => s.AddHttpClient(InertiaSsr.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new DelayedRenderer(this, response)));
        _ssrRequests = Requests(_ssr, json: false);
        _diagnostics = (ServiceProvider)BenchApp.CreateServices(configureServices: s => s.AddInertiaDevTools(o => o.Enabled = true));
        _recorder = _diagnostics.GetRequiredService<DevToolsRecorder>();

        await FailedPropBurst8();
        await RejectedSsrBurst8();
        await IgnoredBody1MiB();
        if (LastPropLoads is < 1 or > BurstSize || LastSsrCalls is < 1 or > BurstSize || LastBufferedBytes is not (0 or BodyBytes))
            throw new InvalidOperationException("Incorrect performance fixture.");
        Console.WriteLine($"Fix fixtures: loaders={LastPropLoads}/8; renders={LastSsrCalls}/8; buffered={LastBufferedBytes} bytes; simulated I/O=10ms.");
    }

    [Benchmark] public Task Json20() => _render.Json20Props();
    [Benchmark] public Task Partial2Of20() => _render.PartialReload();
    [Benchmark] public Task Html20() => _render.Html20Props();
    [Benchmark] public void ReferenceSerialize() => _render.PlainSerialize();

    [Benchmark]
    public async Task FailedPropBurst8()
    {
        var before = Volatile.Read(ref _propLoads);
        await Task.WhenAll(_propRequests.Select(context => Execute(context, _page)));
        LastPropLoads = Volatile.Read(ref _propLoads) - before;
    }

    [Benchmark]
    public async Task RejectedSsrBurst8()
    {
        var before = Volatile.Read(ref _ssrCalls);
        await Task.WhenAll(_ssrRequests.Select(context => Execute(context, new InertiaProps { ["value"] = 42 })));
        LastSsrCalls = Volatile.Read(ref _ssrCalls) - before;
    }

    [Benchmark]
    public async Task IgnoredBody1MiB()
    {
        var context = BenchApp.CreateContext(_diagnostics, "/upload");
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = BodyBytes;
        using var source = new NonSeekableStream(_body);
        context.Request.Body = source;
        var capture = _recorder.Begin(context)!;
        _sink.Written = 0;
        try
        {
            await context.Request.Body.CopyToAsync(_sink);
            if (_sink.Written != BodyBytes) throw new InvalidOperationException("The handler did not receive the full body.");
            LastBufferedBytes = context.Request.Body is FileBufferingReadStream { InMemory: false } buffered ? buffered.Length : 0;
            await _recorder.CompleteAsync(context, capture, failed: false);
        }
        finally
        {
            await context.Request.Body.DisposeAsync();
        }
    }

    private static Task Execute(HttpContext context, InertiaProps props)
    {
        context.Response.Headers.Clear();
        return Render(BenchApp.Component, props).ExecuteAsync(context);
    }

    private static DefaultHttpContext[] Requests(IServiceProvider services, bool json) => Enumerable.Range(0, BurstSize)
        .Select(_ => json ? BenchApp.CreateContext(services, "/users", (InertiaHeaders.Inertia, "true")) : BenchApp.CreateContext(services)).ToArray();

    [GlobalCleanup]
    public void Cleanup()
    {
        _props.Dispose();
        _ssr.Dispose();
        _diagnostics.Dispose();
        _sink.Dispose();
    }

    private sealed class FailingWriteStore : IInertiaCacheStore
    {
        public ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken cancellationToken) => new((byte[]?)null);
        public ValueTask SetAsync(InertiaCacheKey key, byte[] value, InertiaCacheEntryOptions options, CancellationToken cancellationToken) => throw new IOException("Simulated cache write failure.");
        public ValueTask RemoveAsync(InertiaCacheKey key, CancellationToken cancellationToken) => default;
        public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken) => default;
    }

    private sealed class DelayedRenderer(FixPerformanceBenchmarks owner, byte[] response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref owner._ssrCalls);
            await Task.Delay(10, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(response) };
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class CountingSink : Stream
    {
        public long Written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Written += count;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) { Written += buffer.Length; return default; }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { Written += count; return Task.CompletedTask; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
