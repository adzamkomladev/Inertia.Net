// The resolver half of Microsoft.Extensions.Validation is marked experimental (ASP0029) in .NET 10; ASP.NET Core's own validation
// filter suppresses it the same way. If the API changes, only this file is affected.
#pragma warning disable ASP0029

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
        builder.AddEndpointFilterFactory((context, next) => InertiaValidationFilter.Create(context, next, precognition: false));
        return builder;
    }

    /// <summary>
    /// <see cref="WithInertiaValidation{TBuilder}"/> plus Precognition (<c>Precognition: true</c> live validation requests): the
    /// request is validated and answered without running the handler or any endpoint filter added after this one, with
    /// <c>204</c> (<c>Precognition-Success: true</c>) when valid, otherwise <c>422</c> with <c>{ "message", "errors": { field: [messages] } }</c>,
    /// limited to the fields named by <c>Precognition-Validate-Only</c>. Use it instead of <c>WithInertiaValidation()</c>, not in addition.
    /// </summary>
    public static TBuilder WithInertiaPrecognition<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.DisableValidation();
        builder.AddEndpointFilterFactory((context, next) => InertiaValidationFilter.Create(context, next, precognition: true));
        return builder;
    }
}

/// <summary>The endpoint filter behind <see cref="InertiaValidationEndpointExtensions.WithInertiaValidation"/>.</summary>
internal static class InertiaValidationFilter
{
    public static JsonNamingPolicy? NamingPolicy(IServiceProvider services) =>
        services.GetService<InertiaPageWriter>()?.SerializerOptions.PropertyNamingPolicy;

    public static EndpointFilterDelegate Create(EndpointFilterFactoryContext context, EndpointFilterDelegate next, bool precognition)
    {
        var validatable = FindValidatableParameters(context);
        var naming = NamingPolicy(context.ApplicationServices);
        return async invocation =>
        {
            var httpContext = invocation.HttpContext;
            if (precognition)
            {
                if (InertiaPrecognition.IsPrecognitive(httpContext.Request))
                {
                    return InertiaPrecognition.Result(httpContext, validatable is null ? null : await ValidateAsync(invocation, validatable.Value), naming);
                }

                InertiaPrecognition.AppendVary(httpContext.Response.Headers);
            }

            if (validatable is not null && await ValidateAsync(invocation, validatable.Value) is { } errors)
            {
                return IsInertiaMutation(httpContext) ? Back(httpContext, ToClientKeys(errors, naming)) : TypedResults.ValidationProblem(errors);
            }

            var result = await next(invocation);
            return IsInertiaMutation(httpContext) && GetValidationErrors(result) is { } returned ? Back(httpContext, returned) : result;
        };
    }

    internal static bool IsInertiaMutation(HttpContext httpContext) =>
        !HttpMethods.IsGet(httpContext.Request.Method) && !string.IsNullOrEmpty(httpContext.Request.Headers[InertiaHeaders.Inertia]);

    // Microsoft.Extensions.Validation reports CLR member paths ("Address.Street"); the client knows the JSON names, so each
    // segment goes through the JSON naming policy ("address.street"). [JsonPropertyName] overrides are not applied.
    internal static Dictionary<string, string[]> ToClientKeys(Dictionary<string, string[]> errors, JsonNamingPolicy? naming, JsonTypeInfo? schema = null)
    {
        if (naming is null && !errors.Keys.Any(k => k.Contains('[', StringComparison.Ordinal)))
        {
            return errors;
        }

        var result = new Dictionary<string, string[]>(errors.Count, StringComparer.Ordinal);
        foreach (var (key, messages) in errors)
        {
            var path = ToClientPath(key, naming, schema);
            result[path] = result.TryGetValue(path, out var existing) ? [.. existing, .. messages] : messages;
        }

        return result;
    }

    internal static string ToClientPath(string path, JsonNamingPolicy? naming, JsonTypeInfo? schema = null)
    {
        var result = new StringBuilder(path.Length);
        for (var i = 0; i < path.Length;)
        {
            if (path[i] == '.') { result.Append('.'); i++; continue; }
            if (path[i] == '[')
            {
                var close = path.IndexOf(']', i + 1);
                if (close < 0) { result.Append(path.AsSpan(i)); break; }
                var index = path.AsSpan(i + 1, close - i - 1);
                if (!index.IsEmpty && index.IndexOfAnyExceptInRange('0', '9') < 0)
                    result.Append('.').Append(index);
                else result.Append(path.AsSpan(i, close - i + 1));
                schema = ElementSchema(schema);
                i = close + 1;
                continue;
            }
            var start = i;
            while (i < path.Length && path[i] is not ('.' or '[')) i++;
            var member = path[start..i];
            if (schema?.Kind == JsonTypeInfoKind.Dictionary || member.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0)
            {
                result.Append(member);
                schema = ElementSchema(schema);
                continue;
            }
            var converted = naming?.ConvertName(member) ?? member;
            // FastEndpoints may already have named part of a validation path through its global resolver.
            var property = schema?.Kind == JsonTypeInfoKind.Object
                ? schema.Properties.FirstOrDefault(p => p.Name == member) ?? schema.Properties.FirstOrDefault(p => p.Name == converted)
                : null;
            result.Append(property?.Name ?? converted);
            schema = property is null ? null : GetSchema(schema!.Options, property.PropertyType);
        }
        return result.ToString();
    }

    private static JsonTypeInfo? ElementSchema(JsonTypeInfo? schema) => schema?.ElementType is { } type
        ? GetSchema(schema.Options, type) : null;
    internal static JsonTypeInfo? GetSchema(JsonSerializerOptions options, Type type)
    {
        try { return options.GetTypeInfo(type); }
        catch (NotSupportedException) { return null; }
    }

    internal static InertiaBackResult Back(HttpContext httpContext, IDictionary<string, string[]> errors)
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
