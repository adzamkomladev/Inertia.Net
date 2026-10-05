# Samples and the shared sample contract

| Sample | Backend | Client | Backend port | Vite dev port |
|---|---|---|---|---|
| `MinimalApi.React` | Minimal API | React 19 | 5101 | 5173 |
| `Mvc.Vue` | MVC controllers | Vue 3 | 5102 | 5174 |
| `FastEndpoints.Svelte` | FastEndpoints | Svelte 5 | 5103 | 5175 |

All samples implement **exactly** the contract below, so the one Playwright suite in
[`tests/e2e`](../tests/e2e) (`specs/sample.spec.ts`) runs unchanged against each of them:
`SAMPLE=react|vue|svelte npx playwright test`.

The .NET project of a sample builds without Node (`dotnet build`); the frontend is built separately
(`npm install && npm run build` inside the sample, which writes `wwwroot/build` with a Vite manifest).

## Pages

The layout (used by every page) shows:
- shared prop `appName` in `data-testid="app-name"`;
- nav links with `data-testid` `nav-home` (`/`), `nav-users` (`/users`), `nav-contacts` (`/contacts/create`), `nav-feed` (`/feed`);
- a flash area `data-testid="flash"` that shows `page.flash.success` when present (empty otherwise).

| Route | Component | Contract |
|---|---|---|
| `GET /` | `Home` | `data-testid="home-title"`. |
| `GET /users` | `Users/Index` | `users`: array of `{id, name}`; each row `data-testid="user-row"` with a button `data-testid="delete-user-{id}"` that calls `router.delete('/users/{id}')`. `stats`: `Defer` (default group), the server sleeps about 300 ms; rendered with `<Deferred data="stats">`: `data-testid="stats-loading"` first, then `data-testid="stats"` containing `stats.total` (the number of users). `plans`: `Once` prop `{items: [...], resolvedCount}`; `data-testid="plans"` with `resolvedCount` in `data-testid="plans-count"`. The server increments a counter each time the loader really runs and sends it as `resolvedCount`, so the E2E can tell the prop was not re-resolved when the page is visited again. A button `data-testid="users-refresh"` calls `router.visit('/users')` (the client only sends `X-Inertia-Except-Once-Props` for once props held by the page it is currently on, so the test needs a Users to Users visit). |
| `DELETE /users/{id}` | n/a | Removes the user from an in-memory list and returns `Back()` with flash `success` = `User deleted`. Must reach the client as a **303**. |
| `GET /contacts/create` | `Contacts/Create` | Form 1 (`useForm`): inputs `data-testid="name"`, `data-testid="email"`, button `data-testid="submit"`, error slots `data-testid="error-name"`, `data-testid="error-email"` (empty element when no error). `POST /contacts` validates a record with `[Required] Name` and `[Required][EmailAddress] Email` using the framework's validation plus the adapter's validation glue; on failure it redirects back with errors, on success it redirects to `/` with flash `success` = `Contact created`. Form 2, error bag `newsletter`: input `data-testid="newsletter-email"`, button `data-testid="newsletter-submit"`, error slot `data-testid="error-newsletter-email"` showing `errors.newsletter.email`; `POST /newsletter` (`[Required][EmailAddress] Email`, sent with `X-Inertia-Error-Bag: newsletter`), success redirects to `/contacts/create` with flash `success` = `Subscribed`. |
| `GET /feed` | `Feed/Index` | `posts`: a `Scroll` prop (wrapper key `data`), 10 items per page, 5 pages, page number in query `page`. Rendered with `<InfiniteScroll data="posts">`; every item `data-testid="post"`. Items must be tall enough (about 160 px) that 10 of them overflow a normal viewport. |

## Test hooks (only mapped when the environment variable `E2E=1`)

- `POST /__e2e/bump-version`: changes the asset version (the sample's `VersionResolver` reads a mutable singleton; it is `null`, meaning the hash of the Vite manifest, until bumped).
- `POST /__e2e/reset`: restores the three seed users (ids 1 to 3), resets the once-prop counter and clears the version override. The spec calls it before every test.

Seed users: `1 Ada Lovelace`, `2 Grace Hopper`, `3 Alan Turing`.

## Production and dev wiring (all samples)

- Vite: `base: '/build/'`, `build.outDir: 'wwwroot/build'`, `build.manifest: true`; the dev server writes `wwwroot/hot` (its origin) while it runs and deletes it on exit.
- Root template `app.html` with `@inertiaHead`, `@vite("<entry>")` (plus `@viteReactRefresh` for React) and `@inertia`.
- Backend started for E2E as `dotnet run --project samples/<X> -c Release --no-launch-profile --urls http://localhost:510N` (Production environment, so the hot file is ignored).

## SSR (React sample only)

Enabled with the environment variable `INERTIA_SSR=1`:
- `npm run build` also builds the SSR bundle (`ssr/ssr.js`);
- the Node SSR server runs on port 13714 (`npm run ssr`), the backend renders through it (`InertiaOptions.Ssr.Enabled`), and falls back to client rendering if it is down;
- the server-rendered HTML contains `data-server-rendered="true"`.
