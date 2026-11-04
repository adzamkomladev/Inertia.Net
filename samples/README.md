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
inside the sample, writing `wwwroot/build` with a Vite manifest. npm, Bun, pnpm and Yarn are supported:

| Package manager | Install dependencies | Start Vite | Build assets |
|---|---|---|---|
| npm | `npm install` | `npm run dev` | `npm run build` |
| Bun | `bun install` | `bun run dev` | `bun run build` |
| pnpm | `pnpm install` | `pnpm run dev` | `pnpm run build` |
| Yarn | `yarn install` | `yarn run dev` | `yarn run build` |

Use the package manager and lockfile chosen for your app. Repository CI uses `npm ci` with the committed `package-lock.json`
for reproducible sample and Playwright installs; that verification command is not an application requirement.

## Pages

The layout (used by every page) shows:
- shared prop `appName` in `data-testid="app-name"`;
- nav links with `data-testid` `nav-home` (`/`), `nav-users` (`/users`), `nav-contacts` (`/contacts/create`), `nav-feed` (`/feed`),
  and in the React sample `nav-live` (`/live`);
- a flash area `data-testid="flash"` that shows `page.flash.success` when present (empty otherwise).

| Route | Component | Contract |
|---|---|---|
| `GET /` | `Home` | `data-testid="home-title"`. |
| `GET /users` | `Users/Index` | `users`: array of `{id, name}` (React uses the `Page<User>` shape with items under `data`); each row `data-testid="user-row"` with a button `data-testid="delete-user-{id}"` that calls `router.delete('/users/{id}')`. `stats`: `Defer` (default group), the server sleeps about 300 ms; rendered with `<Deferred data="stats">`: `data-testid="stats-loading"` first, then `data-testid="stats"` containing `stats.total` (the number of users). `plans`: `Once` prop `{items: [...], resolvedCount}`; `data-testid="plans"` with `resolvedCount` in `data-testid="plans-count"`. The server increments a counter each time the loader really runs and sends it as `resolvedCount`, so the E2E can tell the prop was not re-resolved when the page is visited again. A button `data-testid="users-refresh"` calls `router.visit('/users')` (the client only sends `X-Inertia-Except-Once-Props` for once props held by the page it is currently on, so the test needs a Users to Users visit). |
| `DELETE /users/{id}` | n/a | Removes the user from an in-memory list and returns `Back()` with flash `success` = `User deleted`. Must reach the client as a **303**. |
| `GET /contacts/create` | `Contacts/Create` | Form 1 (`useForm`): inputs `data-testid="name"`, `data-testid="email"`, button `data-testid="submit"`, error slots `data-testid="error-name"`, `data-testid="error-email"` (empty element when no error). `POST /contacts` validates a record with `[Required] Name` and `[Required][EmailAddress] Email` using the framework's validation plus the adapter's validation glue; on failure it redirects back with errors, on success it redirects to `/` with flash `success` = `Contact created`. Form 2, error bag `newsletter`: input `data-testid="newsletter-email"`, button `data-testid="newsletter-submit"`, error slot `data-testid="error-newsletter-email"` showing `errors.newsletter.email`; `POST /newsletter` (`[Required][EmailAddress] Email`, sent with `X-Inertia-Error-Bag: newsletter`), success redirects to `/contacts/create` with flash `success` = `Subscribed`. |
| `GET /nope` (unmatched route) | `Error` | HTTP 404 on both first visits and Inertia navigation; `data-testid="error-status"` contains `404`, shared layout remains available, and `Return home` navigates to `/`. |
| `GET /feed` | `Feed/Index` | `posts`: a `Scroll` prop (wrapper key `data`), 10 items per page, 5 pages, page number in query `page`. Rendered with `<InfiniteScroll data="posts">`; every item `data-testid="post"`. Items must be tall enough (about 160 px) that 10 of them overflow a normal viewport. |

All samples enable `o.State.RememberPreviousUrl = true`. The shared `specs/back.spec.ts` test applies a `no-referrer` policy and verifies that invalid contact submissions return to the form with its input and errors intact.

## Pagination (React sample only)

| Route | Component | Contract |
|---|---|---|
| `GET /feed/cursor` | `Feed/Cursor` | 50 posts ordered by descending `id`, 10 per cursor page. `<InfiniteScroll data="posts">` renders `data-testid="post"`. EF Core queries a seeded in-memory SQLite database with keyset predicates; the paginator supplies opaque cursors. |
| `GET /users?page=` | `Users/Index` | Classic `Page<User>`, five users per page. `data-testid="pager"` contains `page-{n}` buttons that call `router.get` with `only: ['users']` and `preserveState`. |

`specs/pagination.spec.ts` runs with `SAMPLE=react`. It seeds twelve users using the E2E-only
`POST /__e2e/pagination/seed-users` hook after resetting; other specs keep the three standard users.

## Live props (React sample only)

The React sample also delivers [live props](../README.md#live-props) over SignalR (`Inertia.Net.Live.SignalR`): the hub is mapped at
`/inertia-live`, the client uses the transport file `ClientApp/lib/inertia-signalr.ts` (`createInertiaApp({ live: signalr() })`), and the
channel authorizer allows the demo user's private channel `users.1` (the sample has no sign-in). `specs/live.spec.ts` runs with
`SAMPLE=react` only.

| Route | Component | Contract |
|---|---|---|
| shared | every page | `inbox`: shared live prop `{unread}` on `LiveChannel.Private("users.1")`, event `InboxChanged`. |
| `GET /live` | `Live/Index` | `counter`: live prop `{value}` on `LiveChannel.Public("counter")`, event `CounterChanged`, rendered in `data-testid="counter"`; `data-testid="counter-loading"` shows while `useProp('counter').loading`. `inbox.unread` in `data-testid="inbox"`. A checkbox `data-testid="hold-updates"` registers `router.on('live')` returning `false` for `counter`, and `data-testid="held"` counts the events it held back. |
| `POST /__e2e/live/increment` | n/a | Increments the counter and publishes `CounterChanged` without values (clients reload `counter`). |
| `POST /__e2e/live/increment-with-value` | n/a | Increments the counter and publishes `CounterChanged` with `{ counter: { value } }` (clients apply it without a request). |
| `POST /__e2e/live/inbox` | n/a | Increments `unread` and publishes `InboxChanged` on `users.1`. |

When the page sets `window.__e2e = true` before the app starts (the spec does, with `addInitScript`), the app exposes its hub
connection as `window.__inertiaLive`, so the spec can stop it and check that missed events are caught up on reconnect.

## CSRF

Every sample registers `services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken)` and calls `app.UseInertiaAntiforgery()`
after `app.UseInertia()`. Page loads set the `XSRF-TOKEN` cookie, which the Inertia client sends back as `X-XSRF-TOKEN`; a `POST`, `PUT`,
`PATCH` or `DELETE` without a valid token gets **419**. The test hooks below are exempt (`DisableAntiforgery()`).

## Test hooks (only mapped when the environment variable `E2E=1`)

- `POST /__e2e/bump-version`: changes the asset version (the sample's `VersionResolver` reads a mutable singleton; it is `null`, meaning the hash of the Vite manifest, until bumped).
- `POST /__e2e/reset`: restores the three seed users (ids 1 to 3), resets the once-prop counter and clears the version override (and, in
  the React sample, the live counter and unread count). The spec calls it before every test.
- `GET /__e2e/boom`: throws an exception; the `Error` page renders with HTTP 500 and `data-testid="error-status"` containing `500`.
- React sample: the live publish hooks in [Live props](#live-props-react-sample-only).

Seed users: `1 Ada Lovelace`, `2 Grace Hopper`, `3 Alan Turing`.

## Production and dev wiring (all samples)

- Vite: `base: '/build/'`, `build.outDir: 'wwwroot/build'`, `build.manifest: true`; the dev server writes `wwwroot/hot` (its origin) while it runs and deletes it on exit.
- Root template `app.html` with `@inertiaHead`, `@vite("<entry>")` (plus `@viteReactRefresh` for React) and `@inertia`.
- Backend started for E2E as `dotnet run --project samples/<X> -c Release --no-launch-profile --urls http://localhost:510N` (Production environment, so the hot file is ignored).

## Production and development extras (all samples)

- Error pages: `AddInertiaErrorPages()` and `UseInertiaErrorPages()` before `UseInertia()` render navigation errors in Production. Development keeps the diagnostic modal unless `E2E=1`.
- Response compression (`AddResponseCompression()`) and `Cache-Control: public, max-age=31536000, immutable` for `/build/assets`.
- Development only: the pages check (`o.Pages.Directory = "ClientApp/pages"`) and the Inertia DevTools recorder
  (`AddInertiaDevTools()`, `MapInertiaDevTools()`); install the browser extension to see the visits.
- React sample: `o.Vite.Prefetch = VitePrefetch.Waterfall`, `MapInertia("/", "Home")`, and TypeScript declarations in
  `ClientApp/types/inertia.d.ts` (shared props and flash data), rewritten when the app starts in Development and committed so the
  build type-checks without running the app.

## SSR (React sample only)

Enabled with the environment variable `INERTIA_SSR=1`:
- the `build` script (run with any package manager above) also builds the SSR bundle (`ssr/ssr.js`);
- the backend starts and supervises its Node SSR child on `127.0.0.1:13714`, and falls back to client rendering while it is unavailable;
- set `INERTIA_SSR_PORT` to the same free port during both the frontend build and backend run to avoid collisions; managed mode rejects an occupied endpoint and does not shut down another server;
- the Vite configuration explicitly binds SSR to `127.0.0.1`; the backend stops a managed SSR server that also answers on another address. For intentional remote SSR, configure the listener host explicitly and use `o.Ssr.Urls` without `UseNodeProcess()` in .NET;
- the server-rendered HTML contains `data-server-rendered="true"`.
