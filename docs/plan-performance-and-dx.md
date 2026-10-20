# Performance and developer-experience plan (research, October 2026)

Status: phases 1 to 4 are implemented in 1.4.0 (see the [changelog](../CHANGELOG.md) and [upgrade notes](migrating-to-1.4.md)); early
hints (3.7) and the "Later" items remain open. Where the shipped design differs from this plan:

- **TypeScript (4.3)** does not go through OpenAPI. `o.TypeScript` writes the declarations directly from the app's JSON contracts at
  startup in Development, which needs no npm generator or mapping script and also types the shared props and `InertiaConfig`.
  `[InertiaPropType(typeof(T))]` types `InertiaProp` members.
- **Lazy XSRF refresh (3.1)** is opt-in (`CookieRefresh = Lazy`), not the default: antiforgery tokens are bound to the signed-in user,
  so a kept token must be validated, which costs about what issuing one does. The gain is cacheable responses, not CPU.
- **Templates (4.6)** are one `dotnet new inertia` template with `--framework react|vue|svelte` on a Minimal API backend, rather than
  copies of the three samples.
- **SSR cache (3.3)** uses a private, size-bounded `MemoryCache` (`Ssr.Cache.MaxBytes`), so per-user pages cannot grow it unbounded.

Each item below names the files it would touch, its size (S: hours, M: a day or two, L: a week or more) and how it is verified. Sources
are at the end.

## 1. Scope and method

Three questions were researched:

1. What do the other Inertia server adapters have that Inertia.Net 1.3 does not, and which of those are worth having?
2. Which of those are performance features and which are developer-experience features?
3. Which .NET 10 and .NET 11 platform improvements does the adapter not yet use?

Adapters reviewed: inertia-laravel 3.x (reference implementation), inertia-rails 3.x, inertia-phoenix 3.x, inertia-django,
@adonisjs/inertia 5, gonertia (Go), InertiaCore.AspNetCore 0.3 (the other active .NET adapter), plus the v3 docs for DevTools, SSR,
TypeScript and testing. Platform material: the ASP.NET Core 10 and 11 release notes, the .NET 10 library notes and the .NET 10
performance post.

### Where Inertia.Net already leads

No other adapter has all of: per-server circuit breakers and render slots, several SSR servers with a supervised process pool, metrics and
traces, opt-in parallel loaders, Native AOT, a single-pass pooled JSON writer, byte-level benchmarks, and built-in CSRF with `419`.
Parity items that are done and need no work: once, scroll, rescue, deep merge, `matchOn`, big integers, fragment redirects, Vite dev-mode
SSR through `@inertiajs/vite`, the SSR health check (Laravel's `inertia:check-ssr`), `Instances` (Laravel's `cluster`), the named SSR
`HttpClient` (Laravel's `configureSsrRequestUsing`), and Laravel-style test assertions including `ReloadOnly` and `LoadDeferredProps`.

## 2. Gap analysis

### 2.1 Features other adapters have

| Feature | Who has it | Kind | Fit for Inertia.Net |
|---|---|---|---|
| DevTools protocol (browser extension support) | Laravel 3.2+, Rails 3.23 | DX | High. The protocol is documented and backend-agnostic. |
| Cached props: a loader's JSON kept in a server cache under a key | Rails 3.21 | Perf | High. Fits the writer: cached bytes go through `WriteRawValue`. |
| SSR response cache keyed by the page JSON | Rails 3.19 | Perf | High for anonymous pages. |
| Prefetch of code-split chunks after load (waterfall or aggressive) | Laravel 11.21 `Vite::usePrefetchStrategy`, gonertia | Perf | Medium. We emit `modulepreload` for static imports only; page chunks are dynamic imports and are not prefetched. |
| CSP nonce on emitted script and link tags | Phoenix, gonertia, InertiaCore.AspNetCore | DX, security | High. Blocks any strict CSP today. Also a prerequisite for the prefetch script. |
| Subresource integrity from the manifest | gonertia, InertiaCore.AspNetCore | Security | Low. Needs a Vite plugin to write hashes; add when the nonce work touches the tag renderer. |
| Lazy XSRF cookie refresh on GET | Rails 3.22 | Perf, caching | High and cheap. We call `GetAndStoreTokens` and `Set-Cookie` on every GET. |
| Convert cross-origin redirects to `409 X-Inertia-Location` | Rails 3.23 | DX | Medium, cheap. `Results.Redirect("https://other")` would just work. |
| Route shorthand that renders a component with static props | Laravel `Route::inertia`, Rails `inertia 'Comp'` | DX | High, trivial: `app.MapInertia("/about", "About")`. |
| Default component name from controller and action | Rails `default_render`, Adonis | DX | Medium, MVC only. |
| Middleware hooks: `onVersionChange`, `onEmptyResponse`, `urlResolver` | Laravel | DX | Low-medium. Options with delegates. |
| Pages directory check: fail fast when a component file is missing | Laravel `pages.paths`, InertiaCore `ValidatePages` | DX | High, cheap in Development. |
| TypeScript types for page props and shared props | Wayfinder (Laravel), Typelizer (Rails), Adonis codegen, InertiaCore.TypeGen | DX | High. See 4.3 for the approach that adds no new .NET tooling. |
| Typed route helper for the client | Wayfinder, Adonis, InertiaCore `inertiatsgen routes` | DX | Medium. |
| Project templates and installers | Laravel starter kits, Phoenix igniter, Rails generators, InertiaNet.Templates | DX | High. We have three samples and no `dotnet new` template. |
| Previous-URL storage so `Back()` works without a Referer | Laravel 3.4 | DX | Low. Costs a state write per GET; our Referer fallback covers it. |
| Telemetry event when a deferred prop is rescued | Phoenix | Ops | Low, S. A counter on the existing meter. |
| Prop key transform (camelize) | Phoenix, Rails | DX | Not needed: the HTTP JSON naming policy does this. |
| Bun or Deno as the SSR runtime | Rails, Laravel | DX | Already possible through `NodeProcess.Executable`; document only. |

### 2.2 .NET platform improvements not yet used

Automatic with .NET 10 (no code change, already in effect): faster `Dictionary` lookups with constant string keys, stack-allocated
non-escaping delegates and arrays, inlining of methods with `try/finally`, the `JsonSerializer` sync buffer cache, `Task.WhenAll`
fast paths, Kestrel memory-pool eviction, HTTP/2 HPACK memory, zlib-ng compression.

| Platform item | Status | Use for Inertia.Net |
|---|---|---|
| Response compression middleware (Brotli, gzip; Zstandard in .NET 11) | Not in samples or docs | Page JSON compresses 5 to 10 times. Document, enable in samples. |
| `MapStaticAssets` (build-time Brotli and immutable caching of `wwwroot`) | Samples use `UseStaticFiles` | Hashed Vite chunks should be `Cache-Control: immutable`. |
| `IMemoryCache` (in the shared framework) | Unused | Backing store for cached props and the SSR cache. No new dependency. |
| 103 Early Hints | Kestrel has no API (dotnet/aspnetcore#27851, now in the .NET 12 planning milestone) | Blocked. Would let the browser fetch Vite assets while loaders run. Revisit when Kestrel ships it. |
| `IOutputCachePolicyProvider` (.NET 11) | n/a | A documented output-cache policy for anonymous pages that varies on the Inertia headers. |
| Runtime-async (.NET 11) | Preview | Evaluate when adding a `net11.0` target after GA in November 2026. |
| Native OpenTelemetry attributes on the request activity (.NET 11) | n/a | Our `inertia.render` activity already nests under it; nothing to change. |
| `FrozenDictionary`, `AlternateLookup<ReadOnlySpan<char>>` | Unused | Manifest chunks and the shared-prop index are candidates; measure before changing (the tags are already pre-rendered per entry set). |
| `JsonElement.Parse` instead of `JsonDocument.Parse` | One use in `InertiaStateStore` (disposed) | Fine as is. |
| HTTP/3 trimming feature switch for AOT (`System.Net.SocketsHttpHandler.Http3Support=false`) | Unused | Smaller AOT binary; one line in the AOT docs and smoke app. |

### 2.3 Decided against

- In-process JavaScript SSR (Jint, ClearScript V8, SSR.Net). Jint cannot run React or Vue SSR at a useful speed; V8 embedding adds a
  native dependency and breaks the AOT and trimming story. The Node pool with slots and breakers is the right design, and dev-mode SSR
  already goes through Vite.
- Streaming the page JSON straight to the response. The buffer is what guarantees a failing loader never produces a half-written 200
  and gives `Content-Length`. The copy into Kestrel's pipe is a memcpy.
- `HybridCache` as a dependency. `IMemoryCache` is in the shared framework and is enough; an `IDistributedCache` option can come later.
- A custom prop-key transformer. The HTTP JSON naming policy already does it.

## 3. Performance work

### 3.1 Lazy XSRF cookie refresh (S)

`InertiaAntiforgery.IssueToken` calls `GetAndStoreTokens` (a Data Protection encrypt) and appends `Set-Cookie` on every GET and HEAD.
Add `InertiaAntiforgeryOptions.CookieRefresh` with `Always` (current) and `Lazy` (default: skip when the request already carries a valid
`XSRF-TOKEN` cookie that matches the antiforgery cookie). Rails made `:lazy` an option for exactly this: fewer bytes per response and
responses that shared caches can store.

Files: `InertiaAntiforgery.cs`. Verify: integration test that a second GET has no `Set-Cookie`, and that a POST with the old token still
validates. Benchmark: `--render-probe` is unaffected; add a middleware probe row.

### 3.2 Cached props (M)

```csharp
["stats"] = Cache("dashboard-stats", ct => db.StatsAsync(ct)).For(TimeSpan.FromMinutes(5)),
["feed"]  = Defer(ct => db.FeedAsync(ct)).Cache(["feed", userId], TimeSpan.FromMinutes(1)),
```

The loader's result is serialized once with the member's `JsonTypeInfo` into a `byte[]` and stored in `IMemoryCache` under
`"Inertia.Net/props/" + key`; a hit writes the bytes with `Utf8JsonWriter.WriteRawValue` and skips the loader. Keys are strings or arrays
of parts (joined), so per-user data is the caller's responsibility, as in Rails. Composable with `Defer`, `Optional`, `Merge` and `Once`;
not with `Always` loaders that return `InertiaProp` values (the cached bytes are a plain value). `Cache` on a prop whose type can hold
`InertiaProp` values throws at render time with a clear message.

Files: `InertiaProp.cs` (a `CacheKey`, `CacheTtl` pair), `Inertia.cs` (factories), `PropsWriter.cs` (one branch before `resolveValue`),
`InertiaServiceCollectionExtensions.cs` (`TryAdd` of `IMemoryCache` through `AddMemoryCache()`). Protocol: no wire change. Verify: unit tests for hit, miss, expiry (with `FakeTimeProvider`), partial reload, and the `InertiaProp`-typed rejection. AOT:
`AddMemoryCache` is AOT safe; the smoke app gets one cached prop.

### 3.3 SSR response cache (M)

`Render(...).WithSsrCache(TimeSpan)` and `Ssr.Cache.DefaultTtl` (default off). Key: SHA-256 of the page JSON bytes (already in a buffer;
SHA-256 is in-box and runs above 1 GB/s, so a 100 KiB page costs about 0.1 ms, far below a render). Value: the `SsrRender` head and body
arrays. Disabled while the Vite dev server runs. Hits are recorded as `inertia.ssr.outcomes{outcome=rendered, reason=cache}` and bypass the
slots and breaker. The cache is per process; a distributed variant is a later `IInertiaSsrCache` if asked for.

Files: `SsrGateway.cs` (lookup before the slot, store after a successful render), `InertiaResult.cs`, `InertiaOptions.cs`,
`InertiaMetrics.cs`. Verify: gateway tests with a counting fake server; `--ssr-probe` row for a warm hit.

### 3.4 Vite chunk prefetch (M)

Add `o.Vite.Prefetch` with `None` (default), `Waterfall(concurrency = 3)` and `Aggressive`, as Laravel does. The tag renderer already walks
the manifest closure; it also knows each chunk's `dynamicImports`. After the entry tags, emit one inline script that, on `load`, appends
`<link rel="prefetch" fetchpriority="low">` for every chunk reachable through dynamic imports (the page components), either all at once or
with a bounded concurrent count. The script is pre-rendered per entry set like the tags, so the per-request cost stays zero. The script
carries the CSP nonce from 4.1 when one is configured.

Files: `ViteAssets.cs`, `InertiaOptions.cs`. Verify: unit test on the rendered HTML; a Playwright assertion in the React sample that a
page chunk is in the browser cache before navigation.

### 3.5 Compression and static asset caching in the samples and docs (S)

- Samples call `AddResponseCompression()` with Brotli and gzip for `application/json` and `text/html`; the README's deployment section
  explains it and notes Zstandard in .NET 11. The compression middleware removes our `Content-Length`; `Vary: Accept-Encoding` is added by
  it (always, from .NET 11).
- Serve `wwwroot/build/assets/*` with `Cache-Control: public, max-age=31536000, immutable` (file names are content-hashed). Either
  `MapStaticAssets()` when the frontend is built before `dotnet publish` (which also precompresses with Brotli at publish), or
  `UseStaticFiles` with `OnPrepareResponse`. Document both; switch the samples to the cheaper `OnPrepareResponse` form since the
  frontend build order varies.

Files: `samples/*/Program.cs`, `README.md`. Verify: e2e response-header assertions.

### 3.6 Small items (S each)

- A `rescued` counter: `inertia.props.rescued{group}` on the existing meter, recorded where `rescuedProps` is collected.
- AOT docs and smoke app: the HTTP/3 trimming switch.
- `InertiaMiddleware`: the 409 version path calls `GetEncodedUrl()`; build it from the request parts into a pooled builder only if a
  benchmark shows it (the path runs once per deploy per client, so probably skip).

### 3.7 Blocked: early hints

Our page is fully buffered before the first byte, so the browser cannot start fetching Vite assets until every loader has finished.
A `103 Early Hints` response with `Link: </build/assets/app.js>; rel=modulepreload` would remove that wait without giving up the
"never a half-written 200" guarantee, which is why streaming the head early is not an option. Kestrel has no API for 1xx responses
(dotnet/aspnetcore#27851, open since 2020, now in the .NET 12 planning milestone; the workaround in that issue writes to the transport
directly and does not work on IIS or HTTP.sys). Track the issue; implement `o.EarlyHints = true` when it ships.

## 4. Developer-experience work

### 4.1 CSP nonce (S)

`o.Csp.Nonce = ctx => ...` (default null). When set, the `<script data-page>` element, the Vite `<script type="module">` and
`<link rel="modulepreload">` tags, the dev-server tags and the prefetch script carry `nonce="..."`. The root template is already split into
pre-encoded segments; the nonce is the one per-request piece written between them, so the cached tag bytes split at the attribute.
MVC tag helpers take `nonce` too. Phoenix reads it from a conn assign; the delegate form covers `HttpContext.Items` and
`IHttpContextAccessor` users alike.

Files: `RootTemplateView.cs`, `ViteAssets.cs`, `TagHelpers.cs`, `InertiaOptions.cs`. Verify: unit tests on the rendered HTML with and
without a nonce; a sample with a strict CSP header in the e2e matrix.

### 4.2 DevTools protocol (L)

Implement the documented protocol so the Inertia DevTools extension (Chrome and Firefox) works against .NET apps:

- `AddInertiaDevTools()` registers a recorder that is on only in Development unless `Enabled` is set, plus `Gate: Func<HttpContext, bool>`
  for other environments.
- Per request: build an entry (`__meta`, `http`, `props`, `propValues`, `route`, `renderSource`, `componentPath`), store it in a per-tab
  ring buffer (cap per `X-Inertia-Devtools-Tab`, TTL prune), set `X-Inertia-Devtools-Id` and `X-Inertia-Devtools-Parent-Out`, and inject
  `<script data-inertia-devtools-id type="application/json">` into the initial HTML (the template already has a hook point next to
  `@inertia`).
- Endpoints `GET /_inertia/devtools/entries/{id}` and `GET /_inertia/devtools/entries`, mapped by `app.MapInertiaDevTools()`, excluded
  from recording.
- Props metadata comes from the writer, which already collects every prop's kind, group and path; `propValues` is the page JSON written
  with `WriteRawValue`. Redaction of `password`, `token`, `secret`, `authorization`, `cookie` and the `XSRF` headers before storage.
  `route` uses the endpoint display name and route pattern; `renderSource` and `componentPath` are `null` (no editor links), with
  `o.Pages` from 4.4 providing `componentPath` when configured.
- Request type derivation follows the protocol table (precognition, initial, http, deferred, poll, partial, prefetch, navigate).

Ship it in the core package behind the opt-in call so it stays one install; the recorder, buffer and endpoints are trimmed when unused.
A source-generated `JsonSerializerContext` keeps it AOT clean. Verify: unit tests for entry shape and type derivation against the
protocol, an integration test that drives the two endpoints, and a manual check with the extension on the React sample.

### 4.3 TypeScript types for pages and shared props (M, no new .NET tool)

Climbing the ladder: Laravel, Rails and InertiaCore each ship a bespoke generator. ASP.NET Core 10 already generates OpenAPI at build
time, so the smallest path is:

1. `Render<TProps>(component, TProps props)` (and the typed-object overload we have) adds `ProducesResponseType<TProps>` metadata through
   `IEndpointMetadataProvider`, plus an operation extension `x-inertia-component: "Users/Index"`. Shared props declared through a typed
   `o.Share<TShared>()` overload register a `SharedProps` schema with `x-inertia-shared: true`.
2. A documented `npm` script (`openapi-typescript` or `@hey-api/openapi-ts`, both already common in Vite projects) turns the OpenAPI
   document into `resources/js/types/inertia.d.ts`: one interface per component and the `InertiaConfig` augmentation
   (`sharedPageProps`, `flashDataType`, `errorValueType`). The mapping script is about 40 lines and lives in the samples.
3. Later, if asked for: a `dotnet inertia-net types` tool that reads the OpenAPI document and writes the file without npm.

Files: `InertiaResult.cs`, `Inertia.cs`, `InertiaOptions.cs`, one script and a `types` entry in each sample. Verify: a test that the
OpenAPI document carries the extension and schema; a sample `tsc --noEmit` step in CI.

### 4.4 Pages directory check and component constants (S then M)

- S: `o.Pages.Directory = "resources/js/Pages"` and `o.Pages.Extensions = [".tsx", ".vue", ".svelte"]` (Laravel's `pages.paths` and
  `pages.extensions`). In Development, `Render` of a component without a matching file throws
  `Inertia page 'Users/Indx' was not found in resources/js/Pages (did you mean Users/Index?)`. One `FileCache` lookup; off in production.
- M: a Roslyn source generator package `Inertia.Net.Generators` that reads the same directory (as `AdditionalFiles`) and emits a static
  `Pages` class (`Pages.Users.Index == "Users/Index"`) so component names are compile-time checked, as Adonis's `InertiaPages` interface
  does. Optional; the runtime check covers most of the value.

### 4.5 Small DX additions (S each)

- `app.MapInertia("/about", "About", props?)`: a GET and HEAD endpoint returning `Render`. MVC: `Render()` with no component resolves
  `{Controller}/{Action}` when `AddInertiaMvc(o => o.DefaultComponentResolver = ...)` is set.
- `InertiaOptions.ConvertExternalRedirects` (default true as in Rails): in `FinishAsync`, a redirect whose `Location` host differs from
  the request host becomes `409 X-Inertia-Location` for Inertia requests.
- `InertiaOptions.UrlResolver: Func<HttpContext, string>?` to override the page `url` (behind path-rewriting proxies).
- Document `o.Share("plans", Once(...))` as the equivalent of Laravel's `shareOnce`, with a test.
- Document Bun and Deno through `NodeProcess.Executable`.

### 4.6 Templates and orchestration (M)

- `Inertia.Net.Templates`: `dotnet new inertia-react`, `inertia-vue`, `inertia-svelte` generated from the three samples (the samples are
  already kept identical by the shared e2e contract), with `--backend minimal|mvc|fastendpoints` where the sample exists. Packed in
  `release.yml`, smoke-tested in CI by instantiating and building each.
- An Aspire AppHost example: `AddProject` for the app and `AddViteApp` (Aspire 13, `Aspire.Hosting.JavaScript`) for the Vite dev server,
  with the hot-file flow unchanged. Docs only; no new package.

## 5. Order of work

| Phase | Items | Why first |
|---|---|---|
| 1 | 3.1 lazy XSRF, 3.5 compression and caching, 3.6 small items, 4.1 nonce, 4.5 small DX | Cheap, no protocol change, each a few hours; the nonce unblocks strict CSP users today. |
| 2 | 3.2 cached props, 3.3 SSR cache, 3.4 prefetch | The measurable wins; each is self-contained and benchmarked. |
| 3 | 4.4 pages check, 4.3 OpenAPI types, 4.6 templates | The onboarding story. |
| 4 | 4.2 DevTools | Largest item; worth doing once the metadata from 4.4 can feed `componentPath`. |
| Later | 3.7 early hints, 4.4 generator, .NET 11 runtime-async, distributed caches | Blocked on the platform or on demand. |

Every phase keeps the rules in `AGENTS.md`: regression tests in the existing projects, `docs/protocol.md` untouched (none of this changes
the wire format), AOT smoke green, and new benchmark rows in `tests/Inertia.Net.Benchmarks/RESULTS.md` for 3.1 to 3.4.

## 6. Sources

Adapters and protocol:

- Inertia v3 docs: [DevTools protocol](https://inertiajs.com/docs/v3/advanced/devtools-protocol), [DevTools](https://inertiajs.com/docs/v3/advanced/devtools), [SSR](https://inertiajs.com/docs/v3/advanced/server-side-rendering), [TypeScript](https://inertiajs.com/docs/v3/advanced/typescript), [Testing](https://inertiajs.com/docs/v3/advanced/testing)
- inertia-laravel: [releases](https://github.com/inertiajs/inertia-laravel/releases), [Middleware.php](https://github.com/inertiajs/inertia-laravel/blob/3.x/src/Middleware.php), [ServiceProvider.php](https://github.com/inertiajs/inertia-laravel/blob/3.x/src/ServiceProvider.php)
- inertia-rails: [configuration](https://inertia-rails.dev/guide/configuration), [cached props](https://inertia-rails.dev/guide/cached-props), [SSR](https://inertia-rails.dev/guide/server-side-rendering), [responses](https://inertia-rails.dev/guide/responses), [partial reloads](https://inertia-rails.dev/guide/partial-reloads)
- inertia-phoenix: [README](https://inertia.hexdocs.pm/readme.html)
- AdonisJS: [Inertia guide](https://docs.adonisjs.com/guides/frontend/inertia)
- gonertia: [repository](https://github.com/romsar/gonertia)
- InertiaCore.AspNetCore: [NuGet](https://www.nuget.org/packages/InertiaCore.AspNetCore)
- Laravel Vite prefetching: [laravel/framework#52462](https://github.com/laravel/framework/pull/52462)
- Laravel Wayfinder: [blog post](https://laravel.com/blog/laravel-wayfinder-end-to-end-type-safety-for-php-and-typescript)
- awesome-inertiajs: [adapter list](https://github.com/innocenzi/awesome-inertiajs)

Platform:

- [What's new in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)
- [What's new in ASP.NET Core 11](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-11) (Zstandard compression, native OpenTelemetry, `IOutputCachePolicyProvider`, runtime-async)
- [What's new in .NET 10 libraries](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries)
- [Performance improvements in .NET 10](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/)
- [Static files and MapStaticAssets](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/map-static-files?view=aspnetcore-10.0)
- Early hints: [dotnet/aspnetcore#27851](https://github.com/dotnet/aspnetcore/issues/27851)
- Aspire JavaScript hosting: [aspire.dev](https://aspire.dev/integrations/frameworks/javascript)
