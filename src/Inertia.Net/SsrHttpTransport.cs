using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

// Keep the shared named client's headers and delegating handlers. The default primary handler needs
// a separate connection pool only for mounts that use a different connection establishment timeout.
internal sealed class SsrHttpTransport : HttpMessageHandler
{
    internal static readonly HttpRequestOptionsKey<string> MountKey = new("Inertia.Net.Ssr.Mount");
    private readonly HttpMessageInvoker _default;
    private readonly Dictionary<string, HttpMessageInvoker> _mounts;
    private readonly HttpMessageInvoker[] _pools;

    private SsrHttpTransport(HttpMessageInvoker defaultPool, Dictionary<string, HttpMessageInvoker> mounts, HttpMessageInvoker[] pools)
    {
        _default = defaultPool;
        _mounts = mounts;
        _pools = pools;
    }

    internal static HttpMessageHandler Create(IServiceProvider services, Func<TimeSpan, HttpMessageHandler>? createPool = null)
    {
        createPool ??= static timeout => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = timeout,
        };
        var options = services.GetRequiredService<IOptionsMonitor<InertiaOptions>>();
        var timeout = options.CurrentValue.Ssr.ConnectTimeout;
        var names = services.GetRequiredService<InertiaMounts>().Names;
        var groups = names.Where(name => name.Length > 0).GroupBy(name => options.Get(name).Ssr.ConnectTimeout)
            .Where(group => group.Key != timeout).ToArray();
        var handler = createPool(timeout);
        if (groups.Length == 0) return handler;

        var defaultPool = new HttpMessageInvoker(handler);
        var mounts = new Dictionary<string, HttpMessageInvoker>(StringComparer.Ordinal);
        var pools = new List<HttpMessageInvoker> { defaultPool };
        foreach (var group in groups)
        {
            var pool = new HttpMessageInvoker(createPool(group.Key));
            pools.Add(pool);
            foreach (var name in group) mounts.Add(name, pool);
        }

        return new SsrHttpTransport(defaultPool, mounts, [.. pools]);
    }

    private HttpMessageInvoker Select(HttpRequestMessage request) =>
        request.Options.TryGetValue(MountKey, out var mount) && _mounts.TryGetValue(mount, out var pool) ? pool : _default;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Select(request).SendAsync(request, cancellationToken);

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Select(request).Send(request, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var pool in _pools) pool.Dispose();
        }
        base.Dispose(disposing);
    }
}
