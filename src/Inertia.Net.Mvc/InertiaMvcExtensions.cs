using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inertia.Net.Mvc;

/// <summary>Options for <see cref="InertiaMvcExtensions.AddInertiaMvc"/>.</summary>
public sealed class InertiaMvcOptions
{
    internal string? RazorView { get; private set; }

    internal bool TempDataStore { get; private set; }

    /// <summary>
    /// The component <see cref="InertiaControllerExtensions.InertiaView"/> renders for the current action. Default
    /// <c>{Controller}/{Action}</c>, prefixed with <c>{area}/</c> in an area: <c>UsersController.Index</c> renders <c>Users/Index</c>.
    /// </summary>
    public Func<ControllerContext, string>? ComponentResolver { get; set; }

    /// <summary>
    /// Renders the first-visit HTML with a Razor view instead of the <c>app.html</c> template. The view gets an
    /// <see cref="InertiaRootViewContext"/> as its model and the <c>&lt;inertia /&gt;</c>, <c>&lt;inertia-head /&gt;</c>, <c>&lt;vite /&gt;</c> and
    /// <c>&lt;vite-react-refresh /&gt;</c> tag helpers (<c>@addTagHelper *, Inertia.Net.Mvc</c>). Sets <see cref="InertiaOptions.RootView"/>
    /// to <paramref name="view"/>, so <c>WithRootView</c> then takes Razor view names too.
    /// </summary>
    /// <param name="view">A view name (<c>App</c> finds <c>Views/Shared/App.cshtml</c>) or an app-relative path (<c>~/Views/App.cshtml</c>).</param>
    public InertiaMvcOptions UseRazorRootView(string view = "App")
    {
        ArgumentException.ThrowIfNullOrEmpty(view);
        RazorView = view;
        return this;
    }

    /// <summary>
    /// Keeps the redirect state (flash, errors, history flags) in MVC TempData instead of the encrypted cookie, so it follows the app's
    /// TempData provider: the default cookie provider, or <c>AddSessionStateTempDataProvider()</c> with <c>AddSession()</c> and
    /// <c>app.UseSession()</c> before <c>app.UseInertia()</c>. Replaces the <see cref="IInertiaStateStore"/> registration.
    /// </summary>
    public InertiaMvcOptions UseTempDataStateStore()
    {
        TempDataStore = true;
        return this;
    }
}

/// <summary>Registers the Inertia.Net MVC integration.</summary>
public static class InertiaMvcExtensions
{
    /// <summary>
    /// Adds <see cref="InertiaModelStateFilter"/> as a global filter and, with <see cref="InertiaMvcOptions.UseRazorRootView"/>, the Razor root view.
    /// Call it after <c>AddControllers()</c> / <c>AddControllersWithViews()</c> (the Razor root view needs the latter) and <c>AddInertia()</c>.
    /// <c>[ApiController]</c> controllers are covered too: for Inertia requests the automatic 400 is replaced by the same redirect back.
    /// </summary>
    public static IMvcBuilder AddInertiaMvc(this IMvcBuilder builder, Action<InertiaMvcOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new InertiaMvcOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        builder.AddMvcOptions(o => o.Filters.Add(new InertiaModelStateFilter()));

        // ModelStateInvalidFilter (the [ApiController] 400) runs before global filters, so it is wrapped rather than followed.
        builder.Services.PostConfigure<ApiBehaviorOptions>(o =>
        {
            var inner = o.InvalidModelStateResponseFactory;
            o.InvalidModelStateResponseFactory = context => InertiaValidationFilter.IsInertiaMutation(context.HttpContext)
                ? new ResultAdapter(InertiaModelStateFilter.BackWithErrors(context.HttpContext, context.ModelState))
                : inner(context);
        });

        if (options.RazorView is { } view)
        {
            builder.Services.Configure<InertiaOptions>(o => o.RootView = view);
            builder.Services.Replace(ServiceDescriptor.Singleton<IInertiaRootView, RazorInertiaRootView>());
        }

        if (options.TempDataStore)
        {
            builder.Services.Replace(ServiceDescriptor.Singleton<IInertiaStateStore, TempDataInertiaStateStore>());
        }

        return builder;
    }
}

/// <summary>Inertia helpers for controllers.</summary>
public static class InertiaControllerExtensions
{
    /// <summary>
    /// Renders the action's own page component, named by <see cref="InertiaMvcOptions.ComponentResolver"/> (default
    /// <c>{Controller}/{Action}</c>): <c>return this.InertiaView(props);</c> in <c>UsersController.Index</c> renders <c>Users/Index</c>.
    /// </summary>
    /// <param name="controller">The controller.</param>
    /// <param name="props">The page props, as for <c>Render</c>.</param>
    public static InertiaResult InertiaView(this ControllerBase controller, object? props = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var context = controller.ControllerContext;
        var resolver = context.HttpContext?.RequestServices?.GetService<InertiaMvcOptions>()?.ComponentResolver ?? DefaultComponent;
        return Inertia.Render(resolver(context), props);
    }

    internal static string DefaultComponent(ControllerContext context)
    {
        var descriptor = context.ActionDescriptor
            ?? throw new InvalidOperationException("InertiaView() needs the action's ControllerContext; call it from a controller action.");
        var component = $"{descriptor.ControllerName}/{descriptor.ActionName}";
        return descriptor.RouteValues.TryGetValue("area", out var area) && !string.IsNullOrEmpty(area) ? $"{area}/{component}" : component;
    }
}
