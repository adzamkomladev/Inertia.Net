using FastEndpoints;
using FastEndpoints.Svelte;
using FastEndpointsSvelte;
using Inertia.Net.FastEndpoints;

var e2e = Environment.GetEnvironmentVariable("E2E") == "1";

var builder = WebApplication.CreateBuilder(args);

// The app's source-generated JSON context: Inertia.Net serializes prop values through these options (Native AOT friendly).
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddFastEndpoints(DiscoveredTypes.All);
// CSRF: the Inertia client sends the XSRF-TOKEN cookie back as X-XSRF-TOKEN; UseInertiaAntiforgery() issues and validates it.
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
builder.Services.AddInertia(o =>
{
    o.Share("appName", "Inertia.Net Sample");
    o.VersionResolver = _ => AppState.Version; // null: fall back to the hash of the Vite manifest
});

var app = builder.Build();

app.UseStaticFiles();
app.UseInertia();
app.UseInertiaAntiforgery(); // after UseAuthentication() in an app with sign-in
// Validation failures of Inertia requests become a redirect back with the errors (FluentValidation validators work as usual).
app.UseFastEndpoints(c =>
{
    c.UseInertia(ep => ep.AllowAnonymous());
    c.Serializer.Options.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    c.Binding.ReflectionCache.AddFromFastEndpointsSvelte(); // source-generated request DTO binding data (no expression compilation)
});

if (e2e)
{
    // Test hooks called by the E2E runner, not by a browser page: exempt from CSRF validation.
    app.MapPost("/__e2e/bump-version", () => AppState.Version = Guid.NewGuid().ToString("N")).DisableAntiforgery();
    app.MapPost("/__e2e/reset", AppState.Reset).DisableAntiforgery();
}

app.Run();
