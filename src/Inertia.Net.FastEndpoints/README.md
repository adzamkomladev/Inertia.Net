# Inertia.Net.FastEndpoints

Part of Inertia.Net, a .NET 10 server adapter for the Inertia.js v3 protocol.
See the repository README for documentation.

```csharp
app.UseInertia();
app.UseFastEndpoints(c => c.UseInertia(ep => ep.AllowAnonymous())); // validation redirect + Precognition

public sealed class UsersEndpoint : EndpointWithoutRequest
{
    public override void Configure() => Get("/users");
    public override Task HandleAsync(CancellationToken ct) =>
        Send.InertiaAsync("Users/Index", new InertiaProps { ["users"] = users });   // also InertiaLocationAsync, InertiaBackAsync
}
```

- An endpoint implementing `ExecuteAsync` can return `Render(...)`, `Back()` or `Location(...)` directly (`Endpoint<TReq, InertiaResult>`); FastEndpoints sends any `IResult`.
- `c.UseInertia()` turns FastEndpoints validation failures of Inertia requests (not GET) into a redirect back with the errors (first message per field, JSON names, `X-Inertia-Error-Bag` honoured). Requests without `X-Inertia` keep the normal 400. It sets `c.Endpoints.Configurator`: pass your own configuration as the argument.
- It also answers Precognition requests (`Precognition: true`): 204, or 422 with `{ message, errors }` limited to `Precognition-Validate-Only`, without running the handler.
- Native AOT: register endpoints with the FastEndpoints source generator, `AddFastEndpoints(o => o.SourceGeneratorDiscoveredTypes.AddRange(DiscoveredTypes.All))`.
