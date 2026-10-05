# MinimalApi.React

Inertia.Net with Minimal APIs and a React 19 client (`@inertiajs/react` v3, `@inertiajs/vite`, Vite). It implements the
[sample contract](../README.md) that the Playwright suite in `tests/e2e` checks.

- Backend: `Program.cs` (Minimal API, `AddInertia`, `AddValidation` + `WithInertiaValidation()`, source-generated `AppJsonContext`), root template `app.html`.
- Frontend: `ClientApp/` (`app.tsx`, `ssr.tsx`, `Layout.tsx`, `pages/**`). `@inertiajs/vite` resolves pages from `ClientApp/pages` and wires up SSR.
- `vite.config.ts` also writes `wwwroot/hot` while the dev server runs, which is how Inertia.Net finds it (`@inertiajs/vite` does not write one).

## Run (development, hot reload)

```bash
npm install
npm run dev          # Vite on http://localhost:5173
dotnet run           # backend on http://localhost:5101 (Development, uses wwwroot/hot)
```

## Run (production build)

```bash
npm install
npm run build        # wwwroot/build + manifest
dotnet run -c Release --no-launch-profile --urls http://localhost:5101
```

`dotnet build` never needs Node; build the frontend separately.

## Server-side rendering (optional)

```bash
INERTIA_SSR=1 npm run build   # also builds ssr/ssr.js
npm run ssr                   # Node SSR server on :13714
INERTIA_SSR=1 dotnet run -c Release --no-launch-profile --urls http://localhost:5101
```

If the SSR server is down the backend logs it and falls back to client rendering.

## E2E

```bash
cd ../../tests/e2e && npm install && npx playwright install chromium
SAMPLE=react npx playwright test            # add INERTIA_SSR=1 (after an SSR build) for the SSR spec
```
