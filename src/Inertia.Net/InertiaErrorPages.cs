using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

internal static class InertiaErrorPages
{
    private static readonly Action<ILogger, string, int, Exception?> Rendered = LoggerMessage.Define<string, int>(
        LogLevel.Debug, new EventId(1, "InertiaErrorPage"), "Rendered Inertia error component {Component} for status {StatusCode}.");

    public static Task HandleStatusCodeAsync(StatusCodeContext context) =>
        TryRenderAsync(context.HttpContext, null).AsTask();

    internal static async ValueTask<bool> TryRenderAsync(HttpContext context, Exception? exception)
    {
        var services = context.RequestServices;
        // Each mount renders only the error components it registered: another mount's would not exist in its bundle.
        var mount = InertiaMountServices.Mount(context);
        if (services.GetService<InertiaErrorPagesRegistration>()?.Mounts.Contains(mount ?? "") != true) return false;
        var options = mount is null
            ? services.GetRequiredService<IOptions<InertiaErrorPagesOptions>>().Value
            : services.GetRequiredService<IOptionsMonitor<InertiaErrorPagesOptions>>().Get(mount);
        var status = context.Response.StatusCode;
        if (context.Response.HasStarted || status is 409 or 419 || !options.StatusCodes.Contains(status)
            || (!options.EnabledInDevelopment && services.GetRequiredService<IHostEnvironment>().IsDevelopment())
            || !IsPageRequest(context.Request)) return false;

        var component = options.Components.GetValueOrDefault(status, options.Component);
        var props = new InertiaProps { ["status"] = status };
        if (options.Props?.Invoke(context, status, exception) is { } extra)
        {
            foreach (var (key, value) in extra) props[key] = value;
        }

        await Inertia.Render(component, props).WithStatusCode(status).ExecuteAsync(context);
        Rendered(services.GetRequiredService<ILoggerFactory>().CreateLogger("Inertia.Net.InertiaErrorPages"), component, status, null);
        return true;
    }

    private static bool IsPageRequest(HttpRequest request)
    {
        if (!string.IsNullOrEmpty(request.Headers[InertiaHeaders.Inertia])) return true;
        try
        {
            return request.GetTypedHeaders().Accept?.Any(value =>
                string.Equals(value.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase) && value.Quality is not 0) == true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal sealed class InertiaExceptionHandler : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken) =>
        InertiaErrorPages.TryRenderAsync(httpContext, exception);
}
