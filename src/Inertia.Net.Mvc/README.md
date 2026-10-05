# Inertia.Net.Mvc

Part of Inertia.Net, a .NET 10 server adapter for the Inertia.js v3 protocol.
See the repository README for documentation.

Controllers return the core results (`return Render("Users/Index", props);`, `Back()`, `Location(url)`). This package adds:

```csharp
builder.Services.AddInertia();
builder.Services.AddControllersWithViews().AddInertiaMvc(o => o.UseRazorRootView("App")); // UseRazorRootView is optional
```

- `InertiaModelStateFilter` (global): an Inertia non-GET request with invalid `ModelState` is redirected back with the errors in the
  `X-Inertia-Error-Bag` bag (default `default`). Keys are model paths with each segment through the MVC JSON naming policy
  (`Address.Street` becomes `address.street`). `[ApiController]` controllers work too: their automatic 400 is replaced by the same redirect for Inertia requests.
- Razor root view: `UseRazorRootView("App")` renders `Views/Shared/App.cshtml` (or a `~/Views/...` path) with an `InertiaRootViewContext` model.
  In the view, after `@addTagHelper *, Inertia.Net.Mvc`: `<inertia />`, `<inertia-head />` (SSR head), `<vite entry="src/app.ts, src/x.css" />`, `<vite-react-refresh />`.
  SSR runs at most once per request.

Not Native AOT compatible (MVC is not).
