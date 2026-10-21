using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Inertia.Net;

/// <summary>Endpoint shortcuts for Inertia pages.</summary>
public static class InertiaEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps GET and HEAD requests for <paramref name="pattern"/> to <c>Render(component, props)</c>, for pages without a handler of their
    /// own, such as an about or terms page. The same <paramref name="props"/> object serves every request. Native AOT safe.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern, e.g. <c>/about</c>.</param>
    /// <param name="component">The client-side page component.</param>
    /// <param name="props">Props for the page (an <see cref="InertiaProps"/>, dictionary or typed object), or null.</param>
    public static IEndpointConventionBuilder MapInertia(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern, string component, object? props = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(component);
        return endpoints.MapMethods(pattern, ["GET", "HEAD"], httpContext => Inertia.Render(component, props).ExecuteAsync(httpContext))
            .WithDisplayName($"Inertia page {component}");
    }
}
