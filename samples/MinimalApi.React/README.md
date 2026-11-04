# MinimalApi.React

Inertia.Net with Minimal APIs and a React 19 client (`@inertiajs/react` v3, `@inertiajs/vite`, Vite). It implements the
[sample contract](../README.md) that the Playwright suite in `tests/e2e` checks.

- Backend: `Program.cs` (Minimal API, `AddInertia`, `AddValidation` + `WithInertiaValidation()`, source-generated `AppJsonContext`), root template `app.html`.
- Frontend: `ClientApp/` (`app.tsx`, `ssr.tsx`, `Layout.tsx`, `pages/**`). `@inertiajs/vite` resolves pages from `ClientApp/pages` and wires up SSR.
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
dotnet run           # backend on http://localhost:5101 (Development, uses wwwroot/hot)
```

## Run (production build)

Run the `build` script from your package-manager row above, then start the backend:

```bash
dotnet run -c Release --no-launch-profile --urls http://localhost:5101
```

`dotnet build` never needs Node; build the frontend separately.

## Server-side rendering (optional)

Set `INERTIA_SSR=1` for your build command (npm shown; Bun, pnpm and Yarn use the same environment variable):

```bash
INERTIA_SSR=1 npm run build   # also builds ssr/ssr.js
INERTIA_SSR=1 dotnet run -c Release --no-launch-profile --urls http://localhost:5101
```

With `INERTIA_SSR=1` the app starts `node ssr/ssr.js` itself (`o.Ssr.UseNodeProcess()`, port 13714) and stops it on exit.
If the SSR server is down the backend logs it and falls back to client rendering.

## E2E

These repository verification commands use the committed npm lockfile; they do not constrain your app's package manager.

```bash
cd ../../tests/e2e && npm ci && npx playwright install chromium
SAMPLE=react npx playwright test            # add INERTIA_SSR=1 (after an SSR build) for the SSR spec
```
