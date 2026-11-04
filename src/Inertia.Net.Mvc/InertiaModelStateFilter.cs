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
/// </summary>
public sealed class InertiaModelStateFilter : IAsyncActionFilter
{
    /// <inheritdoc />
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (!context.ModelState.IsValid && InertiaValidationFilter.IsInertiaMutation(context.HttpContext))
        {
            context.Result = new ResultAdapter(BackWithErrors(context));
            return Task.CompletedTask;
        }

        return next();
    }

    internal static InertiaBackResult BackWithErrors(ActionContext context)
    {
        var httpContext = context.HttpContext;
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
        return InertiaValidationFilter.Back(httpContext, InertiaValidationFilter.ToClientKeys(errors, naming, schema));
    }
}

/// <summary>Lets an <see cref="IResult"/> be used where MVC wants an <see cref="IActionResult"/>.</summary>
internal sealed class ResultAdapter(IResult result) : IActionResult
{
    public Task ExecuteResultAsync(ActionContext context) => result.ExecuteAsync(context.HttpContext);
}
