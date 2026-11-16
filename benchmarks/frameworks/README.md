# Framework comparison

The same small app, built four ways, measured the same way:

| App | Stack | Folder |
|---|---|---|
| Inertia.Net | ASP.NET Core Minimal API on .NET 10 + Inertia.Net (this repository, by project reference) + React. Run as a normal (JIT) build and as a Native AOT build. | `inertianet/` |
| Next.js | Next.js 16.4.6, App Router, `output: 'standalone'` | `nextjs/` |
| TanStack Start | TanStack Start 1.169.5 on Vite 8 with the `nitro/vite` plugin (Node server) | `tanstack/` |
| Laravel | Laravel 13 (framework 13.42.0) + inertia-laravel 3.7.0 + React, served by Laravel Octane 2.24.0 on FrankenPHP 1.13.5 (worker mode) | `laravel/` |

Every app shows the same React pages (`shared/js/*Page.jsx`, copied into each app at build time), reads the same Postgres
table with the same SQL (`shared/schema.sql`, `shared/queries.sql`), caches the dashboard in Redis for 60 seconds, and runs the
same CPU-heavy algorithm. A parity check proves they all return the same rows and checksums before anything is measured.

## What the app does

| Route | What the server does |
|---|---|
| `/` | A small home page with three static items. |
| `/todos?perPage=25`, `/todos?perPage=100` | One Postgres query: a page of todos, newest first, through an index on `(created_at DESC, id DESC)`. |
| `/dashboard` | Per-assignee totals from a `GROUP BY` query, cached in Redis for 60 seconds. Under load this is the cache-hit path. |
| `/compute?n=20000` | CPU work: 16 batches of n/16 generated records, each sorted, grouped into 64 buckets and hashed with SHA-256; the page shows the final checksum. Same algorithm in C# (`inertianet/Compute.cs`), JavaScript (`shared/js/compute.js`) and PHP (`laravel/app/Support/Compute.php`). |
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
The Inertia.Net app references the library's source, so its first `dotnet watch` build also compiles Inertia.Net and its analyzers,
which an app using the NuGet package would not. Hot-reload timing in a browser is not measured (it needs a scripted browser).

## How to run it

You need Docker, `jq`, `curl` and the .NET SDK from `global.json` (for the load client). From the repository root:

```sh
benchmarks/frameworks/run.sh --check        # parity check only: builds the images, starts each app, compares the answers
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
- Postgres and Redis are shared by all apps, run one app at a time, and Redis is flushed before each app starts.

## Feature comparison

What each stack offers out of the box or through its official packages. "Unverified" means not checked for this README.

| Feature | Inertia.Net | Laravel + Inertia | Next.js | TanStack Start |
|---|---|---|---|---|
| Server language | C# | PHP | JavaScript / TypeScript | JavaScript / TypeScript |
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
image tags (`node:24.22.3-bookworm-slim`, `mcr.microsoft.com/dotnet/sdk:11.0.100`, `aspnet:10.0.15-noble`, `runtime-deps:10.0.15-noble`,
`dunglas/frankenphp:1.13.5-php8.5-trixie`, `composer:2.11.2`, `postgres:18.7-bookworm`, `redis:8.8.4-alpine`). React is 19.3.1 and
`@inertiajs/react` 3.11.0 everywhere. The Laravel image starts from the `laravel/laravel` v13.11.0 skeleton and overlays the files in
`laravel/`; the PHP extensions are pdo_pgsql, phpredis 6.3.0, pcntl and zip.
