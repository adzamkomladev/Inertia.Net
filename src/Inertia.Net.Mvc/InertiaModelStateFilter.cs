using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Inertia.Net.Mvc;

/// <summary>
/// For Inertia requests other than GET with an invalid <see cref="ControllerBase.ModelState"/>, short-circuits the action with a redirect
/// back carrying the errors (bag from <c>X-Inertia-Error-Bag</c>, else <c>default</c>). Registered globally by <c>AddInertiaMvc()</c>.
/// Keys are the model paths with each segment run through the MVC JSON naming policy (<c>Address.Street</c> becomes <c>address.street</c>).
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
            context.Result = new ResultAdapter(BackWithErrors(context.HttpContext, context.ModelState));
            return Task.CompletedTask;
        }

        return next();
    }

    internal static InertiaBackResult BackWithErrors(HttpContext httpContext, ModelStateDictionary modelState)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, entry) in modelState)
        {
            if (entry.Errors.Count > 0)
            {
                // Binding failures carry only an exception (whose message may expose internals), so they get a generic message.
                errors[key] = [.. entry.Errors.Select(e => e.ErrorMessage.Length > 0 ? e.ErrorMessage : "The value is invalid.")];
            }
        }

        var naming = httpContext.RequestServices.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions.PropertyNamingPolicy;
        return InertiaValidationFilter.Back(httpContext, InertiaValidationFilter.ToClientKeys(errors, naming));
    }
}

/// <summary>Lets an <see cref="IResult"/> be used where MVC wants an <see cref="IActionResult"/>.</summary>
internal sealed class ResultAdapter(IResult result) : IActionResult
{
    public Task ExecuteResultAsync(ActionContext context) => result.ExecuteAsync(context.HttpContext);
}
