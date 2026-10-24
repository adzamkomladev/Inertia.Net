# Inertia.Net

Version 1.5 lets you remove, replace and tag cached props and SSR renders (`IInertiaCache`), serve them stale while they refresh (`staleFor`), and keep them in a cache store of your choice (`IInertiaCacheStore`). Upgrading from 1.3 or earlier? Read the [1.4 upgrade notes](https://github.com/adzamkomladev/Inertia.Net/blob/main/docs/migrating-to-1.4.md): one default changed (redirects to another origin become a full visit). The [changelog](https://github.com/adzamkomladev/Inertia.Net/blob/main/CHANGELOG.md) lists every release. Start a new app with `dotnet new install Inertia.Net.Templates` and `dotnet new inertia`.

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

## Performance

v1.5.2 allocates less per render than v1.5.1: a JSON page with 20 props drops from 2,152 to 1,736 bytes, a partial reload from 984 to
568 bytes, and a render with 20 parallel loaders from 6,496 to 3,992 bytes. Pages without parallel props also skip the parallel
planning walk.

Compared with published v1.5.0, v1.5.1 measured 43.3% faster 1 KiB cache writes, 56.5% faster 1 MiB cache writes, 8.1% faster
integer-key dictionary rendering, and 87.0% faster 32-request cold-cache bursts in controlled fixtures. Ordinary JSON, HTML and
partial rendering showed no statistically clear slowdown. JSON render allocations fell from 2,760 to 2,152 bytes; partial reload
allocations fell from 1,592 to 984 bytes. These are fixture measurements, not production throughput guarantees.

See [benchmark results and confidence intervals](https://github.com/adzamkomladev/Inertia.Net/blob/main/tests/Inertia.Net.Benchmarks/RESULTS.md)
for package hashes, raw samples and the distinction between the published-release comparison and earlier incremental GC/RSS experiments.
