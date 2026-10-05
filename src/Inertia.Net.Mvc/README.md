# Inertia.Net.Mvc

MVC integration for [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net), a .NET 10 server adapter for the Inertia.js v3 protocol. Install it next to `Inertia.Net`:

```sh
dotnet add package Inertia.Net
dotnet add package Inertia.Net.Mvc
```

```csharp
builder.Services.AddInertia();
builder.Services.AddControllersWithViews().AddInertiaMvc(o => o.UseRazorRootView("App")); // UseRazorRootView is optional

var app = builder.Build();
app.UseStaticFiles();
app.UseInertia();
app.MapControllers();
```

```csharp
public sealed class UsersController : Controller
{
    [HttpGet("/users")]
    public IResult Index() => Render("Users/Index", new InertiaProps { ["users"] = users });   // also Back(), Location(url)

    [HttpPost("/users")]
    public IActionResult Store([FromBody] CreateUserInput input) => Redirect("/users");
}
```

- **Validation:** a global filter redirects an Inertia non-GET request with an invalid `ModelState` back with the errors, in the bag named by `X-Inertia-Error-Bag` (default `default`). Keys go through the JSON naming policy per segment (`Address.Street` becomes `address.street`). `[ApiController]` controllers are covered too.
- **Razor root view (optional):** `UseRazorRootView("App")` renders `Views/Shared/App.cshtml` with an `InertiaRootViewContext` model. After `@addTagHelper *, Inertia.Net.Mvc` the view can use `<inertia />`, `<inertia-head />`, `<vite entry="resources/js/app.tsx" />` and `<vite-react-refresh />`.
- **TempData state store (optional):** `AddInertiaMvc(o => o.UseTempDataStateStore())` keeps flash data, errors and history flags in TempData (cookie provider by default, or `AddSessionStateTempDataProvider()` plus `AddSession()`/`app.UseSession()` before `app.UseInertia()`). It also serves Minimal API endpoints.
- Not Native AOT compatible (MVC is not).

Documentation:
- [Quick start (MVC)](https://github.com/adzamkomladev/Inertia.Net#mvc)
- [Root template and Razor](https://github.com/adzamkomladev/Inertia.Net#the-root-template)
- [Validation and error bags](https://github.com/adzamkomladev/Inertia.Net#validation-and-error-bags)
