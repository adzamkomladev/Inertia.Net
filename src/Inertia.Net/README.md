# Inertia.Net

The core of Inertia.Net, a .NET 10 server adapter for the [Inertia.js](https://inertiajs.com) **v3** protocol. It works with Minimal APIs (this package), MVC (`Inertia.Net.Mvc`) and FastEndpoints (`Inertia.Net.FastEndpoints`), with the React, Vue and Svelte clients, and is Native AOT and trim compatible.

```sh
dotnet add package Inertia.Net
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInertia(o => o.Share("appName", "Demo"));

var app = builder.Build();
app.UseStaticFiles();
app.UseInertia();

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

What is in the package: prop factories (lazy, optional, deferred, always, merge, once, scroll), partial reloads, shared data, flash, history encryption, asset versioning, Vite manifest and dev-server tags, SSR with a health check, validation and Precognition for Minimal APIs (`WithInertiaValidation()`, `WithInertiaPrecognition()`), the antiforgery cookie, and an encrypted-cookie state store.

Documentation: <https://github.com/OWNER/Inertia.Net#readme>
- [Quick start](https://github.com/OWNER/Inertia.Net#quick-start) and [client setup](https://github.com/OWNER/Inertia.Net#client-setup)
- [Props reference](https://github.com/OWNER/Inertia.Net#props-reference)
- [Native AOT](https://github.com/OWNER/Inertia.Net#native-aot)
- [Configuration reference](https://github.com/OWNER/Inertia.Net#configuration-reference)
