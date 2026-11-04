# FastEndpoints.Svelte

Inertia.Net with FastEndpoints and a Svelte 5 client (`@inertiajs/svelte` v3, `@inertiajs/vite`, Vite). It implements the
[sample contract](../README.md) that the Playwright suite in `tests/e2e` checks.

- Backend: `Program.cs` (`AddFastEndpoints(new[] { DiscoveredTypes.All })`, `AddInertia`, `UseFastEndpoints(c => c.ConfigureInertia(...))`), `Endpoints/` (one class per route), `AppState.cs` (props types, source-generated `AppJsonContext`, in-memory state), root template `app.html`.
- The endpoints show both response styles: `Send.InertiaAsync` / `Send.InertiaBackAsync` from `HandleAsync`, and returning `Render(...)` / `Back().WithFlash(...)` from `ExecuteAsync`.
- Validation: FluentValidation `Validator<TRequest>` classes; `c.ConfigureInertia()` turns their failures into a redirect back with the errors (and honours `X-Inertia-Error-Bag`).
- AOT in spirit: the `FastEndpoints.Generator` package generates `DiscoveredTypes` (endpoint discovery) and request DTO binding data, the JSON contexts are source generated and `JsonSerializerIsReflectionEnabledByDefault` is off. Every request DTO (even those with only a route value) must be in `AppJsonContext`, because the Inertia client sends a JSON body on DELETE too.
- Frontend: `ClientApp/` (`app.ts`, `Layout.svelte`, `pages/**`). `@inertiajs/vite` resolves pages from `ClientApp/pages`. The `build` script runs `svelte-check` first.
- `vite.config.ts` also writes `wwwroot/hot` while the dev server runs, which is how Inertia.Net finds it (`@inertiajs/vite` does not write one).

## Run (development, hot reload)

Install dependencies and start Vite using npm, Bun, pnpm or Yarn:

| Package manager | Install dependencies | Start Vite | Build assets |
|---|---|---|---|
| npm | `npm install` | `npm run dev` | `npm run build` |
| Bun | `bun install` | `bun run dev` | `bun run build` |
| pnpm | `pnpm install` | `pnpm run dev` | `pnpm run build` |
| Yarn | `yarn install` | `yarn run dev` | `yarn run build` |

Keep Vite running in one terminal. In a second terminal:

```bash
dotnet run           # backend on http://localhost:5103 (Development, uses wwwroot/hot)
```

## Run (production build)

Run the `build` script from your package-manager row above, then start the backend:

```bash
dotnet run -c Release --no-launch-profile --urls http://localhost:5103
```

`dotnet build` never needs Node; build the frontend separately.

## E2E

These repository verification commands use the committed npm lockfile; they do not constrain your app's package manager.

```bash
cd ../../tests/e2e && npm ci && npx playwright install chromium
SAMPLE=svelte npx playwright test
```
