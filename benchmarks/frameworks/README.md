# Framework comparison

The same small app, built six ways, measured the same way:

| App | Stack | Folder |
|---|---|---|
| Inertia.Net | ASP.NET Core Minimal API on .NET 10 + Inertia.Net (this repository, by project reference) + React. Run as a normal (JIT) build and as a Native AOT build. | `inertianet/` |
| Next.js | Next.js 16.4.6, App Router, `output: 'standalone'` | `nextjs/` |
| TanStack Start | TanStack Start 1.169.5 on Vite 8 with the `nitro/vite` plugin (Node server) | `tanstack/` |
| Nuxt | Nuxt 4.6.4 (Vue 3.5.45) on Nitro 2.13.6, `node-server` preset | `nuxt/` |
| Laravel | Laravel 13 (framework 13.42.0) + inertia-laravel 3.7.0 + React, served by Laravel Octane 2.24.0 on FrankenPHP 1.13.5 (worker mode) | `laravel/` |
| Go | Go 1.26 + Gin 1.12 + GORM 1.31 (pgx driver) + gonertia 3.0 + React, built as one static binary | `golang/` |

Every app shows the same React pages, written in TypeScript (`shared/ts/*Page.tsx`, copied into each app at build time), except Nuxt, which renders Vue ports of those pages (`nuxt/app/components`) with the same markup. Each app
reads the same Postgres tables (`shared/schema.sql`) through the ORM its community normally uses, caches the dashboard in Redis
for 60 seconds the framework's usual way, validates the same form with its usual validation library, and runs the same
CPU-heavy algorithm. A parity check proves they all return the same rows, checksums and validation messages before anything is
measured.

## How each app is written

The aim is that each app looks like what a team would write in that framework, not the fastest code each one could run.

| | Inertia.Net | Laravel | Next.js | TanStack Start | Nuxt | Go |
|---|---|---|---|---|---|---|
| Data access | EF Core 10 (Npgsql provider), paging with `Inertia.Net.EntityFrameworkCore`'s `ToPageAsync` | Eloquent models | Drizzle ORM on node-postgres | Drizzle ORM on node-postgres | Drizzle ORM on node-postgres | GORM 1.31 on the pgx driver |
| Dashboard cache (60 s, Redis) | Inertia.Net's prop cache, `InertiaProp.Cache("dashboard", ...)`, on the `Inertia.Net.Redis` store | `Cache::remember` on the Redis store | A `'use cache: remote'` function with a Redis cache handler (Cache Components) | Nitro's `defineCachedFunction`, with Nitro's storage mounted on Redis (unstorage's Redis driver) by a Nitro plugin | Nitro's `defineCachedFunction`, with Nitro's storage mounted on Redis (unstorage's Redis driver) by a Nitro plugin | A direct `GET`/`SET bench:dashboard` with go-redis |
| Form validation | .NET 10 validation (`AddValidation()`, `IValidatableObject`) with `WithInertiaValidation()` | A Form Request | zod | zod | zod | Hand-written rules, the same four messages, stored through gonertia's flash provider |
| Form endpoint | `POST /todos`, Inertia redirect back with errors | `POST /todos`, Inertia redirect back with errors | `POST /api/todos` route handler: 201, or 422 with errors | `POST /api/todos` server route: 201, or 422 with errors | `POST /api/todos` Nitro server route: 201, or 422 with errors | `POST /todos`, Inertia redirect back with errors |
| Request forgery protection | Antiforgery with the `XSRF-TOKEN` cookie (as in the Inertia.Net template) | CSRF with the `XSRF-TOKEN` cookie, cookie sessions | None by default for route handlers | None by default for server routes | None by default for server routes | None by default; a session cookie only |

**The Native AOT build uses plain Npgsql, not EF Core.** EF Core 10's `DbContext` is not trim- or AOT-safe (its constructor warns
IL2026/IL3050), `ToPageAsync` builds its query inside the library where EF's query precompiler cannot see it, and the
precompiler's interceptors do not compile with the .NET 11 SDK this repository builds with. The csproj switches to an Npgsql data
layer (`BenchData.Npgsql.cs`, same routes and results) whenever `PublishAot=true`. This is what the Inertia.Net docs recommend for
Native AOT apps today.

**The Go app uses the stack a Go team would pick**: Gin for routing, GORM for the database, gonertia for Inertia, and go-redis for
the dashboard cache. gonertia renders the shared React pages through the same `@inertiajs/vite` plugin as Inertia.Net; with
`BENCH_SSR=1` it posts the page JSON to `ssr/ssr.js`, which `start.sh` runs beside the Go process. The form's four rules are
written by hand with the same messages, and validation errors are kept in memory keyed by a session cookie until the redirect
back to the form. gonertia itself has no CSRF protection or SSR cache, so the Go app has neither.

The Next.js, TanStack Start and Nuxt forms post to a JSON route rather than a server action or server function, because a load test needs
a stable URL to post to; a route handler is a normal way to build a form API in both.

## Results

From one GitHub Actions run on 2026-11-16 with `split=routes`: each route ran on its own 4-CPU runner with all six apps on
it, each app limited to 2 CPUs and 2 GB. The runners had different CPUs (AMD EPYC and Intel Xeon), so compare apps along a
row, not one row with another. The full tables, with every route in both modes, latency percentiles and fixed-rate runs, are in
[results/2026-11-16-v3.md](results/2026-11-16-v3.md). No run was marked overloaded: every request succeeded, and every SSR
response was really server-rendered. Earlier runs, of earlier versions of the apps, are in [results/](results/).

**Memory and startup** (measured on one runner, AMD EPYC 9V45)

| App | Memory at rest, SPA / SSR | Cold start, SPA / SSR | Image, SPA / SSR |
|---|---:|---:|---:|
| Inertia.Net, Native AOT (Npgsql) | **17** / 46 MB | **216** / 514 ms | **34** / 98 MB |
| Inertia.Net, JIT (EF Core) | 27 / 60 MB | 522 / 1108 ms | 125 / 189 MB |
| TanStack Start | 28 / 29 MB | 263 / **258** ms | 166 MB |
| Next.js | 72 / 76 MB | 715 / 775 ms | 185 MB |
| Nuxt | 138 / 139 MB | 340 / 739 ms | 169 MB |
| Laravel (Octane, FrankenPHP) | 98 / 154 MB | 785 / 951 ms | 252 / 311 MB |

Memory is the whole container: for SSR that includes the Node renderer, for Laravel all its PHP workers.

**Requests per second** (16 connections, SSR mode; higher is better)

| Route | Inertia.Net JIT | Inertia.Net AOT | TanStack Start | Nuxt | Next.js | Laravel |
|---|---:|---:|---:|---:|---:|---:|
| `/todos?perPage=25`, first visit | 1,764 | **3,905** | 1,294 | 745 | 364 | 337 |
| `/todos?perPage=25`, client-side navigation (JSON) | 6,155 | **9,789** | 3,962 | 5,498 | 1,512 | 550 |
| `/dashboard` (cached in Redis), client-side navigation | **13,465** | 12,842 | 7,153 | 10,262 | 2,283 | 1,056 |
| New todo, valid post (validate and insert) | 2,810 | **2,956** | 2,464 | 1,996 | 957 | 517 |
| New todo, invalid post (four errors) | 16,314 | **18,299** | 12,559 | 11,417 | 1,841 | 854 |
| `/compute?n=20000` (CPU-heavy, JSON) | **1,212** | 1,033 | 227 | 240 | 207 | 81 |

**Using more CPUs** (`/compute/parallel`, JSON, 16 connections)

| App | 1 CPU | 2 CPUs | 4 CPUs |
|---|---:|---:|---:|
| Inertia.Net, JIT | 648 | 1,260 | 1,938 |
| Inertia.Net, Native AOT | 614 | 1,062 | 1,576 |
| Nuxt | 204 | 367 | 499 |
| TanStack Start | 197 | 361 | 493 |
| Next.js | 173 | 341 | 431 |
| Laravel (sequential in PHP) | 56 | 79 | 157 |

What stands out:

- **Inertia.Net served the most requests on every route.** The Native AOT build served 3 times as many SSR first visits as
  TanStack Start, 5 times as many as Nuxt and 11 to 12 times as many as Next.js and Laravel.
- **The data layer matters more than JIT or AOT.** On database routes the AOT build, on plain Npgsql, beat the JIT build on EF
  Core: 59% more JSON navigations and twice the SSR first visits. On the CPU-heavy route, where neither touches the database,
  JIT was about 17% faster.
- **Saving a todo is limited by the database for every app.** Valid posts are close across the fast apps, and their p99 rises to
  48 to 290 ms in every app, so the insert, not the framework, sets the pace. Invalid posts, which write nothing, show the
  framework cost: Inertia.Net served 16,000 to 18,000 per second with validation, antiforgery and a redirect.
- **Nuxt's JSON routes are fast, its SSR pages are not.** Its `/api` routes kept up with Inertia.Net on small payloads
  (27,698 req/s for `/` JSON against 27,584), but its server-rendered pages came behind TanStack Start's. Nuxt renders Vue
  pages, not the shared React ones, so part of that gap is Vue's SSR, not Nuxt itself.
- **TanStack Start is the leanest Node app**: 28 MB at rest and a 258 ms cold start, the fastest SSR cold start of all six.
- **CPU-heavy work is mostly a language-speed test.** Node runs one request's JavaScript at a time per process, so plain
  `/compute` stays near 200 to 240 req/s at any CPU count in all three Node apps. .NET spreads both routes over every core with
  no extra code.
- **Laravel and Next.js can't keep up with 200 req/s on `/compute`,** so requests queue for one to seven seconds (see the
  fixed-rate columns in the full results).
- **Next.js "SPA (nearest)" pages are a static shell.** The page then fetches its data from an API route. Its `page` rows measure
  serving that shell, so compare its `json` rows instead.

Read these numbers as one run on shared cloud machines, using Alpine images (see [Caveats](#caveats)). The load client ran on
the same machine as the apps. Image sizes are what that runner's Docker reported, and can differ from `docker images` on another
machine.

## Inertia.Net with its caching features on

The comparison above runs every app without page caching. Inertia.Net has caching features an app can turn on, and the
`inertianet-tuned` and `inertianet-aot-tuned` builds (the same images started with `BENCH_TUNED=1`) use them:

- **SSR render cache** (`o.Ssr.Cache`): a page whose data hasn't changed reuses its rendered HTML for 60 s, then is served stale
  for up to 5 minutes while it renders again in the background.
- **Two Node SSR instances** (`UseNodeProcess(n => n.Instances = 2)`), one per CPU, instead of one.
- **The in-memory cache store**, Inertia.Net's default, instead of Redis: right for a single instance.
- **Stale-while-revalidate prop caches**: the dashboard, and each todo list page for 10 s.

From one GitHub Actions run on 2026-11-16 ([results/2026-11-16-inertianet-tuned.md](results/2026-11-16-inertianet-tuned.md)),
split by route, with all four builds on each route's runner (requests per second, SSR mode):

| Route | JIT | JIT, tuned | Native AOT | Native AOT, tuned |
|---|---:|---:|---:|---:|
| `/`, first visit | 3,528 | **20,800** | 4,378 | 17,952 |
| `/todos?perPage=25`, first visit | 1,561 | **25,756** | 3,293 | 23,443 |
| `/todos?perPage=100`, first visit | 562 | **15,119** | 1,033 | 13,745 |
| `/dashboard`, first visit | 2,819 | **20,903** | 3,586 | 18,054 |
| `/todos/new`, first visit | 7,068 | **56,746** | 7,773 | 49,923 |
| `/todos?perPage=25`, client-side navigation | 4,248 | **17,565** | 7,276 | 16,047 |
| `/dashboard`, client-side navigation | 9,140 | **13,102** | 8,910 | 11,353 |

- **Cached first visits are 4 to 27 times faster**, and their median response fell from 2 to 25 ms to about 1 ms or less: a
  cached render skips the Node renderer entirely.
- **Form posts and the CPU-heavy routes didn't change**, as expected: nothing there is cached.
- **The second Node process costs memory**: at rest with SSR, 84 MB instead of 60 (JIT) and 75 instead of 46 (Native AOT).

These numbers show what Inertia.Net can do with its features on. They aren't in the main table because the other apps had no
caching on in that run; the next section turns it on for all of them. Cache only pages that are the same for everyone, or whose cache
key covers the user; see the [response cache docs](../../README.md#6-response-cache).

## Every framework with its caching on

Every app also has a tuned build (`<id>-tuned`, `BENCH_TUNED=1`) with its own framework's caching on, set like Inertia.Net tuned:
pages for 60 s and todo lists for 10 s, each served stale while it refreshes, kept in the app's memory. Compute and the form post
are never cached, so tuned apps skip those routes.

| App | What its tuned build caches |
|---|---|
| Inertia.Net | The SSR render (by a hash of the page's data) and the todo and dashboard props (`.Cache(...)`); two Node renderers. The handler still runs on every request. |
| Next.js | `'use cache'` on the todo and dashboard page components and the JSON route data ([lib/tuned.tsx](nextjs/lib/tuned.tsx)). |
| TanStack Start, Nuxt | Nitro `routeRules` caching the whole GET response of the pages and `/api` routes. |
| Laravel | The whole response, through [spatie/laravel-responsecache](https://github.com/spatie/laravel-responsecache) with a profile that also caches Inertia visits ([BenchCacheProfile.php](laravel/app/Support/BenchCacheProfile.php)). |

From one GitHub Actions run on 2026-11-16 ([results/2026-11-16-all-tuned.md](results/2026-11-16-all-tuned.md)), split by route
with all twelve builds on each route's runner. Requests per second for first visits (SSR), plain → tuned:

| Route | Inertia.Net (JIT) | Inertia.Net (Native AOT) | Next.js | TanStack Start | Nuxt | Laravel |
|---|---:|---:|---:|---:|---:|---:|
| `/` | 9,548 → **55,711** | 10,812 → 50,970 | 555 → 558 | 2,567 → 12,997 | 1,196 → 18,471 | 572 → 1,177 |
| `/todos?perPage=25` | 2,201 → **34,020** | 4,567 → 32,889 | 380 → 439 | 1,827 → 9,543 | 908 → 15,481 | 396 → 1,201 |
| `/todos?perPage=100` | 787 → **22,834** | 1,442 → 20,007 | 203 → 201 | 767 → 3,882 | 568 → 6,835 | 168 → 868 |
| `/dashboard` | 6,847 → **55,439** | 8,002 → 50,105 | 472 → 424 | 3,642 → 11,680 | 1,016 → 18,151 | 527 → 1,206 |
| `/todos/new` | 7,464 → **57,611** | 8,100 → 51,266 | 501 → 504 | 2,607 → 12,129 | 1,446 → 18,603 | 513 → 1,180 |

- **Inertia.Net tuned is the fastest on every cached first visit**: 2 to 3 times Nuxt tuned, 3 to 6 times TanStack Start tuned, and
  far ahead of Laravel and Next.js, although it still runs the handler and builds the page data on every request while the Nitro and
  Laravel caches answer with a stored response.
- **Next.js didn't get faster.** `'use cache'` caches the components' output, but a page that reads the query string (or calls
  `connection()`) still renders its HTML on every request. Next.js only serves stored HTML for static and ISR routes, which can't read
  the query string; the todo list needs it.
- **Laravel gained 2 to 5 times** but stops near 1,200 requests per second: a cache hit still runs the session, cookie and CSRF middleware.
- **Already-cheap routes don't gain**: the home page's JSON (a fixed list) was a little slower in some tuned builds, within the noise of
  one run.

## Go (Gin, GORM) vs Inertia.Net

On 2026-11-26 we ran the comparison again with only the Go and .NET builds (`--frameworks inertianet,inertianet-aot,golang`), split by
route. No other framework was in that run; Node is used only to build the shared React pages and, in SSR mode, to render them. The
full report, with every latency percentile and fixed-rate run, is in [results/2026-11-26-golang.md](results/2026-11-26-golang.md).

**Memory, startup and image size**

| App | Memory at rest, SPA / SSR | Cold start, SPA / SSR | Image, SPA / SSR |
|---|---:|---:|---:|
| Inertia.Net (JIT) | 27 / 60 MB | 702 / 1562 ms | 57 / 81 MB |
| Inertia.Net (Native AOT) | 17 / 46 MB | 265 / 705 ms | 16 / 40 MB |
| Go (Gin, GORM) | 4 / 34 MB | 208 / 211 ms | 16 / 41 MB |

**Requests per second** (16 connections, SSR mode; higher is better)

| Route | Response | Inertia.Net JIT | Inertia.Net AOT | Go |
|---|---|---:|---:|---:|
| Home | page | 4,075 | 4,565 | 3,674 |
| Home, data | json | 12,013 | 12,234 | 19,141 |
| Todos, 25 rows | page | 1,049 | 2,304 | 1,746 |
| Todos, 25 rows, data | json | 3,316 | 5,059 | 5,415 |
| Todos, 100 rows | page | 572 | 1,038 | 796 |
| Todos, 100 rows, data | json | 3,154 | 4,214 | 2,683 |
| Dashboard | page | 5,077 | 6,403 | 5,173 |
| Dashboard, data | json | 15,998 | 15,374 | 22,265 |
| New todo form | page | 3,488 | 4,270 | 3,803 |
| New todo form, data | json | 13,890 | 14,819 | 23,125 |
| Valid post | valid | 2,025 | 4,851 | 6,943 |
| Invalid post | invalid | 18,650 | 18,571 | 50,061 |
| Compute | page | 731 | 643 | 595 |
| Compute, data | json | 999 | 717 | 744 |
| Compute parallel | page | 945 | 754 | 739 |
| Compute parallel, data | json | 1,130 | 844 | 950 |

**Using more CPUs** (`/compute/parallel`, JSON, 16 connections)

| App | 1 CPU | 2 CPUs | 4 CPUs |
|---|---:|---:|---:|
| Inertia.Net (JIT) | 665 | 1001 | 1591 |
| Inertia.Net (Native AOT) | 483 | 717 | 1170 |
| Go (Gin, GORM) | 440 | 782 | 978 |

What stands out:

- **Go used the least memory and started fastest.** The SPA container sat at 4 MB and the SSR container, Node renderer included, at
  34 MB; Inertia.Net's AOT builds used 17 MB and 46 MB. The images were 16 MB (SPA) and 41 MB (SSR), against 16/40 MB for AOT and
  57/81 MB for JIT. Go's 208 ms SPA and 211 ms SSR cold starts were close to the AOT SPA build's 265 ms and well ahead of the JIT
  build's 702/1,562 ms.
- **Go was much faster on the form.** Invalid posts, which write nothing, reached 50,061 per second against 18,650 for JIT and 18,571
  for AOT; valid posts reached 6,943 against 2,025 and 4,851. GORM's insert is a single statement and the Go app has no CSRF token,
  while Inertia.Net validates antiforgery on every post (see [Caveats](#caveats)).
- **Go won most data-heavy JSON routes.** Home data was 1.6x both .NET builds, the cached dashboard 1.4x, the 25-row list 1.6x over
  JIT and 1.07x over AOT, and the new-todo form's JSON 1.6x. The 100-row list was the exception: 2,683 against 3,154 (JIT) and
  4,214 (AOT).
- **.NET AOT kept the lead on server-rendered first visits.** AOT was 12% to 32% faster on all five pages. Go beat the JIT build on
  both todo lists and the new-todo form, was within 2% on the dashboard, and was 10% behind on the home page.
- **CPU-heavy work is close now.** Go's `/compute` JSON reached 744 per second, 4% ahead of the AOT build and 26% behind JIT, and
  `/compute/parallel` scaled from 440 to 978 per second over 1, 2 and 4 CPUs. Before the app used `slices.SortFunc` and appended
  integers without allocating a string per id, this route was about 2x slower; that was the Go app's implementation, not a language
  limit.

The Go app has no CSRF token and does not supervise its Node renderer, so these numbers give it a little less work to do than
Inertia.Net's antiforgery and managed Node process. The .NET and Go apps otherwise run the same routes, queries, React pages and
compute algorithm, and the parity check compares their answers byte for byte.


## The Node apps in cluster mode

Node runs one request's JavaScript at a time, so a single Node server uses about one core. The usual fix in production is to run one
server process per core behind one port, as PM2's cluster mode does. The `nextjs-cluster`, `tanstack-cluster` and `nuxt-cluster`
builds do that: the plain image started through [shared/cluster.mjs](shared/cluster.mjs), which forks one worker per CPU
(`WEB_CONCURRENCY`) with `node:cluster`. Each worker gets a one-thread `/compute/parallel` pool, so the app still has one compute
thread per CPU. Nitro 3 (TanStack Start) listens with `exclusive: true`, which stops workers from sharing the port, so the wrapper
passes `exclusive: false`, as Nitro's own `node-cluster` preset does.

From one GitHub Actions run on 2026-11-17 ([results/2026-11-17-node-cluster.md](results/2026-11-17-node-cluster.md)), split by route
with Inertia.Net (JIT) and the six Node builds on each route's runner. Requests per second, plain → cluster:

| Route | Next.js | TanStack Start | Nuxt | Inertia.Net (JIT) |
|---|---:|---:|---:|---:|
| `/` page | 522 → 600 | 2,543 → 3,044 | 1,187 → 1,358 | **9,876** |
| `/todos?perPage=25` page | 357 → 395 | 1,311 → 1,538 | 734 → 874 | **1,717** |
| `/todos?perPage=100` page | 143 → 160 | 592 → **757** | 432 → 506 | 591 |
| `/dashboard` page | 478 → 503 | 3,578 → 3,407 | 1,049 → 1,212 | **6,463** |
| `/todos/new` page | 497 → 562 | 2,503 → 3,113 | 1,391 → 1,589 | **6,812** |
| `/compute` JSON | 156 → 272 | 172 → 305 | 180 → 333 | **1,029** |
| `/compute/parallel` JSON | 219 → 199 | 244 → 218 | 256 → 233 | **943** |
| `/compute` JSON, 4 CPUs | 148 → 341 | 175 → 398 | 182 → 421 | **1,537** |
| Memory at rest, SSR (MB) | 78 → 160 | 30 → 68 | 139 → 288 | 58 |

- **Compute work nearly doubles at 2 CPUs and more than doubles at 4.** At a steady 200 req/s, `/compute` JSON went from a median of
  0.6 to 1.2 s (the single process was overloaded) to 7 to 9 ms in all three apps. Inertia.Net (JIT), in one process, still served
  3 to 4 times as much.
- **Server-rendered pages gain less**: −5% to +28%. A single Node process already spends part of the second CPU on garbage
  collection and I/O, so a second worker adds less than a second core.
- **`/compute/parallel` gets 9 to 21% slower**: each worker's pool has one thread, so one request can no longer spread over the cores.
- **Memory roughly doubles**, since every worker loads the whole app, and p99 under full load rose by up to 2 times.
- The Inertia apps keep their one SSR renderer here; Inertia.Net can run more (`NodeProcess.Instances`).

## What the app does

| Route | What the server does |
|---|---|
| `/` | A small home page with three static items. |
| `/todos?perPage=25`, `/todos?perPage=100` | One Postgres query: a page of todos, newest first, through an index on `(created_at DESC, id DESC)`. |
| `/dashboard` | Per-assignee totals from a `GROUP BY` query, cached in Redis for 60 seconds. Under load this is the cache-hit path. |
| `/todos/new` | The new-todo form page. |
| `POST /todos` (`POST /api/todos` in the Node apps) | The form post: validate title, priority, assignee and due date (rules and messages in `shared/ts/rules.ts`), then insert a row into `submissions`. Measured twice: **valid** (validates and inserts) and **invalid** (fails all four rules, writes nothing). Each run reuses one session, as a user submitting the form repeatedly would. |
| `/compute?n=20000` | CPU work: 16 batches of n/16 generated records, each sorted, grouped into 64 buckets and hashed with SHA-256; the page shows the final checksum. Same algorithm in C# (`inertianet/Compute.cs`), TypeScript (`shared/ts/compute.ts`) and PHP (`laravel/app/Support/Compute.php`). |
| `/compute/parallel?n=20000` | The same work split across CPU cores inside one request: `Parallel.For` in C#, a `worker_threads` pool in Node, goroutines in Go. **PHP has no standard way to run threads inside a request**, so Laravel runs this route sequentially (same result, one core). PHP spreads work across cores by running several requests at once in separate workers instead. |

Each route is measured twice:

- **page**: a first visit, which returns HTML.
- **json**: the data a client-side navigation fetches. For the Inertia apps that is an Inertia visit (`X-Inertia: true`), which
  returns the page's props as JSON. For Next.js and TanStack Start it is an `/api/...` route (a Next.js route handler, a
  TanStack Start server route) that returns the same props.

## SSR and SPA modes

| App | SSR | SPA |
|---|---|---|
| Inertia.Net | `BENCH_SSR=1`: the library starts and supervises one Node process (`UseNodeProcess`) that renders first visits. SSR is `Required`, so a render that fails is an error rather than a silent client-rendered page. | SSR off: the HTML holds the page JSON and React renders it in the browser. |
| Laravel | `BENCH_SSR=1`: inertia-laravel's SSR server (`php artisan inertia:start-ssr`, one Node process). | SSR off (`INERTIA_SSR_ENABLED=0`). |
| Go | `BENCH_SSR=1`: gonertia posts the page JSON to the Node SSR server (`ssr/ssr.js`) that `start.sh` runs beside the Go process. | SSR off: the HTML holds the page JSON and React renders it in the browser. |
| Nuxt | The default: pages load their data with `useFetch`, which calls the `/api` routes in-process on the server. | `ssr: false` at build time: the server answers page requests with the SPA shell and the browser fetches the `/api` routes. |
| TanStack Start | The default: loaders and components run on the server. | Start's SPA mode (`spa: { enabled: true }`) plus `defaultSsr: false`. SPA mode prerenders a shell for static hosts; on the Node server it still rendered every route until SSR was turned off, so the server now answers page requests with the shell and the browser loads the data through server functions. |
| Next.js | Server components that query the database on every request. | **Next.js has no true SPA mode.** The nearest equivalent is used and labelled "SPA (nearest)": each page is a static shell (prerendered at build time) whose client component fetches its props from the `/api` route handler. |

One difference to keep in mind when reading SPA rows: an Inertia SPA first visit still runs the route's server work, because the
page's props are embedded in the HTML; a Next.js or TanStack Start SPA shell carries no data, and the browser makes a second request
(the `json` row) for it.

Both Inertia SSR bundles inline their dependencies (Vite `ssr.noExternal`), so they run without `node_modules`.

## What is measured

Each app runs in its own Docker container with the same limits (`--cpus 2 --memory 2g` by default). Postgres (2 CPUs, 1 GB) and Redis
(1 CPU, 256 MB) run in their own containers on a private Docker network. The load client is the hosting probe's `load` command
(`tests/Inertia.Net.HostingProbe`): raw keep-alive HTTP/1.1 connections, a latency histogram with 2% buckets, and fixed-rate runs
that time each request from when it was due (so a slow server cannot hide its queueing).

| Number | Meaning |
|---|---|
| Req/s | Requests per second with a fixed number of connections (16 by default), each sending its next request as soon as the last one answers. |
| p50 / p99 ms | Median and 99th-percentile latency in that run. |
| p50 / p99 at N req/s | Latency when requests arrive at a steady N per second (50 and 200 by default): what users feel below saturation. |
| CPU µs per request | CPU time the whole container used (cgroup `cpu.stat` `usage_usec`) divided by the requests served. Holds up better than req/s on a noisy machine. |
| Memory at rest / peak | The container's cgroup `memory.current` after warmup, and the highest value sampled every 100 ms under load. This counts every process in the container: the Node SSR servers and all PHP workers too. |
| Cold start | From `docker run` until `/` answers 200. |
| Image MB | The image size Docker reports. |
| overloaded | Shown instead of numbers when any request failed, or, for SSR pages, came back without the server-rendered markup. |

**Multi-core scaling**: `/compute` and `/compute/parallel` (JSON form, so no rendering) at 1, 2 and 4 CPUs, as throughput with 16
connections and as the median latency of one request at a time. The single-request latency is where `/compute/parallel` should
drop as CPUs are added; throughput is where the sequential route scales instead.

**Dev-server cold start** (`--dev`): with dependencies installed and nothing built, the time from starting the dev command
(`dotnet watch` + Vite, `next dev`, `vite dev`, `php artisan serve` + Vite) until `/` answers 200, in the same container limits.
The benchmark images reference the library's source, but the Inertia.Net dev image builds against the `Inertia.Net` 1.9.2 NuGet
package (`-p:InertiaBenchPackage=true`), as a developer's app would, so its first `dotnet watch` build does not compile the library.
Hot-reload timing in a browser is not measured (it needs a scripted browser).

## Images

Every app is built on the host (`dotnet publish`, `go build`, `npm ci && npm run build`), and its Dockerfile only copies the output onto an
Alpine runtime image, so each image holds a runtime and the built app, nothing else. Two steps run in Docker instead: the Native
AOT publish (`inertianet/aot.Dockerfile`, in `mcr.microsoft.com/dotnet/sdk:11.0.100-alpine3.24-aot`, so the binary links against
musl; run.sh exports the result to `inertianet/publish/aot`), and Laravel's `composer install` (a pinned build stage, as PHP is
not needed on the host).

| Image | Base | Node |
|---|---|---|
| Inertia.Net JIT, SSR / SPA | `mcr.microsoft.com/dotnet/aspnet:10.0.15-alpine3.24` | SSR only |
| Inertia.Net Native AOT, SSR / SPA | `mcr.microsoft.com/dotnet/runtime-deps:10.0.15-alpine3.24` | SSR only |
| Next.js, SSR / SPA | `node:24.22.3-alpine3.24` | Always (it is the server) |
| TanStack Start, SSR / SPA | `node:24.22.3-alpine3.24` | Always (it is the server) |
| Nuxt, SSR / SPA | `node:24.22.3-alpine3.24` | Always (it is the server) |
| Laravel, SSR / SPA | `dunglas/frankenphp:1.13.5-php8.5-alpine` | SSR only |
| Go, SSR / SPA | `alpine:3.24` | SSR only |

**Every image is Alpine, so every app runs on musl libc.** musl's memory allocator can be slower than glibc's for some
allocation-heavy workloads, so these numbers describe Alpine images; the same apps on Debian or Ubuntu images may measure
differently.

Node is in an image only when something runs JavaScript on the server: the Next.js and TanStack Start servers, and the SSR renderer
of the three Inertia apps (Inertia.Net's managed Node process, inertia-laravel's SSR server, and the `ssr/ssr.js` that gonertia's
Go app starts). The SPA images of the Inertia apps have no Node at all. Where Node is added to a .NET or FrankenPHP image, it is Alpine's own musl build (`apk add nodejs=24.22.3-r0`, the
same Node version as the `node` images).

The dev-server images (`Dockerfile.dev`, used by `--dev`) are different: they install the full toolchain and dependencies
themselves, on the Debian-based SDK, `node` and FrankenPHP images, because they measure a developer's machine, not a deployment.
The Go app has no dev image yet, so `--dev` skips it.

## How to run it

You need Docker, `jq`, `curl`, the .NET SDK from `global.json`, Go 1.26 (the version in `golang/go.mod`), and Node 24 with npm (the apps are built on the host). From the
repository root:

```sh
benchmarks/frameworks/run.sh --check        # parity check only: builds the apps and images, starts each app, compares the answers
benchmarks/frameworks/run.sh --smoke        # parity check + very short loads: proves everything works, the numbers mean nothing
benchmarks/frameworks/run.sh                # parity check + the full benchmark, written to artifacts/framework-bench.md
benchmarks/frameworks/run.sh --dev          # dev-server cold start (the Go app has no dev image yet)
```

Options: `--frameworks inertianet,inertianet-aot,nextjs,tanstack,nuxt,laravel,golang` (add `-tuned` or, for the Node apps, `-cluster` to an id), `--modes ssr,spa`,
`--routes home,todos25,todos100,dashboard,compute,parallel`, `--seconds 10`, `--warmup 5`, `--connections 16`, `--rates 50,200`,
`--cpus 2`, `--memory 2g`, `--scale-cpus 1,2,4`, `--port 18080`, `--out <file>`, and `--keep-services` to leave Postgres and Redis
running for the next run. Everything run.sh creates is named `inertia-bench-fw-*` and removed when it exits.

The parity check (`--check`, and the first step of every run) verifies, for every app and mode, that every route answers 200,
that SSR pages contain the server-rendered rows (25 and 100 `data-todo` items) and checksum, that SPA pages contain none, and that
every app returns byte-for-byte the same todos, dashboard totals and compute checksums.

The GitHub Actions workflow `.github/workflows/framework-bench.yml` (run it by hand from the Actions tab) runs the parity check and
the benchmark on `ubuntu-latest` and attaches the report to the run. Its `split` input runs the work in parallel: `routes` (the
default) gives each route its own runner with every app on it, so each route's numbers compare apps on one machine; `frameworks`
gives each app its own runner; `none` runs everything on one runner. `merge.sh` joins the parts into one report, which names the
CPU each part ran on.

## Caveats

- **Only numbers from a quiet machine mean anything.** The load client runs on the same machine as the apps and takes CPU too. On a
  4-CPU runner, the 4-CPU scaling step shares the machine with the client and with Postgres.
- **One Node process renders SSR** in the three Inertia apps, and Next.js and TanStack Start render in their one Node server process. Node
  renders on one thread, so SSR throughput is roughly one core's worth of rendering in every app. The `-cluster` builds run one Node
  server per CPU instead (see [The Node apps in cluster mode](#the-node-apps-in-cluster-mode)). Inertia.Net can run several SSR
  processes (`NodeProcess.Instances`); this benchmark keeps the default of one. The Go app starts its Node renderer in `start.sh` and does not
  supervise it; gonertia falls back to client rendering if it stops answering.
- **Pools**: .NET, Go and Node use a Postgres pool of at most 10 connections. Laravel runs two Octane workers per CPU, each with its own
  connection. Node's worker pool for `/compute/parallel` is sized to the CPU limit (`BENCH_WORKERS`), because Node counts the host's
  CPUs, not the container's; .NET reads the container's CPU limit itself.
- **Laravel sessions**: the other apps keep no session, so Laravel uses the `array` session driver (nothing stored between requests).
  Laravel's web middleware (cookies, CSRF token) still runs, as it would in a real app. The Go app sets a session cookie, keeps form
  errors in memory for the redirect, and has no CSRF token, as gonertia itself provides neither.
- **Next.js renders every page per request** (`await connection()`), like the other apps. Left alone, Next.js would prerender the home
  page at build time, which would measure serving a static file.
- **TanStack Start's Node deployment uses Nitro 3, which is still a release candidate** (3.0.261103-rc); it is what the TanStack Start
  hosting docs use for Node.
- **Octane restarts each worker after 500 requests** (its default, to contain memory leaks); that cost is part of Laravel's numbers.
- Postgres and Redis are shared by all apps, run one app at a time; Redis is flushed and the `submissions` table emptied before each
  app starts.
- **Sessions and forgery protection cost something on every page in the Inertia apps.** Inertia.Net issues antiforgery tokens and
  Laravel writes its encrypted session cookies on each page response, as a real app with forms does. The Go app sets a session cookie
  but has no CSRF token, and the Node apps' route handlers have no equivalent by default.
- **Next.js runs with Cache Components on** (needed for `'use cache'`). Each page sets `instant = false`, so pages are still fully
  rendered per request, as in the other apps, instead of streaming a prerendered shell first.

## Feature comparison

What each stack offers out of the box or through its official packages. "Unverified" means not checked for this README.

| Feature | Inertia.Net | Laravel + Inertia | Next.js | TanStack Start | Go + gonertia |
|---|---|---|---|---|---|
| Server language | C# | PHP | TypeScript | TypeScript | Go |
| SSR | Yes, Node or Bun process managed by the app | Yes, separate Node SSR server | Yes (React Server Components, streaming) | Yes (streaming, per-route selective SSR) | Yes, a separate Node SSR server started beside the app |
| Client-only (SPA) mode | Yes (SSR off) | Yes (SSR off) | No; static export or client components are the nearest | Yes (SPA mode) | Yes (SSR off) |
| Typed routes in the client | Yes (generated `routes.ts`) | Yes, with Laravel Wayfinder | Yes (`typedRoutes`), unverified here | Yes (core feature) | No |
| DevTools | Inertia DevTools extension | Inertia DevTools extension | Next.js dev overlay | TanStack Router Devtools | Inertia DevTools extension |
| Precognition (validate a form before submitting) | Yes | Yes (Laravel Precognition) | No built-in equivalent | No built-in equivalent | No built-in equivalent |
| Live props (props that refresh on a server event) | Yes (SignalR) | Yes (inertia-laravel `LiveProp`, transport unverified) | No built-in equivalent | No built-in equivalent | No built-in equivalent |
| Native AOT / single binary | Yes (Native AOT) | No (FrankenPHP can embed an app in a static binary; unverified with Octane) | No | No | Yes (one static Go binary) |
| Hot reload in development | Vite HMR for pages, `dotnet watch` for C# | Vite HMR for pages | Turbopack fast refresh | Vite HMR | Vite HMR for pages; Go has no built-in hot reload |

## Versions

Exact versions are pinned everywhere: npm `package.json` files with exact versions and lockfiles, `laravel/composer.lock`, and Docker
image tags (runtime: `node:24.22.3-alpine3.24`, `aspnet:10.0.15-alpine3.24`, `runtime-deps:10.0.15-alpine3.24`,
`dunglas/frankenphp:1.13.5-php8.5-alpine` with Alpine's `nodejs=24.22.3-r0`; build: `sdk:11.0.100-alpine3.24-aot`, `composer:2.11.2`;
dev: `node:24.22.3-bookworm-slim`, `sdk:11.0.100`, `aspnet:10.0.15-noble`, `dunglas/frankenphp:1.13.5-php8.5-trixie`; services:
`postgres:18.7-bookworm`, `redis:8.8.4-alpine`). The workflow builds with Node 24.22.3, the SDK from `global.json` and Go 1.26. Go uses Gin 1.12.0, GORM 1.31.2 (with the pgx 5.10.0 driver), gonertia 3.0.0 and go-redis 9.24.0. React is 19.3.1 and
`@inertiajs/react` 3.11.0 everywhere except Nuxt, which is Vue 3.5.45 with Nuxt 4.6.4 (Nitro 2.13.6, unstorage 1.17.6, ioredis 5.11.1). The Laravel image starts from the `laravel/laravel` v13.11.0 skeleton and overlays the files in
`laravel/`; the PHP extensions are pdo_pgsql, phpredis 6.3.0 and pcntl (plus zip in the Composer stage).
