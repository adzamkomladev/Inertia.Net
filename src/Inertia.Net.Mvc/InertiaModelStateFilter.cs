using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Inertia.Net.Mvc;

/// <summary>
/// For Inertia requests other than GET with an invalid <see cref="ControllerBase.ModelState"/>, short-circuits the action with a redirect
/// back carrying the errors (bag from <c>X-Inertia-Error-Bag</c>, else <c>default</c>). Registered globally by <c>AddInertiaMvc()</c>.
/// Keys are the model paths mapped through the page JSON contract (<c>Address.Street</c> becomes <c>address.street</c>).
/// A request that is not an Inertia visit but carries <c>X-Requested-With: XMLHttpRequest</c> (the client's <c>useHttp</c>) gets a
/// <c>422</c> with <c>{ "message", "errors" }</c> instead.
/// </summary>
public sealed class InertiaModelStateFilter : IAsyncActionFilter
{
    /// <inheritdoc />
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (!context.ModelState.IsValid && Respond(context) is { } result)
        {
            context.Result = result;
            return Task.CompletedTask;
        }

        return next();
    }

    /// <summary>The redirect back of an Inertia form, the 422 of a <c>useHttp</c> request, or null for any other request.</summary>
    internal static IActionResult? Respond(ActionContext context)
    {
        var httpContext = context.HttpContext;
        if (!InertiaValidationFilter.IsInertiaMutation(httpContext) && !InertiaValidationFilter.IsHttpRequest(httpContext)) return null;
        var modelState = context.ModelState;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, entry) in modelState)
        {
            if (entry.Errors.Count > 0)
            {
                // Binding failures carry only an exception (whose message may expose internals), so they get a generic message.
                errors[key] = [.. entry.Errors.Select(e => e.ErrorMessage.Length > 0 ? e.ErrorMessage : "The value is invalid.")];
            }
        }

        var naming = InertiaValidationFilter.NamingPolicy(httpContext.RequestServices);
        var schema = context.ActionDescriptor is ControllerActionDescriptor action
            ? InertiaValidationFilter.BodySchema(action.MethodInfo, httpContext.RequestServices) : null;
        return InertiaValidationFilter.Respond(httpContext, errors, naming, schema) is { } result ? new ResultAdapter(result) : null;
    }
}

/// <summary>Lets an <see cref="IResult"/> be used where MVC wants an <see cref="IActionResult"/>.</summary>
internal sealed class ResultAdapter(IResult result) : IActionResult
{
    public Task ExecuteResultAsync(ActionContext context) => result.ExecuteAsync(context.HttpContext);
}
