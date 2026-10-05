# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - TBD

First release: a .NET 10 server adapter for the Inertia.js v3 protocol (v3 only, no v2 compatibility mode).

### Added

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
