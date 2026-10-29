using FastEndpoints;
using FastEndpoints.Svelte;
using FastEndpointsSvelte;
using Inertia.Net.FastEndpoints;

var e2e = Environment.GetEnvironmentVariable("E2E") == "1";

var builder = WebApplication.CreateBuilder(args);

// The app's source-generated JSON context: Inertia.Net serializes prop values through these options (Native AOT friendly).
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddFastEndpoints(new[] { DiscoveredTypes.All });
// CSRF: the Inertia client sends the XSRF-TOKEN cookie back as X-XSRF-TOKEN; UseInertiaAntiforgery() issues and validates it.
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
builder.Services.AddInertia(o =>
{
    o.Share("appName", "Inertia.Net Sample");
    o.VersionResolver = _ => AppState.Version; // null: fall back to the hash of the Vite manifest
    o.Pages.Directory = "ClientApp/pages"; // Development only: rendering a component without a page file fails fast
});
builder.Services.AddResponseCompression(); // gzip and Brotli for the page JSON and HTML (see the README on HTTPS)
builder.Services.AddInertiaDevTools(); // the Inertia DevTools browser extension, in Development only

var app = builder.Build();

app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    // Vite puts a content hash in every file name under build/assets, so browsers can keep them for good.
    OnPrepareResponse = c =>
    {
        if (c.Context.Request.Path.StartsWithSegments("/build/assets"))
        {
            c.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    },
});
app.UseInertia();
app.UseInertiaAntiforgery(); // after UseAuthentication() in an app with sign-in
app.MapInertiaDevTools();
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
