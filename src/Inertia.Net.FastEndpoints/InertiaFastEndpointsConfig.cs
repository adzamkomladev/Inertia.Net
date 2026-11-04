using System.Runtime.CompilerServices;
using FastEndpoints;

namespace Inertia.Net.FastEndpoints;

/// <summary>Configures FastEndpoints for Inertia forms.</summary>
public static class InertiaFastEndpointsConfig
{
    /// <summary>
    /// For every endpoint, answers request validation failures of Inertia requests (other than GET) with a redirect back carrying
    /// the errors (first message per field, keyed by the JSON name, in the bag named by <c>X-Inertia-Error-Bag</c>) instead of
    /// FastEndpoints' 400 JSON. Requests without <c>X-Inertia</c> still get the normal 400.
    /// <para>
    /// Also answers Precognition requests (<c>Precognition: true</c>, live validation): after validation, and without running the
    /// handler, with <c>204</c> when valid or <c>422</c> with <c>{ message, errors }</c> limited to <c>Precognition-Validate-Only</c>.
    /// </para>
    /// Use it as <c>app.UseFastEndpoints(c =&gt; c.ConfigureInertia())</c>. An existing <c>c.Endpoints.Configurator</c> runs first,
    /// followed by the Inertia setup and then the optional per-endpoint <paramref name="configure"/> callback.
    /// </summary>
    /// <param name="config">The FastEndpoints configuration.</param>
    /// <param name="configure">Your own per-endpoint configuration, run after the Inertia setup.</param>
    public static Config ConfigureInertia(this Config config, Action<EndpointDefinition>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var processor = new InertiaPreProcessor(config);
        var previous = GetConfigurator(config.Endpoints);
        config.Endpoints.Configurator = ep =>
        {
            previous?.Invoke(ep);
            // After the endpoint's own pre-processors, so one that already answered (e.g. 403) wins.
            ep.PreProcessors(Order.After, processor);
            configure?.Invoke(ep);
        };
        return config;
    }

    // FastEndpoints 8.3 exposes this property's getter only internally; a static accessor preserves callbacks without reflection.
    // The exact member contract is covered by the configuration regression and the Native AOT smoke app.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_" + nameof(EndpointOptions.Configurator))]
    private static extern Action<EndpointDefinition>? GetConfigurator(EndpointOptions options);
}

internal sealed class InertiaPreProcessor(Config config) : IGlobalPreProcessor
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

        var naming = config.Serializer.Options.PropertyNamingPolicy;
        errors = InertiaValidationFilter.ToClientKeys(errors, naming,
            context.Request is { } request
                ? InertiaValidationFilter.GetSchema(config.Serializer.Options, request.GetType()) : null);
        return httpContext.Response.SendResultAsync(precognitive
            ? InertiaPrecognition.Result(httpContext, errors, null)
            : InertiaValidationFilter.Back(httpContext, errors));
    }
}
