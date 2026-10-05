// The resolver half of Microsoft.Extensions.Validation is marked experimental (ASP0029) in .NET 10; ASP.NET Core's own validation
// filter suppresses it the same way. If the API changes, only this file is affected.
#pragma warning disable ASP0029

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Validation;

namespace Inertia.Net;

/// <summary>Minimal API validation glue for Inertia forms.</summary>
public static class InertiaValidationEndpointExtensions
{
    /// <summary>
    /// For Inertia requests other than GET, turns validation failures into a redirect back with the errors (in the bag named by
    /// <c>X-Inertia-Error-Bag</c>, else <c>default</c>) instead of a 400 problem details response. Covers:
    /// <list type="bullet">
    /// <item>.NET 10 <c>services.AddValidation()</c>: its built-in endpoint filter always runs outermost and writes the 400 itself, so
    /// this disables it on the endpoint and runs the same source-generated validation in its place (other requests still get the 400);</item>
    /// <item>a handler returning <c>TypedResults.ValidationProblem(...)</c>, <c>Results.ValidationProblem(...)</c> or an
    /// <see cref="HttpValidationProblemDetails"/>.</item>
    /// </list>
    /// Works on a single endpoint or a route group.
    /// </summary>
    public static TBuilder WithInertiaValidation<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.DisableValidation();
        builder.AddEndpointFilterFactory(InertiaValidationFilter.Create);
        return builder;
    }
}

/// <summary>The endpoint filter behind <see cref="InertiaValidationEndpointExtensions.WithInertiaValidation"/>.</summary>
internal static class InertiaValidationFilter
{
    public static EndpointFilterDelegate Create(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        var validatable = FindValidatableParameters(context);
        var naming = context.ApplicationServices.GetService<InertiaPageWriter>()?.SerializerOptions.PropertyNamingPolicy;
        return async invocation =>
        {
            var httpContext = invocation.HttpContext;
            if (validatable is not null && await ValidateAsync(invocation, validatable.Value) is { } errors)
            {
                return IsInertiaMutation(httpContext) ? Back(httpContext, ToClientKeys(errors, naming)) : TypedResults.ValidationProblem(errors);
            }

            var result = await next(invocation);
            return IsInertiaMutation(httpContext) && GetValidationErrors(result) is { } returned ? Back(httpContext, returned) : result;
        };
    }

    private static bool IsInertiaMutation(HttpContext httpContext) =>
        !HttpMethods.IsGet(httpContext.Request.Method) && !string.IsNullOrEmpty(httpContext.Request.Headers[InertiaHeaders.Inertia]);

    // Microsoft.Extensions.Validation reports CLR member paths ("Address.Street"); the client knows the JSON names, so each
    // segment goes through the JSON naming policy ("address.street"). [JsonPropertyName] overrides are not applied.
    private static Dictionary<string, string[]> ToClientKeys(Dictionary<string, string[]> errors, JsonNamingPolicy? naming)
    {
        if (naming is null)
        {
            return errors;
        }

        var result = new Dictionary<string, string[]>(errors.Count, StringComparer.Ordinal);
        foreach (var (key, messages) in errors)
        {
            result[string.Join('.', key.Split('.').Select(naming.ConvertName))] = messages;
        }

        return result;
    }

    private static InertiaBackResult Back(HttpContext httpContext, IDictionary<string, string[]> errors)
    {
        var bag = httpContext.Request.Headers[InertiaHeaders.ErrorBag].ToString();
        return Inertia.Back().WithErrors(errors, bag.Length == 0 ? "default" : bag);
    }

    private static IDictionary<string, string[]>? GetValidationErrors(object? result) => result switch
    {
        INestedHttpResult nested => GetValidationErrors(nested.Result),
        ValidationProblem problem => problem.ProblemDetails.Errors,
        ProblemHttpResult { ProblemDetails: HttpValidationProblemDetails details } => details.Errors,
        HttpValidationProblemDetails details => details.Errors,
        _ => null,
    };

    // Mirrors the built-in ValidationEndpointFilterFactory (Microsoft.AspNetCore.Routing, .NET 10).
    private static (ValidationOptions Options, (int Index, IValidatableInfo Info, string DisplayName)[] Parameters)? FindValidatableParameters(EndpointFilterFactoryContext context)
    {
        if (context.ApplicationServices.GetService<IOptions<ValidationOptions>>()?.Value is not { Resolvers.Count: > 0 } options)
        {
            return null;
        }

        var isService = context.ApplicationServices.GetService<IServiceProviderIsService>();
        var parameters = context.MethodInfo.GetParameters();
        var result = new List<(int, IValidatableInfo, string)>();
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.GetCustomAttributes(inherit: false).Any(a => a is IFromServiceMetadata)
                || isService?.IsService(parameter.ParameterType) == true)
            {
                continue;
            }

            if (options.TryGetValidatableParameterInfo(parameter, out var info))
            {
                result.Add((i, info, parameter.GetCustomAttribute<DisplayAttribute>()?.Name ?? parameter.Name!));
            }
        }

        return result.Count == 0 ? null : (options, result.ToArray());
    }

    private static async ValueTask<Dictionary<string, string[]>?> ValidateAsync(
        EndpointFilterInvocationContext invocation,
        (ValidationOptions Options, (int Index, IValidatableInfo Info, string DisplayName)[] Parameters) validatable)
    {
        ValidateContext? validateContext = null;
        foreach (var (index, info, displayName) in validatable.Parameters)
        {
            if (index >= invocation.Arguments.Count || invocation.Arguments[index] is not { } argument)
            {
                continue;
            }

            var validationContext = new ValidationContext(argument, displayName, invocation.HttpContext.RequestServices, items: null);
            validateContext ??= new ValidateContext { ValidationOptions = validatable.Options, ValidationContext = validationContext };
            validateContext.ValidationContext = validationContext;
            await info.ValidateAsync(argument, validateContext, invocation.HttpContext.RequestAborted);
        }

        return validateContext?.ValidationErrors is { Count: > 0 } errors ? errors : null;
    }
}
