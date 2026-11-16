# Framework comparison

The same small app, built four ways, measured the same way:

| App | Stack | Folder |
|---|---|---|
| Inertia.Net | ASP.NET Core Minimal API on .NET 10 + Inertia.Net (this repository, by project reference) + React. Run as a normal (JIT) build and as a Native AOT build. | `inertianet/` |
| Next.js | Next.js 16.4.6, App Router, `output: 'standalone'` | `nextjs/` |
| TanStack Start | TanStack Start 1.169.5 on Vite 8 with the `nitro/vite` plugin (Node server) | `tanstack/` |
| Laravel | Laravel 13 (framework 13.42.0) + inertia-laravel 3.7.0 + React, served by Laravel Octane 2.24.0 on FrankenPHP 1.13.5 (worker mode) | `laravel/` |

Every app shows the same React pages, written in TypeScript (`shared/ts/*Page.tsx`, copied into each app at build time). Each app
reads the same Postgres tables (`shared/schema.sql`) through the ORM its community normally uses, caches the dashboard in Redis
for 60 seconds the framework's usual way, validates the same form with its usual validation library, and runs the same
CPU-heavy algorithm. A parity check proves they all return the same rows, checksums and validation messages before anything is
measured.

## How each app is written

The aim is that each app looks like what a team would write in that framework, not the fastest code each one could run.

| | Inertia.Net | Laravel | Next.js | TanStack Start |
|---|---|---|---|---|
| Data access | EF Core 10 (Npgsql provider), paging with `Inertia.Net.EntityFrameworkCore`'s `ToPageAsync` | Eloquent models | Drizzle ORM on node-postgres | Drizzle ORM on node-postgres |
| Dashboard cache (60 s, Redis) | Inertia.Net's prop cache, `InertiaProp.Cache("dashboard", ...)`, on the `Inertia.Net.Redis` store | `Cache::remember` on the Redis store | A `'use cache: remote'` function with a Redis cache handler (Cache Components) | A small cache-aside helper on node-redis (Start has no server data cache of its own) |
| Form validation | .NET 10 validation (`AddValidation()`, `IValidatableObject`) with `WithInertiaValidation()` | A Form Request | zod | zod |
| Form endpoint | `POST /todos`, Inertia redirect back with errors | `POST /todos`, Inertia redirect back with errors | `POST /api/todos` route handler: 201, or 422 with errors | `POST /api/todos` server route: 201, or 422 with errors |
| Request forgery protection | Antiforgery with the `XSRF-TOKEN` cookie (as in the Inertia.Net template) | CSRF with the `XSRF-TOKEN` cookie, cookie sessions | None by default for route handlers | None by default for server routes |

**The Native AOT build uses plain Npgsql, not EF Core.** EF Core 10's `DbContext` is not trim- or AOT-safe (its constructor warns
IL2026/IL3050), `ToPageAsync` builds its query inside the library where EF's query precompiler cannot see it, and the
precompiler's interceptors do not compile with the .NET 11 SDK this repository builds with. The csproj switches to an Npgsql data
layer (`BenchData.Npgsql.cs`, same routes and results) whenever `PublishAot=true`. This is what the Inertia.Net docs recommend for
Native AOT apps today.

The Next.js and TanStack Start forms post to a JSON route rather than a server action or server function, because a load test needs
a stable URL to post to; a route handler is a normal way to build a form API in both.

## Results

> These results are from the first version of the apps (plain SQL in every app, no form, no ORM). They will be replaced by a run
> of the current version.

From one run on a GitHub-hosted runner (4 CPUs, AMD EPYC) on 2026-11-16, each app limited to 2 CPUs and 2 GB. The full tables,
with every route, latency percentile and fixed-rate run, are in [results/2026-11-16.md](results/2026-11-16.md). No run was marked
overloaded: every request succeeded, and every SSR response was really server-rendered.

**Memory and startup**

| App | Memory at rest, SPA / SSR | Cold start, SPA / SSR | Image, SPA / SSR |
|---|---:|---:|---:|
| Inertia.Net, Native AOT | **18** / 49 MB | **221** / 496 ms | **34** / 98 MB |
| Inertia.Net, JIT | 34 / 66 MB | 433 / 1009 ms | 119 / 183 MB |
| TanStack Start | 88 / 88 MB | 295 / 275 ms | 167 MB |
| Next.js | 115 / 117 MB | 524 / 638 ms | 182 MB |
| Laravel (Octane, FrankenPHP) | 101 / 156 MB | 775 / 888 ms | 252 / 311 MB |

Memory is the whole container: for SSR that includes the Node renderer, for Laravel all its PHP workers.

**Requests per second** (16 connections; higher is better)

| Route | Inertia.Net JIT | Inertia.Net AOT | TanStack Start | Next.js | Laravel |
|---|---:|---:|---:|---:|---:|
| `/todos?perPage=25`, first visit with SSR | **5,023** | 4,817 | 1,897 | 592 | 534 |
| `/todos?perPage=25`, client-side navigation (JSON) | **12,603** | 11,901 | 6,261 | 2,624 | 1,102 |
| `/dashboard` (Redis), client-side navigation | 26,383 | **26,670** | 13,009 | 3,297 | 1,410 |
| `/compute?n=20000` (CPU-heavy, JSON) | **1,551** | 1,103 | 288 | 240 | 86 |

**Using more CPUs** (`/compute/parallel`, JSON, 16 connections)

| App | 1 CPU | 2 CPUs | 4 CPUs |
|---|---:|---:|---:|
| Inertia.Net, JIT | 909 | 1,534 | 2,702 |
| Inertia.Net, Native AOT | 714 | 1,069 | 1,824 |
| TanStack Start | 215 | 398 | 514 |
| Next.js | 182 | 350 | 444 |
| Laravel (sequential in PHP) | 61 | 84 | 161 |

What stands out:

- **Inertia.Net uses the least memory and serves the most requests in this test.** The Native AOT SPA build sits at 18 MB, about
  a fifth of the smallest Node app. With SSR, Inertia.Net served about 2.6 times as many first visits as TanStack Start and about
  8 to 9 times as many as Next.js and Laravel.
- **Client-side navigation is cheap in every Inertia.Net mode**, because an Inertia visit returns only the page's JSON and the SSR
  renderer is not involved.
- **CPU-heavy work is mostly a language-speed test.** .NET runs the same algorithm several times faster than Node and much faster
  than PHP. Node handles one request's JavaScript at a time per process, so the plain `/compute` route stays near 240 req/s at any
  CPU count. Only the worker-thread route (`/compute/parallel`) uses extra cores. .NET spreads both routes over all cores with no
  extra code.
- **Native AOT is about 30% slower than JIT on the CPU-heavy routes.** The JIT build optimises hot code at run time using what
  it observes (dynamic PGO), and AOT can't. AOT wins on memory, startup and image size, and is level with JIT on ordinary pages.
- **Laravel can't keep up with 200 req/s on `/compute`** (it peaks near 90 on 2 CPUs), so those requests queue for 3 to 6 seconds.
  It scales by running more PHP workers, which shows from 2 to 4 CPUs.
- **Next.js "SPA (nearest)" pages are a static shell.** The page then fetches its data from an API route. Its `page` rows measure
  serving that shell, so compare its `json` rows instead.

Read these numbers as one run on one shared cloud machine, using Alpine images (see [Caveats](#caveats)). The load client ran on
the same machine. Image sizes are what that runner's Docker reported, and can differ from `docker images` on another machine.

## What the app does

| Route | What the server does |
|---|---|
| `/` | A small home page with three static items. |
| `/todos?perPage=25`, `/todos?perPage=100` | One Postgres query: a page of todos, newest first, through an index on `(created_at DESC, id DESC)`. |
| `/dashboard` | Per-assignee totals from a `GROUP BY` query, cached in Redis for 60 seconds. Under load this is the cache-hit path. |
| `/todos/new` | The new-todo form page. |
| `POST /todos` (`POST /api/todos` in the Node apps) | The form post: validate title, priority, assignee and due date (rules and messages in `shared/ts/rules.ts`), then insert a row into `submissions`. Measured twice: **valid** (validates and inserts) and **invalid** (fails all four rules, writes nothing). Each run reuses one session, as a user submitting the form repeatedly would. |
| `/compute?n=20000` | CPU work: 16 batches of n/16 generated records, each sorted, grouped into 64 buckets and hashed with SHA-256; the page shows the final checksum. Same algorithm in C# (`inertianet/Compute.cs`), TypeScript (`shared/ts/compute.ts`) and PHP (`laravel/app/Support/Compute.php`). |
| `/compute/parallel?n=20000` | The same work split across CPU cores inside one request: `Parallel.For` in C#, a `worker_threads` pool in Node. **PHP has no standard way to run threads inside a request**, so Laravel runs this route sequentially (same result, one core). PHP spreads work across cores by running several requests at once in separate workers instead. |

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

Every app is built on the host (`dotnet publish`, `npm ci && npm run build`), and its Dockerfile only copies the output onto an
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
| Laravel, SSR / SPA | `dunglas/frankenphp:1.13.5-php8.5-alpine` | SSR only |

**Every image is Alpine, so every app runs on musl libc.** musl's memory allocator can be slower than glibc's for some
allocation-heavy workloads, so these numbers describe Alpine images; the same apps on Debian or Ubuntu images may measure
differently.

Node is in an image only when something runs JavaScript on the server: the Next.js and TanStack Start servers, and the SSR renderer
of the two Inertia apps (Inertia.Net's managed Node process, inertia-laravel's SSR server). The SPA images of the Inertia apps have
no Node at all. Where Node is added to a .NET or FrankenPHP image, it is Alpine's own musl build (`apk add nodejs=24.22.3-r0`, the
same Node version as the `node` images).

The dev-server images (`Dockerfile.dev`, used by `--dev`) are different: they install the full toolchain and dependencies
themselves, on the Debian-based SDK, `node` and FrankenPHP images, because they measure a developer's machine, not a deployment.

## How to run it

You need Docker, `jq`, `curl`, the .NET SDK from `global.json`, and Node 24 with npm (the apps are built on the host). From the
repository root:

```sh
benchmarks/frameworks/run.sh --check        # parity check only: builds the apps and images, starts each app, compares the answers
benchmarks/frameworks/run.sh --smoke        # parity check + very short loads: proves everything works, the numbers mean nothing
benchmarks/frameworks/run.sh                # parity check + the full benchmark, written to artifacts/framework-bench.md
benchmarks/frameworks/run.sh --dev          # dev-server cold start
```

Options: `--frameworks inertianet,inertianet-aot,nextjs,tanstack,laravel`, `--modes ssr,spa`,
`--routes home,todos25,todos100,dashboard,compute,parallel`, `--seconds 10`, `--warmup 5`, `--connections 16`, `--rates 50,200`,
`--cpus 2`, `--memory 2g`, `--scale-cpus 1,2,4`, `--port 18080`, `--out <file>`, and `--keep-services` to leave Postgres and Redis
running for the next run. Everything run.sh creates is named `inertia-bench-fw-*` and removed when it exits.

The parity check (`--check`, and the first step of every run) verifies, for every app and mode, that every route answers 200,
that SSR pages contain the server-rendered rows (25 and 100 `data-todo` items) and checksum, that SPA pages contain none, and that
every app returns byte-for-byte the same todos, dashboard totals and compute checksums.

The GitHub Actions workflow `.github/workflows/framework-bench.yml` (run it by hand from the Actions tab) runs the parity check and
the benchmark on `ubuntu-latest` and attaches the report to the run.

## Caveats

- **Only numbers from a quiet machine mean anything.** The load client runs on the same machine as the apps and takes CPU too. On a
  4-CPU runner, the 4-CPU scaling step shares the machine with the client and with Postgres.
- **One Node process renders SSR** in both Inertia apps, and Next.js and TanStack Start render in their one Node server process. Node
  renders on one thread, so SSR throughput is roughly one core's worth of rendering in every app. Inertia.Net can run several SSR
  processes (`NodeProcess.Instances`); this benchmark keeps the default of one.
- **Pools**: .NET and Node use a Postgres pool of at most 10 connections. Laravel runs two Octane workers per CPU, each with its own
  connection. Node's worker pool for `/compute/parallel` is sized to the CPU limit (`BENCH_WORKERS`), because Node counts the host's
  CPUs, not the container's; .NET reads the container's CPU limit itself.
- **Laravel sessions**: the other apps keep no session, so Laravel uses the `array` session driver (nothing stored between requests).
  Laravel's web middleware (cookies, CSRF token) still runs, as it would in a real app.
- **Next.js renders every page per request** (`await connection()`), like the other apps. Left alone, Next.js would prerender the home
  page at build time, which would measure serving a static file.
- **TanStack Start's Node deployment uses Nitro 3, which is still a release candidate** (3.0.261103-rc); it is what the TanStack Start
  hosting docs use for Node.
- **Octane restarts each worker after 500 requests** (its default, to contain memory leaks); that cost is part of Laravel's numbers.
- Postgres and Redis are shared by all apps, run one app at a time; Redis is flushed and the `submissions` table emptied before each
  app starts.
- **Sessions and forgery protection cost something on every page in the Inertia apps.** Inertia.Net issues antiforgery tokens and
  Laravel writes its encrypted session cookies on each page response, as a real app with forms does. The Node apps' route handlers
  have no equivalent by default.
- **Next.js runs with Cache Components on** (needed for `'use cache'`). Each page sets `instant = false`, so pages are still fully
  rendered per request, as in the other apps, instead of streaming a prerendered shell first.

## Feature comparison

What each stack offers out of the box or through its official packages. "Unverified" means not checked for this README.

| Feature | Inertia.Net | Laravel + Inertia | Next.js | TanStack Start |
|---|---|---|---|---|
| Server language | C# | PHP | TypeScript | TypeScript |
| SSR | Yes, Node or Bun process managed by the app | Yes, separate Node SSR server | Yes (React Server Components, streaming) | Yes (streaming, per-route selective SSR) |
| Client-only (SPA) mode | Yes (SSR off) | Yes (SSR off) | No; static export or client components are the nearest | Yes (SPA mode) |
| Typed routes in the client | Yes (generated `routes.ts`) | Yes, with Laravel Wayfinder | Yes (`typedRoutes`), unverified here | Yes (core feature) |
| DevTools | Inertia DevTools extension | Inertia DevTools extension | Next.js dev overlay | TanStack Router Devtools |
| Precognition (validate a form before submitting) | Yes | Yes (Laravel Precognition) | No built-in equivalent | No built-in equivalent |
| Live props (props that refresh on a server event) | Yes (SignalR) | Yes (inertia-laravel `LiveProp`, transport unverified) | No built-in equivalent | No built-in equivalent |
| Native AOT / single binary | Yes (Native AOT) | No (FrankenPHP can embed an app in a static binary; unverified with Octane) | No | No |
| Hot reload in development | Vite HMR for pages, `dotnet watch` for C# | Vite HMR for pages | Turbopack fast refresh | Vite HMR |

## Versions

Exact versions are pinned everywhere: npm `package.json` files with exact versions and lockfiles, `laravel/composer.lock`, and Docker
image tags (runtime: `node:24.22.3-alpine3.24`, `aspnet:10.0.15-alpine3.24`, `runtime-deps:10.0.15-alpine3.24`,
`dunglas/frankenphp:1.13.5-php8.5-alpine` with Alpine's `nodejs=24.22.3-r0`; build: `sdk:11.0.100-alpine3.24-aot`, `composer:2.11.2`;
dev: `node:24.22.3-bookworm-slim`, `sdk:11.0.100`, `aspnet:10.0.15-noble`, `dunglas/frankenphp:1.13.5-php8.5-trixie`; services:
`postgres:18.7-bookworm`, `redis:8.8.4-alpine`). The workflow builds with Node 24.22.3 and the SDK from `global.json`. React is 19.3.1 and
`@inertiajs/react` 3.11.0 everywhere. The Laravel image starts from the `laravel/laravel` v13.11.0 skeleton and overlays the files in
`laravel/`; the PHP extensions are pdo_pgsql, phpredis 6.3.0 and pcntl (plus zip in the Composer stage).
