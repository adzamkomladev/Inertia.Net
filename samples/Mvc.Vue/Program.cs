using Inertia.Net.Mvc;
using Mvc.Vue;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInertia(o =>
{
    o.Share("appName", "Inertia.Net Sample");
    o.VersionResolver = _ => AppState.Version; // null: fall back to the hash of the Vite manifest
});
// The first-visit HTML is Views/Shared/App.cshtml (tag helpers) instead of an app.html template; the ModelState filter turns
// validation failures of Inertia requests into a redirect back with the errors.
builder.Services.AddControllersWithViews().AddInertiaMvc(o => o.UseRazorRootView("App"));
// CSRF: the Inertia client sends the XSRF-TOKEN cookie back as X-XSRF-TOKEN; UseInertiaAntiforgery() issues and validates it.
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);

var app = builder.Build();

app.UseStaticFiles();
app.UseInertia();
app.UseInertiaAntiforgery(); // after UseAuthentication() in an app with sign-in
app.MapControllers();

if (Environment.GetEnvironmentVariable("E2E") == "1")
{
    // Test hooks called by the E2E runner, not by a browser page: exempt from CSRF validation.
    app.MapPost("/__e2e/bump-version", () => AppState.Version = Guid.NewGuid().ToString("N")).DisableAntiforgery();
    app.MapPost("/__e2e/reset", AppState.Reset).DisableAntiforgery();
}

app.Run();
