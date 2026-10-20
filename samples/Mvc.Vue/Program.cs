using Inertia.Net.Mvc;
using Mvc.Vue;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInertia(o =>
{
    o.Share("appName", "Inertia.Net Sample");
    o.VersionResolver = _ => AppState.Version; // null: fall back to the hash of the Vite manifest
    o.Pages.Directory = "ClientApp/pages"; // Development only: rendering a component without a page file fails fast
});
builder.Services.AddResponseCompression(); // gzip and Brotli for the page JSON and HTML (see the README on HTTPS)
builder.Services.AddInertiaDevTools(); // the Inertia DevTools browser extension, in Development only
// The first-visit HTML is Views/Shared/App.cshtml (tag helpers) instead of an app.html template; the ModelState filter turns
// validation failures of Inertia requests into a redirect back with the errors.
builder.Services.AddControllersWithViews().AddInertiaMvc(o => o.UseRazorRootView("App"));
// CSRF: the Inertia client sends the XSRF-TOKEN cookie back as X-XSRF-TOKEN; UseInertiaAntiforgery() issues and validates it.
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);

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
app.MapControllers();
app.MapInertiaDevTools();

if (Environment.GetEnvironmentVariable("E2E") == "1")
{
    // Test hooks called by the E2E runner, not by a browser page: exempt from CSRF validation.
    app.MapPost("/__e2e/bump-version", () => AppState.Version = Guid.NewGuid().ToString("N")).DisableAntiforgery();
    app.MapPost("/__e2e/reset", AppState.Reset).DisableAntiforgery();
}

app.Run();
