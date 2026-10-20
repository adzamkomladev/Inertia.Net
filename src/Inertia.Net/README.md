# Inertia.Net

Version 1.4 adds cached props, an SSR response cache, Vite chunk prefetching, CSP nonces, generated TypeScript declarations and the Inertia DevTools protocol. One default changed: redirects to another origin become a full visit. Read the [1.4 upgrade notes](https://github.com/adzamkomladev/Inertia.Net/blob/main/docs/migrating-to-1.4.md) and the [changelog](https://github.com/adzamkomladev/Inertia.Net/blob/main/CHANGELOG.md). Start a new app with `dotnet new install Inertia.Net.Templates` and `dotnet new inertia`.

The core of Inertia.Net, a .NET 10 server adapter for the [Inertia.js](https://inertiajs.com) **v3** protocol. It works with Minimal APIs (this package), MVC (`Inertia.Net.Mvc`) and FastEndpoints (`Inertia.Net.FastEndpoints`), with the React, Vue and Svelte clients, and is Native AOT and trim compatible.

```sh
dotnet add package Inertia.Net
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInertia(o => o.Share("appName", "Demo"));
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);

var app = builder.Build();
app.UseStaticFiles();
app.UseInertia();
app.UseInertiaAntiforgery();   // XSRF-TOKEN cookie for the client; 419 for mutations without a valid token

app.MapGet("/users", () => Render("Users/Index", new InertiaProps
{
    ["users"] = users,
    ["stats"] = Defer(ct => statsService.GetAsync(ct), group: "sidebar"),
    ["plans"] = Once(ct => plans.AllAsync(ct)).Until(TimeSpan.FromHours(1)),
}));

app.Run();
```

```html
<!-- app.html, in the content root -->
<!DOCTYPE html>
<html>
<head>@viteReactRefresh @vite("resources/js/app.tsx") @inertiaHead</head>
<body>@inertia</body>
</html>
```

`Render`, `Defer`, `Back()` and `Location(...)` are called unqualified: the package adds `global using Inertia.Net;` and `global using static Inertia.Net.Inertia;` when `ImplicitUsings` is enabled (`Inertia.Render` does not compile outside the `Inertia.Net` namespace). Opt out with `<InertiaNetImplicitUsings>false</InertiaNetImplicitUsings>`.

What is in the package: prop factories (lazy, optional, deferred, always, merge, once, scroll) with opt-in parallel loaders and cached props, partial reloads, shared data, flash, history encryption, asset versioning, Vite manifest and dev-server tags, SSR (several servers, a supervised Node process pool, circuit breakers, render slots, per-response modes, metrics and a health check), validation and Precognition for Minimal APIs (`WithInertiaValidation()`, `WithInertiaPrecognition()`), CSRF protection (`UseInertiaAntiforgery()`), an encrypted-cookie state store, an SSR response cache, Vite chunk prefetching and CSP nonces, `MapInertia` page endpoints, TypeScript declarations from your C# page types, and the Inertia DevTools recorder (`AddInertiaDevTools()`).

Documentation: <https://github.com/adzamkomladev/Inertia.Net#readme>
- [Quick start](https://github.com/adzamkomladev/Inertia.Net#quick-start) and [client setup](https://github.com/adzamkomladev/Inertia.Net#client-setup)
- [Props reference](https://github.com/adzamkomladev/Inertia.Net#props-reference)
- [Native AOT](https://github.com/adzamkomladev/Inertia.Net#native-aot)
- [Configuration reference](https://github.com/adzamkomladev/Inertia.Net#configuration-reference)
