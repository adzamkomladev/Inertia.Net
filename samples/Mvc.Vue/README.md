# Mvc.Vue

Inertia.Net with MVC controllers, a Razor root view and a Vue 3 client (`@inertiajs/vue3` v3, `@inertiajs/vite`, Vite). It implements the
[sample contract](../README.md) that the Playwright suite in `tests/e2e` checks.

- Backend: `Program.cs` (`AddInertia`, `AddControllersWithViews().AddInertiaMvc(o => o.UseRazorRootView("App"))`), `Controllers/PagesController.cs`
  and `Controllers/LiveController.cs`, in-memory state in `AppState.cs`, the cursor feed's SQLite database in `FeedDbContext.cs`. The home page is
  `MapInertia("/", "Home")`, so its generated route names the component for `<Link instant>`.
- Root view: `Views/Shared/App.cshtml` with the `<inertia-head />`, `<vite entry="ClientApp/app.ts" />` and `<inertia />` tag helpers (registered in `Views/_ViewImports.cshtml`).
- Validation: DataAnnotations on the form models plus the `InertiaModelStateFilter` that `AddInertiaMvc` registers. A failed Inertia request is redirected back with the errors (error bag `newsletter` for the newsletter form). The controllers are not `[ApiController]`, so the JSON form posts need `[FromBody]`. A `useHttp` request (`/contacts/check`) gets a 422 with
  `{ message, errors }` instead. The Precognition login demo (`LoginDemo.cs`) is a Minimal API endpoint (`AddValidation()`,
  `WithInertiaPrecognition()`), with the rate limits described in the [sample contract](../README.md).
- Live props: SignalR by default (`MapInertiaLiveHub()`, `ClientApp/lib/inertia-signalr.ts`); `LIVE_TRANSPORT=sse` on the server with a
  `VITE_LIVE_TRANSPORT=sse` client build uses Server-Sent Events. `LIVE_BACKPLANE=redis|postgres` (`REDIS_URL`, `POSTGRES_URL`) shares
  events between instances. `ClientApp/lib/inertia-*.ts` are the same files as in the React sample.
- Frontend: `ClientApp/` (`app.ts`, `ssr.ts`, `Layout.vue`, `pages/**`). `@inertiajs/vite` resolves pages from `ClientApp/pages` and wires up SSR.
- `ClientApp/types/inertia.d.ts` and `routes.ts` are written by the app in Development (or `dotnet run -- --inertia-types`) and committed.
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

## Server-side rendering (optional)

```bash
INERTIA_SSR=1 npm run build   # also builds ssr/ssr.js
INERTIA_SSR=1 dotnet run -c Release --no-launch-profile --urls http://localhost:5102
```

With `INERTIA_SSR=1` the app starts `node ssr/ssr.js` itself (`o.Ssr.UseNodeProcess()`, port 13714, or `INERTIA_SSR_PORT` for both
commands) and stops it on exit. The Razor `<inertia />` and `<inertia-head />` tag helpers render its output.

## E2E

These repository verification commands use the committed npm lockfile; they do not constrain your app's package manager.

```bash
cd ../../tests/e2e && npm ci && npx playwright install chromium
SAMPLE=vue npx playwright test            # add INERTIA_SSR=1 (after an SSR build) for the SSR spec
```
