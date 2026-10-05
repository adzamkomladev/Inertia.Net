using FastEndpoints;

namespace Inertia.Net.FastEndpoints;

/// <summary>Configures FastEndpoints for Inertia forms.</summary>
public static class InertiaFastEndpointsConfig
{
    private static readonly InertiaPreProcessor Processor = new();

    /// <summary>
    /// For every endpoint, answers request validation failures of Inertia requests (other than GET) with a redirect back carrying
    /// the errors (first message per field, keyed by the JSON name, in the bag named by <c>X-Inertia-Error-Bag</c>) instead of
    /// FastEndpoints' 400 JSON. Requests without <c>X-Inertia</c> still get the normal 400.
    /// <para>
    /// Also answers Precognition requests (<c>Precognition: true</c>, live validation): after validation, and without running the
    /// handler, with <c>204</c> when valid or <c>422</c> with <c>{ message, errors }</c> limited to <c>Precognition-Validate-Only</c>.
    /// </para>
    /// Use it as <c>app.UseFastEndpoints(c =&gt; c.UseInertia())</c>. It sets <c>c.Endpoints.Configurator</c>, so pass your own
    /// per-endpoint configuration as <paramref name="configure"/> rather than setting the configurator separately.
    /// </summary>
    /// <param name="config">The FastEndpoints configuration.</param>
    /// <param name="configure">Your own per-endpoint configuration, run after the Inertia setup.</param>
    public static Config UseInertia(this Config config, Action<EndpointDefinition>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Endpoints.Configurator = ep =>
        {
            // After the endpoint's own pre-processors, so one that already answered (e.g. 403) wins.
            ep.PreProcessors(Order.After, Processor);
            configure?.Invoke(ep);
        };
        return config;
    }
}

internal sealed class InertiaPreProcessor : IGlobalPreProcessor
{
    public Task PreProcessAsync(IPreProcessorContext context, CancellationToken ct)
    {
        var httpContext = context.HttpContext;
        if (httpContext.ResponseStarted())
        {
            return Task.CompletedTask;
        }

        var precognitive = InertiaPrecognition.IsPrecognitive(httpContext.Request);
        if (!precognitive && !(context.HasValidationFailures && InertiaValidationFilter.IsInertiaMutation(httpContext)))
        {
            return Task.CompletedTask;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var failures in context.ValidationFailures.GroupBy(f => f.PropertyName))
        {
            errors[failures.Key] = [.. failures.Select(f => f.ErrorMessage)];
        }

        var naming = InertiaValidationFilter.NamingPolicy(httpContext.RequestServices);
        return httpContext.Response.SendResultAsync(precognitive
            ? InertiaPrecognition.Result(httpContext, errors, naming)
            : InertiaValidationFilter.Back(httpContext, InertiaValidationFilter.ToClientKeys(errors, naming)));
    }
}
