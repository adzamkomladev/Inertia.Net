# FastEndpoints.Svelte

Inertia.Net with FastEndpoints and a Svelte 5 client (`@inertiajs/svelte` v3, `@inertiajs/vite`, Vite). It implements the
[sample contract](../README.md) that the Playwright suite in `tests/e2e` checks.

- Backend: `Program.cs` (`AddFastEndpoints(DiscoveredTypes.All)`, `AddInertia`, `UseFastEndpoints(c => c.UseInertia(...))`), `Endpoints/` (one class per route), `AppState.cs` (props types, source-generated `AppJsonContext`, in-memory state), root template `app.html`.
- The endpoints show both response styles: `Send.InertiaAsync` / `Send.InertiaBackAsync` from `HandleAsync`, and returning `Render(...)` / `Back().WithFlash(...)` from `ExecuteAsync`.
- Validation: FluentValidation `Validator<TRequest>` classes; `c.UseInertia()` turns their failures into a redirect back with the errors (and honours `X-Inertia-Error-Bag`).
- AOT in spirit: the `FastEndpoints.Generator` package generates `DiscoveredTypes` (endpoint discovery) and request DTO binding data, the JSON contexts are source generated and `JsonSerializerIsReflectionEnabledByDefault` is off. Every request DTO (even those with only a route value) must be in `AppJsonContext`, because the Inertia client sends a JSON body on DELETE too.
- Frontend: `ClientApp/` (`app.ts`, `Layout.svelte`, `pages/**`). `@inertiajs/vite` resolves pages from `ClientApp/pages`. `npm run build` runs `svelte-check` first.
- `vite.config.ts` also writes `wwwroot/hot` while the dev server runs, which is how Inertia.Net finds it (`@inertiajs/vite` does not write one).

## Run (development, hot reload)

```bash
npm install
npm run dev          # Vite on http://localhost:5175
dotnet run           # backend on http://localhost:5103 (Development, uses wwwroot/hot)
```

## Run (production build)

```bash
npm install
npm run build        # wwwroot/build + manifest
dotnet run -c Release --no-launch-profile --urls http://localhost:5103
```

`dotnet build` never needs Node; build the frontend separately.

## E2E

```bash
cd ../../tests/e2e && npm install && npx playwright install chromium
SAMPLE=svelte npx playwright test
```
