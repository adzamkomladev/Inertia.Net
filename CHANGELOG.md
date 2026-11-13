# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
Release versions use `MAJOR.MINOR.PATCH`; observable behavior changes are documented with migration guidance.

## [Unreleased]

## [1.8.0] - 2026-11-13

### Added

- .NET 11 support: every library package now ships `net10.0` and `net11.0` assemblies. The repository builds with the .NET 11 SDK
  and C# 15; the public API is unchanged.
- On `net11.0`, `Inertia.Net.EntityFrameworkCore` depends on EF Core 11 and `Inertia.Net.FastEndpoints` on FastEndpoints 9.
- `dotnet new inertia --tfm net11.0` creates a .NET 11 app; the default stays `net10.0` (LTS).
- C# 15 `union` props are written as the JSON of their active case, and the generated TypeScript types a union as the union of its
  case types (`User | Team`).
- On .NET 11, async validators (`AsyncValidationAttribute`, `IAsyncValidatableObject`) report through `WithInertiaValidation()` and
  `WithInertiaPrecognition()`.

### Fixed

- Inertia.Net 1.7.1 does not work on .NET 11: `WithInertiaValidation()` and `WithInertiaPrecognition()` failed with
  `TypeLoadException` (`Microsoft.Extensions.Validation.IValidatableInfo`) because .NET 11 reshaped that experimental API. The
  `net11.0` build uses the new API.
- On .NET 11, `UseInertiaAntiforgery()` answered a form post without a valid token with a 500 (`This form is being accessed with a
  failed antiforgery validation`) instead of `419` when the framework's automatic CSRF check had judged the request cross-site. It
  now replaces that verdict with its own token check, as `app.UseAntiforgery()` does.

### Changed

- Dependency floors: EF Core `10.0.15` on `net10.0` (was `10.0.14`).
- A `net10.0` app that calls `AddValidation()` cannot be rolled forward to the .NET 11 runtime (its generated validation code uses
  types .NET 11 removed); retarget it to `net11.0`. See ".NET 10 and .NET 11" in the README.

## [1.7.1] - 2026-11-10

### Added

- `dotnet run -- --inertia-types` writes the TypeScript declarations in any environment and stops the app, exiting with code 1 when
  they cannot be written, so CI can regenerate them and fail on a stale file. The templates add it as the `types` package script.
- `InertiaOptions.Share<T>(key, Func<HttpContext, T>)`: a factory share is typed by what it returns in the generated declarations
  (`user: UserDto` instead of `unknown`). Existing lambdas such as `o.Share("now", _ => DateTime.UtcNow)` now bind to it.
- Numeric enums in the generated declarations carry their member names in a doc comment (`/** Low = 0, High = 1 */`).

### Fixed

- Stale refreshes no longer stall behind one slow loader: the background queue runs 4 refreshes at a time, cancels each after
  30 seconds (a loader that ignores the token is abandoned), and logs dropped refreshes at most every 30 seconds with a count.
- Requests waiting on a cache fill that failed, was rescued or was cancelled now load or render at once, instead of taking the fill
  one after another.
- A cache fill or stale refresh that started before an `IInertiaCache` removal on the same app instance no longer stores its old
  value after the removal.
- SSR fails over to another server only when the connection could not be made. A response that ended early (for example a render
  that crashed its server) is no longer sent to a second server.
- `Precognition-Validate-Only` matching uses one lookup per path prefix of each error, so a long header no longer costs work per
  field and error pair.
- Keyset pages read the cursor key from the loaded rows without compiling the key selectors on every request.

### Changed

- The README now opens with a link to the documentation site; it stays the complete single-file reference for coding agents.
- The TypeScript docs show that `FlashData<T>()` types the whole `page.flash` object, with a `Toast` example.
- Without `InertiaDevToolsOptions.Authorize`, the DevTools read API in Development answers only requests from this machine (a
  loopback connection to a loopback host name); set `Authorize` to read from a container or another machine.

## [1.7.0] - 2026-11-08

### Added

- Named mounts: `services.AddInertia(name, configure)` and `app.UseInertia(name)` serve a second Inertia UI, such as a library
  dashboard under `app.Map("/dashboard", ...)`, with its own shared props, asset version check, state and previous-URL cookies
  (`.Inertia.State.{name}`, `.Inertia.Previous.{name}`), root view (a keyed `IInertiaRootView`), Vite manifest, SSR gateway and
  managed Node processes, and TypeScript output. `InertiaFeature.Mount` names the request's mount. Startup fails for an
  unregistered mount or mounts that share a cookie name. Existing registrations keep configuring the default mount.
- `AddInertiaErrorPages(name, configure)` registers a mount's error components; a mount without them leaves errors to the pipeline.
- `AddInertiaSsrForMount(mount)` health check, and an `inertia.mount` tag on page, SSR and circuit metrics of named mounts.

### Changed

- Error pages render only for mounts that registered them: `AddInertiaErrorPages()` covers the default mount as before.
- Releases publish to NuGet with trusted publishing (GitHub OIDC from the `release` environment) instead of a stored API key.

## [1.6.3] - 2026-11-04

### Fixed

- Cached props retain caching and live listeners beneath dot keys, including parallel renders.
- Stale prop and SSR refreshes run on a bounded background worker so completed responses release their HTTP connection promptly.
  Background prop loaders must use singleton dependencies or create their own service scope.
- Cache tags publish after successful entry storage; refresh claims are released on rejection and shutdown.
- Validation error keys honor generated JSON aliases and indexed paths. Missing Minimal API validation registration fails when
  the endpoint is constructed. SSR exceptions expose a safe category while retaining diagnostic detail in logs.
- Dictionary property names honor converters, and numeric DOM output handles escaped and segmented values without changing spelling.
- Parallel loader failures log their property path and cancel sibling work. SSR circuit windows use elapsed time.

### Changed

- FastEndpoints setup uses `ConfigureInertia()`, preserving the existing configurator before applying Inertia setup.
- `Fresh()` implies `Once()`, `WithGroup()` implies `Defer()`, repeated `MatchOn()` calls accumulate, and explicit root
  `Append()`/`Prepend()` conflicts throw. Prop instances belong to one request.
- SignalR bounds subscriptions, channel/event names and socket IDs; invalid options fail at startup. Proxy-origin mismatches
  and DevTools recording without a reader produce diagnostics.
- Package builds track public API inventories and enable recommended analyzers without distributing analyzer dependencies.

### Added

- Public Precognition header constants, request detection and endpoint metadata for partitioned rate limiting.
- XSRF testing helpers and validation-error assertions, including named bags and indexed field paths.
- `Ssr.ConnectTimeout` and the `inertia.cache.refresh.dropped` metric for the bounded refresh queue.
- A React login validation demo with separate bounded validation/submission budgets and browser regression coverage.

### Performance

- Built-in integer dictionary keys save 9,904 B per 1,000 entries; GUID keys save 408 B per 500 entries in the paired probes.
  Safe-integer DOM output removes 15,920 B per 500 values. Scroll metadata, converter-backed members and fill waiters allocate less.
- Ordinary render allocation is unchanged. Parallel failure-path reporting adds 8 B per planned loader. Cache-hit memoization
  was rejected because it could hide an earlier loader's invalidation. See the benchmark results for timing intervals and limits.

### Documentation

- Updated the README, package guides and documentation site to describe the current APIs, with a first-ten-minutes walkthrough,
  Precognition guide, rate-limit recipe and runnable testing examples. Removed obsolete version and migration pages.
- Documented npm, Bun, pnpm and Yarn as supported package-manager choices, with equivalent install, development and build commands.

## [1.6.2] - 2026-11-04

### Added

- `Inertia.Net.EntityFrameworkCore`: ordered numbered pagination and keyset cursor pagination for EF Core queries.

- Numbered and cursor pagination values, request binding, in-memory and delegate paginators, `Scroll` metadata integration, and safe Debug logging for malformed cursors.
- Pagination testing assertions and helpers to load the next or previous scroll page.

## [1.6.1] - 2026-11-04

### Added

- Production Inertia error pages through `AddInertiaErrorPages()` and `UseInertiaErrorPages()`, with per-status components,
  shared props, optional extra props and Development opt-in. Inertia requests receive JSON pages; HTML navigations use the root
  template and SSR. API responses, existing bodies, redirects, protocol 409s and antiforgery 419s keep their existing handling.
- Error pages in the React, Vue and Svelte samples and starter templates, plus non-success response status assertions in the testing helpers.
- Opt-in `State.RememberPreviousUrl`: successful page GETs remember their relative URL in a session cookie, so `Back()` and empty-response redirects work without a `Referer`. Read the validated value with `HttpContext.Inertia().PreviousUrl`; customize the cookie with `State.PreviousUrlCookieName`.
- An old-input recipe using explicitly sanitized flash data; Inertia forms already retain their input on validation redirects.

## [1.6.0] - 2026-11-03

### Added

- Live props: props that refresh themselves when the server broadcasts an event (Inertia client 3.11.0 or later).
  - `InertiaProp.Live(LiveEvent)`, `Live(LiveChannel, params string[] events)` and `Throttle(TimeSpan)`, plus the `Live(value|loader, LiveEvent)`
    factories. `LiveChannel` (`Public`, `Private`, `Presence`, `EncryptedPrivate`) and `LiveEvent` define an event once for both sides.
  - The `liveProps` page field, after `onceProps`. Deferred, optional, already-loaded once and rescued live props keep their listeners.
  - `InertiaRequest.SocketId` (`X-Socket-Id`, `InertiaHeaders.SocketId`) and `HttpContext.InertiaRequest()`, which parses the
    request's Inertia headers.
  - `services.AddInertiaLive()` with `IInertiaLive` (publish with or without values, `exceptSocketId`; a `JsonTypeInfo<T>` overload for
    Native AOT), `IInertiaLiveBroadcaster` and `LiveMessage` for transports, and `IInertiaLiveBuilder` for transport packages.
  - Testing: `InertiaPage.LiveProps`, `AssertableInertia.HasLive(path)` and `HasLive(path, channel, event)`. DevTools lists live
    listeners per prop.
- `Inertia.Net.Live.SignalR`: the SignalR transport for live props. `AddInertiaLive().AddSignalR(o => ...)` registers the
  broadcaster and channel authorization (`ILiveChannelAuthorizer`, `o.Authorize(...)`; public channels open, the others refused unless
  allowed); `MapInertiaLiveHub()` maps the hub with an origin check, closes connections whose authentication expires, and exempts the
  hub from `UseInertiaAntiforgery()`. Native AOT compatible.
- The client transport `ClientApp/lib/inertia-signalr.ts` in the `dotnet new inertia` templates (commented out in `app.tsx`/`app.ts`;
  the templates now depend on `@microsoft/signalr`, which loads only when the transport subscribes). The templates and samples use
  `@inertiajs/*` 3.11.
- The React sample's `/live` page (a public counter and a shared private unread count) with end-to-end tests, and live props over
  SignalR in the AOT smoke test.
- Merge, deep-merge and scroll props cannot be live: `Live()` on one, or merging a live prop, throws `InvalidOperationException`.

> Deviation from inertia-laravel: a rescued live prop keeps its `liveProps` entry on partial reloads too (Laravel drops it), so it stays
> subscribed and the next event fetches it again. See `docs/protocol.md`, deviation 13.

## [1.5.3] - 2026-10-26

### Fixed

- DevTools omits malformed JSON bodies, redacts sensitive query parameters in recorded URLs and URL headers, and avoids buffering
  bodies it will not capture. Unknown-length nonseekable request bodies are now omitted as streamed; application reads are unaffected.
- Cache tag invalidation is synchronized with entry publication and replacement, so concurrent mutations cannot lose tag tracking.
- Concurrent prop and SSR cache fills share successful results even when the cache store fails or rejects the entry. Failed and
  cancelled loaders still allow waiting requests to retry.
- Cached SSR renders preserve `Timeout.InfiniteTimeSpan`; SSR path exclusions match case-insensitively, like ASP.NET Core routes.
- Generated TypeScript respects member/type number handling and uses `unknown` with a warning for custom converter output.

### Performance

- In synthetic eight-request bursts, sharing successful fills during prop-cache write failures reduces managed allocation from
  32,696 to 22,072 bytes and latency from 122.97 to 14.03 ms; SSR cache rejection drops from 128,296 to 30,776 bytes and 123.29 to 13.95 ms.
  Both fixtures simulate 10 ms I/O. Ordinary rendering shows unchanged allocation and no measurable timing improvement.
- DevTools avoids spooling 1 MiB to disk for an ignored 1 MiB request body; managed allocation falls from 37,784 to 7,680 bytes.
  See `tests/Inertia.Net.Benchmarks/RESULTS.md` for the paired measurements and limitations.

## [1.5.2] - 2026-10-24

Less allocation on every render, and no parallel planning on pages without parallel props. No public API, wire format or stored-state
format changes; upgrading needs no code change.

### Changed

- Pages without `Parallel()` props no longer run the parallel planning walk once another page in the app has used `Parallel()`. Each
  component remembers whether its renders have parallel props. A component that was first rendered without them and later gets some
  resolves them in turn for that one render; every later render starts them early.

### Performance

- Rendering allocates 416 B less per page: a JSON page with 20 mixed props drops from 2,152 B to 1,736 B, and a partial reload from 984 B
  to 568 B. The props walker and its JSON writer are pooled, and neither the `sharedProps` list nor the page `url` is allocated any more.
- Pages with parallel props allocate less: 20 parallel loaders drop from 6,496 B to 3,992 B per render. The planner skips its semaphore
  when the concurrency limit cannot be reached and no longer keeps a dictionary.
- MVC: the root view reuses one metadata provider, and the `<inertia />` body reuses one UTF-8 decoder per thread (80 B to 24 B per
  render). The SSR bundle path, the Vite tag cache key and the page-duration metric tag are no longer allocated per request.
  See `tests/Inertia.Net.Benchmarks/RESULTS.md` (2026-10-24).

### Documentation

- The README links [Inertia.Net-Samples](https://github.com/adzamkomladev/Inertia.Net-Samples) and its three apps with live demos:
  Inkwell (React), TechLandier (Vue) and CarParker (Svelte).

## [1.5.0] - 2026-10-22

Cached props and the SSR response cache can now be invalidated, replaced, served stale while they refresh, and kept in a store of the
app's choice. Nothing changes for apps that do not use the new APIs: the default store keeps props in the app's `IMemoryCache` and
renders in a private cache bounded by `Ssr.Cache.MaxBytes`, as in 1.4.

### Added

- `IInertiaCache` (registered by `AddInertia()`): `RemovePropAsync(key)`, `RemoveByTagAsync(tag)`, `ClearPropsAsync()`,
  `ClearSsrAsync()` and `SetPropAsync(key, value, duration, staleFor?, tags?)`, which caches a value as if its loader had returned it.
- `InertiaProp.Cache(key, duration, staleFor?, tags?)`. `tags` remove a prop with `RemoveByTagAsync`. `staleFor`
  (stale-while-revalidate) keeps serving a value once `duration` has passed: the first request that reads it gets it at once and the
  loader runs after that response has been sent, while the request's scoped services are still alive. One refresh runs per key and
  app instance, and one that finds a fresh value stored since skips its loader; a failed refresh is logged and keeps the stale value.
- `Ssr.Cache.StaleFor` and `InertiaResult.WithSsrCache(duration, staleFor)`: the same for server-rendered HTML, which renders again after
  the response. A stale hit is recorded as `inertia.ssr.outcomes` with reason `stale`.
- `IInertiaCacheStore`, `InertiaCacheKey`, `InertiaCacheEntryKind`, `InertiaCacheEntryOptions` and `InertiaCacheTags`: register an
  `IInertiaCacheStore` singleton to keep props and renders elsewhere, such as a cache every app instance shares. While a page renders, a
  store that throws is logged (at most once every 30 seconds) and counts as a miss.

### Performance

- Renders that do not use the new features allocate exactly what they did in 1.4.0 (JSON 2,760 B, HTML 2,824 B for the 20-prop
  page), and a cached prop hit too (1,152 B); an SSR cache hit allocates 56 B less (1,256 B). A prop with tags costs its tag array, one
  with `staleFor` 16 B more. Render times are unchanged: in alternating BenchmarkDotNet rounds every render path stayed within the
  band a benchmark without Inertia.Net code moved (up to 7.9%). See `tests/Inertia.Net.Benchmarks/RESULTS.md`.

## [1.4.0] - 2026-10-20

Adds cached props, an SSR response cache, Vite chunk prefetching, CSP nonces, generated TypeScript declarations, the Inertia DevTools
protocol, `MapInertia`, `InertiaView` for MVC and `dotnet new` templates (`Inertia.Net.Templates`). One default changes: a redirect to
another origin, answered to an Inertia request, becomes a `409` with `X-Inertia-Location`; see
[current documentation](README.md). The Inertia v3 JSON protocol and the cookie, session and TempData formats are
unchanged. Update all packages together.

### Changed

- `InertiaOptions.ConvertExternalRedirects` (default `true`): a 301, 302, 303, 307 or 308 whose `Location` has another scheme, host or
  port, answered to an Inertia request that is not a prefetch, becomes `409` with `X-Inertia-Location`, so the client makes a full visit
  instead of an XHR the browser blocks. Same-origin and relative redirects are unchanged; `false` restores 1.3.

### Added

- `InertiaProp.Cache(key, duration)`: the resolved value is kept as JSON in the app's `IMemoryCache` and reused without running the
  loader. Works with optional, deferred, merged, once and scroll props; a parallel cached prop does not start its loader on a hit.
  `AddInertia()` now calls `AddMemoryCache()`.
- `Ssr.Cache.Duration`, `Ssr.Cache.MaxBytes` and `InertiaResult.WithSsrCache(duration)`: server-rendered HTML reused by the SHA-256 of
  the page JSON, in a bounded cache of its own. Hits are recorded as `inertia.ssr.outcomes` with reason `cache`.
- `ViteOptions.Prefetch` (`None`, `Waterfall`, `Aggressive`) and `PrefetchConcurrency`: the code-split chunks the entries can import
  later are prefetched at low priority after the page loads.
- `InertiaOptions.CspNonce`: a nonce on the page script, the Vite tags, the React refresh preamble, the prefetch script and the MVC tag
  helpers. `InertiaRootViewContext.CspNonce` gives it to custom root views.
- `InertiaAntiforgeryOptions.CookieRefresh` (`Always`, `Lazy`): with `Lazy`, a page load whose token still validates keeps it, so the
  response has no `Set-Cookie` and no antiforgery `no-store`.
- `InertiaOptions.TypeScript` (`OutputPath`, `Page<T>`, `SharedProps<T>`, `FlashData<T>`) and `InertiaPropTypeAttribute`: TypeScript
  declarations for typed pages, shared props, flash data and the `InertiaConfig` augmentation, written in Development from the JSON
  contracts.
- `services.AddInertiaDevTools()` and `app.MapInertiaDevTools()`: the Inertia DevTools protocol (discovery tag, correlation headers,
  per-tab entries with redaction, `GET /_inertia/devtools/entries[/{id}]`). Records in Development only by default.
- `InertiaOptions.Pages` (`Directory`, `Extensions`): in Development, rendering a component without a page file throws and names the
  closest component.
- `app.MapInertia(pattern, component, props)`, a GET and HEAD page endpoint; `this.InertiaView(props)` and
  `InertiaMvcOptions.ComponentResolver` in `Inertia.Net.Mvc`.
- `InertiaOptions.UrlResolver` for the page `url`.
- The `inertia.props.rescued` counter.
- `Inertia.Net.Templates`: `dotnet new inertia --framework react|vue|svelte`, a Minimal API app with a typed Todos page.
- Samples: response compression, immutable caching of `build/assets`, the pages check, DevTools; the React sample also prefetches,
  generates its TypeScript declarations and maps `/` with `MapInertia`.

### Performance

- A cached prop hit skips the loader and the serialization: a 500-row loader went from 123–170 µs and 25 KB to 5–6 µs and 1.1 KB
  per render (`--cache-probe`). An SSR cache hit skips the render call: 130–221 µs to 12–22 µs with an in-memory SSR server.
- A render allocates 8 bytes more (JSON) and 24 bytes more (HTML) for the new per-response settings, and each `InertiaProp` 8 bytes
  more for its cache settings. Render times are unchanged: in five alternating BenchmarkDotNet rounds on a 4-vCPU Linux VM every
  render path was within −1.7% to +0.9% (at most 0.12 µs) of 1.3.0, below the ±4% a benchmark without Inertia.Net code moved between
  rounds. A render skips the pages-check and DevTools lookups while those features are off.
  See `tests/Inertia.Net.Benchmarks/RESULTS.md`.

### Fixed

- The FastEndpoints antiforgery conformance tests ran concurrently with the other FastEndpoints apps, which share static configuration,
  and could fail intermittently.

## [1.3.0] - 2026-10-19

Reworks server-side rendering so that a slow, failing or overloaded SSR server cannot slow down or take down the app, adds several SSR
servers and a supervised Node process pool, metrics and traces, and opt-in parallel prop loading. The SSR options change shape, so
existing SSR configuration must be updated; see [current SSR documentation](README.md#server-side-rendering). The Inertia v3 JSON protocol and
the cookie, session and TempData formats are unchanged. Update all four packages together.

### Breaking changes

| Before | After |
|---|---|
| `o.Ssr.Enabled = true` | `o.Ssr.Mode = SsrMode.Preferred` (`UseNodeProcess()` still turns SSR on) |
| `o.Ssr.ThrowOnError = true` | `o.Ssr.Mode = SsrMode.Required`, or `Render(...).WithSsr(SsrMode.Required)` for one response |
| `o.Ssr.Url = "..."` | `o.Ssr.Urls.Add("...")`; with `UseNodeProcess()`, `n.BasePort` instead (`Urls` together with `UseNodeProcess()` fails validation) |
| `o.Ssr.MaxResponseBytes = null` (unlimited) | A positive limit is required (default 16 MiB) |
| `o.Ssr.Timeout = TimeSpan.Zero` | Must be positive or `Timeout.InfiniteTimeSpan`; it now also covers the wait for a render slot |
| Unlimited concurrent renders | 8 per server in flight, 32 queued for up to 250 ms, the rest fall back (`overloaded`); `Concurrency.MaxActive = 0` removes the limit |
| Every render tries the server | After 5 consecutive server failures a server is skipped for 10 s (`circuit-open`); `CircuitBreaker.Enabled = false` turns it off |
| Any page size is sent to the server | Pages over `Ssr.MaxPageBytes` (2 MiB) render on the client (`page-too-large`) |
| `AddInertiaSsr()` calls `GET {Url}/health` | Reports the gateway's circuit and readiness state without a call; `Degraded` when some servers are paused |
| `new InertiaSsrHealthCheck(...)` | Not public; register it with `AddInertiaSsr()` |
| A managed Node server that answers beyond loopback logs a warning | It is stopped and not restarted; `NodeProcess.AllowNetworkExposure = true` keeps the warning |
| A managed Node server that hangs keeps getting renders | It is killed and restarted after 3 failed liveness checks (every 5 s) |
| SSR options changed through `IOptionsMonitor` apply | Options are read once at startup |
| The SSR HTTP client follows redirects and uses the system proxy | It does neither; a 3xx is an `http` failure |
| `"auth" = Prop(...)` with `"auth.user"` next to it runs the `auth` loader while unpacking | The `auth` loader runs only when `auth` is part of the response |
| `Ssr.ExcludePaths` entries are not checked | Entries must start with `/` |

### Added

- `SsrMode` (`Disabled`, `Preferred`, `Required`) and `InertiaResult.WithSsr(mode)` / `WithoutSsr()`. A global `Disabled` is a kill switch;
  otherwise the response's mode wins over `ExcludePaths`. `InertiaRootViewContext.SsrMode` carries it to custom root views.
- Several SSR servers in `Ssr.Urls`: each render goes to the least busy server whose circuit is closed; a refused connection is retried
  once on another server.
- A circuit breaker per server (`Ssr.CircuitBreaker`): consecutive connection, timeout, HTTP and malformed-response failures open it; one
  render probes after `BreakDuration`. A page's own render error never opens it.
- Render slots per server (`Ssr.Concurrency.MaxActive`, `MaxQueued`, `QueueTimeout`), so renders fall back instead of piling up.
- `Ssr.MaxPageBytes`.
- `NodeProcess.Instances` and `BasePort`: the app runs a pool of Node SSR processes on consecutive loopback ports, each told its port in
  `INERTIA_SSR_PORT`. `LivenessInterval` and `LivenessFailureThreshold` kill and restart an instance that stops answering.
  `AllowNetworkExposure`.
- Metrics on the `Inertia.Net` meter (`inertia.page.*`, `inertia.ssr.*`) and `inertia.render` / `inertia.ssr` activities on the
  `Inertia.Net` activity source; `InertiaDiagnostics` holds both names.
- `InertiaSsr.HttpClientName` for configuring the SSR HTTP client (e.g. adding an auth header for a remote renderer).
- Startup validation of every SSR option, naming each invalid setting.
- A startup warning for plain-`http` SSR URLs that are not loopback.
- `InertiaProp.Parallel()` and `InertiaOptions.Props.MaxConcurrency` (default 4): independent async loaders start together, bounded per
  render. Props left out of the response never start; a failure cancels the others and fails the render with the first failure in prop order.

### Fixed

- After a managed Node process exited and its port was taken by another program, the supervisor kept waiting on the exited process
  instead of backing off and restarting once the port was free.
- A managed instance is checked for network exposure before it takes renders, not after.

### Security

- A managed Node SSR server that is reachable beyond loopback is stopped by default (it serves unauthenticated `/render` and `/shutdown`).
- The SSR HTTP client no longer follows redirects (a render POST, with the page props, is never replayed to another host) and no longer
  uses a proxy.
- SSR error text in logs is cut to 2 KB, credentials in SSR URLs are left out of logs and exceptions, and repeated skips are logged at most
  once per 30 seconds per reason.

### Performance

- A failing or overloaded SSR server costs a volatile read per render while its circuit is open, instead of a timeout per page.
- The SSR render keeps its head and body in one pooled array, returned after the root view is written.
- The page and SSR instruments record only while a listener is attached.


## [1.2.0] - 2026-10-18

Adds public API (`UseInertiaAntiforgery`, `InertiaAntiforgeryOptions`, `InertiaStateOptions.CookieSecurePolicy`), so the next release is a
minor version. Public signatures, the Inertia v3 JSON protocol and the cookie, session and TempData formats are unchanged. Update all four
packages together.

### Upgrade notes

- **Turn on CSRF protection.** Replace `UseInertiaAntiforgeryCookie()` (and `UseAntiforgery()`) with
  `services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken)` and `app.UseInertiaAntiforgery()` after `app.UseInertia()`.
  Every POST, PUT, PATCH and DELETE then needs the token: exempt webhooks and token-authenticated APIs with `.DisableAntiforgery()` or
  `ShouldValidate`. Rejected requests get `419`; return `Back().WithFlash(...)` from `OnRejected` to show a message instead.
- **Root views** (`RootView`, `WithRootView(...)`) that resolve outside the content root now throw. Razor view names are unaffected.
- **Relaxed app encoder.** With a custom encoder on the HTTP JSON options, declared property names are HTML-escaped (`<`, `>`, `&`, `/`, ...)
  in page JSON. The parsed JSON is identical; only byte-exact snapshots of such names change.
- **SSR bundle.** In production a present `Ssr.BundlePath` is not checked again, so deleting it at runtime no longer turns SSR off; set
  `Ssr.Enabled = false` or restart. A missing bundle, manifest or template is re-checked once per second.
- **SSR responses** with invalid UTF-8 or an unpaired `\uD800` escape are a `response` failure (fallback or throw) instead of being decoded
  with replacement characters.
- **Releases** run in a `release` GitHub environment: add required reviewers and store `NUGET_API_KEY` as an environment secret there.

### Added

- `app.UseInertiaAntiforgery(configure?)`: issues the `XSRF-TOKEN` cookie and validates every unsafe request (JSON and form bodies),
  answering `419` (or `OnRejected`, e.g. `Back().WithFlash(...)`). Endpoints opt out with `DisableAntiforgery()`, areas with `ShouldValidate`.
  It also satisfies form-binding endpoints, so `UseAntiforgery()` is not needed. The samples use it.
- `InertiaAntiforgeryOptions.SecurePolicy` and `InertiaStateOptions.CookieSecurePolicy` for TLS-terminating proxies that do not forward the scheme.
- Managed SSR logs a warning when the Node server also answers on a non-loopback address.
- README sections: "Deploying behind a proxy", the SSR trust model, payload size guidance, and CSRF in every quick start.
- Repository: Dependabot (NuGet, npm, Actions), CodeQL, SHA-pinned actions, a read-only CI token, a `release` environment
  for publishing, and NuGet audit of transitive packages at every severity.

### Fixed

- Typed dictionaries (`Dictionary<TKey, TValue>`) whose values hold `InertiaProp` members are walked instead of throwing
  `InertiaProp values can only be serialized by Inertia.Net`; keys follow System.Text.Json (key policy, non-string keys).
- With a custom app encoder such as `UnsafeRelaxedJsonEscaping`, a declared property name containing `</script>` could close the page
  `<script>` element in the initial HTML. Property names now use the HTML-safe encoder in that case.

### Security

- The `XSRF-TOKEN` cookie is essential, so a cookie-consent policy no longer withholds it.
- `Back()` upgrades an `http` Referer to `https` on HTTPS requests.
- Root views that resolve outside the content root are refused; the template, manifest and path caches are bounded.

### Performance

- SSR responses are parsed from a pooled buffer into UTF-8 head/body bytes written as-is: about half the allocation of 1.1 and faster.
- The MVC `<inertia />`, `<vite />` and `<vite-react-refresh />` tag helpers stream pre-encoded UTF-8 (a 1 MiB page: about 3 MiB to 80 B).
- The session and TempData state stores skip loading when the request has no session or TempData cookie.
- The SSR bundle check and render URL are cached; `Location()` reads one header; typed objects no longer allocate an enumerator;
  members that differ only by number handling share serializer options; the page buffer starts at the component's recent size.
- `@vite(...)` tags are resolved before the template is written, so a manifest error is a clean exception, not half a page.
- A Vite manifest, template or SSR bundle missing at startup is re-checked once per second in production instead of never.

## [1.1.0] - 2026-10-13

See [current documentation](README.md) for observable behavior changes. This minor release includes behavior corrections that may require application changes, despite retaining public APIs and redirect-state formats. Update all four packages together.

### Changed

- Array descendant partial reloads return complete arrays; descendant exclusions omit the whole array, with `Always` precedence. Omitted/rescued slots preserve their indices.
- Managed SSR requires a dedicated loopback endpoint, rejects occupied ports, gates rendering on owned-child readiness, and stops its owned process tree without URL-based shutdown. Disposal and restart share a lifecycle gate.
- SSR responses stream through a 16 MiB default byte limit (`Ssr.MaxResponseBytes`; `null` disables), including error responses. Oversized responses use failure type `response-too-large`.
- Validation collection keys use dotted indices and FastEndpoints uses its binding serializer naming policy.

### Fixed

- Manual typed serialization preserves registered polymorphism, discriminators, default omission, type/member number handling and callbacks, with reflection and source-generated metadata.
- DOM unsafe integers honor `$bigint` preservation in props, flash and restored redirect state.
- Buffered nonempty Inertia responses retain their status, including stream and pipe writes.
- Flash and history first created during share factories or prop resolution reach the rendered page; cancellation prevents subsequent loaders.
- The configured SSR timeout is authoritative, including infinite deadlines.

### Performance

- Removed duplicate HTTP response buffering in SSR and bounded UTF-8 encoding reservations for large HTML strings.
- Restored and pending state share effective dictionaries, preserving pending precedence without repeated merge copies.
- Large pages with dozens of shared props index shared keys instead of repeatedly scanning them; smaller pages retain the allocation-light scan.
- Added payload, SSR, state-codec and separate-process Kestrel benchmarks. MemoryPack is evaluated only in the benchmark project; production state remains JSON.

## [1.0.3] - 2026-10-06

### Fixed

- Managed Node SSR health checks and shutdown tolerate socket disconnections, including unwrapped socket errors on Linux.

## [1.0.2] - 2026-10-06

### Fixed

- Managed Node SSR shutdown is safe after disposal, and repeated disposal no longer throws.

## [1.0.1] - 2026-10-06

### Fixed

- Typed page props and nested objects now honor property-level JSON converters and number handling.
- JSON extension data is included in props, preserving literal keys, null values, and partial-reload filtering.
- Explicit property ignore rules, including `JsonIgnoreCondition.Never`, take precedence over global null omission.

## [1.0.0] - 2026-10-05

First release: a .NET 10 server adapter for the Inertia.js v3 protocol (v3 only, no v2 compatibility mode).

### Added

- `Inertia.Net.Mvc`: `AddInertiaMvc(o => o.UseTempDataStateStore())` keeps the redirect state in MVC TempData (cookie or session provider), also for Minimal API endpoints.
- `o.Ssr.UseNodeProcess()`: the app starts `node {bundle}`, logs its output under `Inertia.Net.Ssr`, waits for `/health`, restarts it on exit (1 s to 30 s backoff) and stops it on shutdown (`/shutdown`, then a process-tree kill). Not started while the Vite dev server runs. The React sample uses it.

#### Packages

- `Inertia.Net`: the core adapter and Minimal API support (Native AOT and trim compatible).
- `Inertia.Net.Mvc`: MVC integration (`ModelState` errors filter, Razor root view and tag helpers). Not AOT compatible.
- `Inertia.Net.FastEndpoints`: FastEndpoints integration (`Send` extensions, validation pre-processor, Precognition).
- `Inertia.Net.Testing`: fluent assertions and `HttpClient` helpers for Inertia responses.
- `buildTransitive` props that add `global using Inertia.Net;` and `global using static Inertia.Net.Inertia;` when `ImplicitUsings` is enabled (opt out with `InertiaNetImplicitUsings=false`).

#### Protocol

- Page object with `component`, `props`, `url`, `version`, plus `sharedProps`, `mergeProps`, `prependProps`, `deepMergeProps`, `matchPropsOn`, `deferredProps`, `rescuedProps`, `scrollProps`, `onceProps`, `preserveBigIntegers`, `clearHistory`, `encryptHistory`, `flash` and `preserveFragment`, in the inertia-laravel key order and omitted when empty.
- JSON responses (`X-Inertia: true`) and the v3 initial HTML payload, `<script data-page="app" type="application/json">`, escaped so it cannot break out of the script element.
- Asset versioning with a `409` + `X-Inertia-Location`, checked before the handler runs. The version comes from `VersionResolver`, `Version`, or a hash of the Vite manifest.
- `Vary: X-Inertia` on every response, 302 to 303 for PUT/PATCH/DELETE, fragment redirects (`409` + `X-Inertia-Redirect`, except prefetch), empty-200 redirect back.
- `Location(url)`, and `Back(fallback)` with a same-origin `Referer` rule.
- Partial reloads (`only`, `except`, dot paths, `Always` props), `Optional`, lazy loaders (sync, `Task`, `ValueTask`, with or without a `CancellationToken`), run sequentially and only when included.
- Deferred props with groups, merge (append, prepend, nested paths, `matchOn`, deep merge, reset), once props (custom key, `Until`, `Fresh`, `X-Inertia-Except-Once-Props`), infinite scroll (`Scroll`, `ScrollMetadata`, `IProvidesScrollMetadata`, merge intent) and rescued props.
- Shared props (static, per request through `Func<HttpContext, object?>`, and per request through `HttpContext.Inertia().Share`).
- Typed page props: a POCO is walked through its `JsonTypeInfo`, and `InertiaProp` members are resolved by the adapter.
- `PreserveBigIntegers` (`{"$bigint":"..."}`).
- Validation errors with error bags, `X-Inertia-Error-Bag` and `WithAllErrors`; flash data; history encryption (global and per request), `ClearHistory()`, `PreserveFragment()`.
- A state store that carries flash data, errors and history flags across redirects: an encrypted cookie by default (AOT safe, warns above 4 KB), `IInertiaStateStore` for custom stores, and an opt-in session store.

#### Backends

- Minimal API: `.WithInertiaValidation()` (redirect back with errors for Inertia requests; works with .NET 10 `AddValidation()` and with returned `ValidationProblem` results) and `.WithInertiaPrecognition()`.
- MVC: global `InertiaModelStateFilter` (`AddInertiaMvc()`), `[ApiController]` support, Razor root view (`UseRazorRootView`) and the `<inertia />`, `<inertia-head />`, `<vite />`, `<vite-react-refresh />` tag helpers.
- FastEndpoints: `Send.InertiaAsync/InertiaLocationAsync/InertiaBackAsync`, `c.UseInertia()` (validation failures redirect back with errors; Precognition), and returning results from `ExecuteAsync`.
- Precognition (live validation): `204` + `Precognition-Success`, or `422` with `{ message, errors }` limited to `Precognition-Validate-Only`.
- `app.UseInertiaAntiforgeryCookie()`: the `XSRF-TOKEN` cookie for the client's `X-XSRF-TOKEN` header.

#### Rendering, assets and SSR

- Default root template (`app.html`) with `@inertia`, `@inertiaHead`, `@vite(...)`, `@viteReactRefresh` and `@viewData("key")` tokens, parsed once at startup. `IInertiaRootView` for custom views. Per-result `WithRootView`, `WithViewData` and `WithStatusCode`.
- Vite integration: manifest parsing with CSS and `modulepreload` collection, dev-server tags from the hot file or `DevServerUrl`, React Fast Refresh preamble, `PathBase` support and a CDN `AssetBaseUrl`. `ViteAssets` is public.
- Server-side rendering: production Node server (`/render`), Vite dev server (`/__inertia_ssr`), `ExcludePaths`, `BundlePath`, fallback to client rendering on failure (or `InertiaSsrException` with `ThrowOnError`), and `AddInertiaSsr()` health check.

#### Quality

- Native AOT: the core and FastEndpoints packages are AOT compatible; an AOT smoke app is published and exercised in CI.
- Benchmarks: a 20-prop JSON render takes about 14 µs and allocates about 2.7 KB; the middleware allocates nothing.
- Tests: unit tests ported from inertia-laravel 3.x, a protocol conformance suite run against Minimal API, MVC and FastEndpoints hosts, and tests for the Testing package.
- Documentation: README with a full guide and package READMEs; protocol reference in `docs/protocol.md`.

### Changed from the inertia-laravel reference

See "Deliberate deviations from Laravel" in `docs/protocol.md`:

- The version `409` check runs before the handler.
- A deferred `Scroll` prop reports its merge path (`posts.data`) like a resolved one.
- `Back()` uses the `Referer` header with a fallback instead of a stored previous URL.
- Flash data, errors and history flags use a Data Protection encrypted cookie by default, not the session.
- Validation errors survive chained redirects like flash data.
- A typed object given directly as a prop keeps partial-reload filtering for its members.
- Lazy values at top-level dot keys stay lazy; any prop can be once, rescued, merged or deferred; list headers are trimmed.
- An SSR response with an empty body falls back to client rendering; the Vite hot file is only read in Development.
