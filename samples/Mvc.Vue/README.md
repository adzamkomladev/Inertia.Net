# Mvc.Vue

Inertia.Net with MVC controllers, a Razor root view and a Vue 3 client (`@inertiajs/vue3` v3, `@inertiajs/vite`, Vite). It implements the
[sample contract](../README.md) that the Playwright suite in `tests/e2e` checks.

- Backend: `Program.cs` (`AddInertia`, `AddControllersWithViews().AddInertiaMvc(o => o.UseRazorRootView("App"))`), `Controllers/PagesController.cs`, in-memory state in `AppState.cs`.
- Root view: `Views/Shared/App.cshtml` with the `<inertia-head />`, `<vite entry="ClientApp/app.ts" />` and `<inertia />` tag helpers (registered in `Views/_ViewImports.cshtml`).
- Validation: DataAnnotations on the form models plus the `InertiaModelStateFilter` that `AddInertiaMvc` registers. A failed Inertia request is redirected back with the errors (error bag `newsletter` for the newsletter form). The controllers are not `[ApiController]`, so the JSON form posts need `[FromBody]`.
- Frontend: `ClientApp/` (`app.ts`, `Layout.vue`, `pages/**`). `@inertiajs/vite` resolves pages from `ClientApp/pages`. No SSR in this sample.
- `vite.config.ts` also writes `wwwroot/hot` while the dev server runs, which is how Inertia.Net finds it (`@inertiajs/vite` does not write one).
- TypeScript is pinned to 5.9 because `vue-tsc` does not support TypeScript 7 yet.

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
dotnet run           # backend on http://localhost:5102 (Development, uses wwwroot/hot)
```

## Run (production build)

Run the `build` script from your package-manager row above, then start the backend:

```bash
dotnet run -c Release --no-launch-profile --urls http://localhost:5102
```

`dotnet build` never needs Node; build the frontend separately.

## E2E

These repository verification commands use the committed npm lockfile; they do not constrain your app's package manager.

```bash
cd ../../tests/e2e && npm ci && npx playwright install chromium
SAMPLE=vue npx playwright test
```
