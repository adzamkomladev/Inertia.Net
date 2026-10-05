# Inertia.Net.FastEndpoints

FastEndpoints integration for [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net), a .NET 10 server adapter for the Inertia.js v3 protocol. Install it next to `Inertia.Net`:

```sh
dotnet add package Inertia.Net
dotnet add package Inertia.Net.FastEndpoints
```

```csharp
builder.Services.AddInertia();
builder.Services.AddFastEndpoints();

var app = builder.Build();
app.UseStaticFiles();
app.UseInertia();
app.UseFastEndpoints(c => c.UseInertia(ep => ep.AllowAnonymous()));   // validation redirect + Precognition
```

```csharp
public sealed class UsersEndpoint : EndpointWithoutRequest
{
    public override void Configure() => Get("/users");
    public override Task HandleAsync(CancellationToken ct) =>
        Send.InertiaAsync("Users/Index", new InertiaProps { ["users"] = users });   // also InertiaLocationAsync, InertiaBackAsync
}
```

- An endpoint that implements `ExecuteAsync` can return `Render(...)`, `Back()` or `Location(...)` directly (`Endpoint<TReq, InertiaResult>`): FastEndpoints sends any `IResult`.
- `c.UseInertia()` turns validation failures of Inertia requests (not GET) into a redirect back with the errors (JSON names, `X-Inertia-Error-Bag` honoured). Other requests keep the normal 400. It sets `c.Endpoints.Configurator`: pass your own configuration as the argument.
- It also answers Precognition requests (`Precognition: true`) with 204 or 422 `{ message, errors }`, without running the handler.
- Native AOT: register endpoints with the FastEndpoints source generator, `AddFastEndpoints(DiscoveredTypes.All)`.

Documentation:
- [Quick start (FastEndpoints)](https://github.com/adzamkomladev/Inertia.Net#fastendpoints)
- [Validation and Precognition](https://github.com/adzamkomladev/Inertia.Net#validation-and-error-bags)
- [Native AOT](https://github.com/adzamkomladev/Inertia.Net#native-aot)
