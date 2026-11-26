# Adapter conformance

Inertia.Net targets the **Inertia.js v3** protocol. The official Laravel adapter is the behavior reference.
The repository keeps intentional differences in [protocol.md](protocol.md). A framework-specific PHP test is not automatically
a requirement for a .NET API with the same name.

## Reference applications

The official v3 example is [Kitchen Sink](https://demo-v3.inertiajs.com), with [source](https://github.com/inertiajs/demo-v3).
It uses Laravel and Vue. Use it to learn the request flows; use [our samples](../samples/README.md) for runnable .NET integrations.

## v1.9.7 audit and regressions

The reference snapshot is [`inertia-laravel` 3.x, `c62b763`](https://github.com/inertiajs/inertia-laravel/tree/c62b763b87d8daa9f8f90789dd29ffdf21f33fc6/tests):
45 suites and 718 declared test methods (722 invocations after expanding four two-row providers, before inline scenarios).
Test counts are not a parity percentage: .NET theories and consolidated tests cover multiple upstream methods, and PHP framework
APIs do not translate literally.
The [suite-by-suite coverage map](laravel-test-parity.md) records every upstream suite and the supported .NET counterparts.

| Audit finding | Resolution and regression coverage |
|---|---|
| Body-bearing external/fragment redirects | Suppress converted response bytes through both stream and pipe paths; `EndpointAndRedirectTests`, including Kestrel |
| Full entries returned by the list endpoint | Metadata-only list; detail remains full; `DevToolsTests`, AOT smoke |
| Raw response inspection | Bounded text/JSON capture, binary/stream/oversize omission and safe malformed-body handling; `DevToolsTests`, `DevToolsParityTests` |
| Upload metadata and render locations | Summarize parsed forms without reading files; capture symbol-backed callsites; `DevToolsParityTests`, `DevToolsTests` |
| Redirect metadata | Inertia header precedence and status-aware ordinary Location; `DevToolsParityTests` |
| Prop classification | Explicit false/nulls, deferred delivery/group, reset, owning merge path/direction and match-on; `DevToolsParityTests`, `DevToolsTests` |
| Recorder isolation and boundaries | Throwing input stream, original body position, multi-value headers, exact size limits; `DevToolsParityTests` |
| Broadcast during a page capture | Existing capture remains untouched by broadcast-only props; `DevToolsParityTests` |
| Entry retention/read API edge cases | Age/global/per-tab/byte limits, deterministic ties, malformed pagination/IDs; recorder and integration tests |
| Shared/once partial behavior | Loaded shared-once metadata and no-filter partial reloads; `AdapterParityTests` |
| Previous URL eligibility | Non-Inertia AJAX exclusion and opt-in partial storage; `PreviousUrlTests` |
| Clearing/omitting shared props | Request-local `FlushShared` and error-page `IncludeSharedProps`; adapter parity/error-page tests |
| SSR diagnostic contract | Exact values and generated JSON metadata, including absent optional fields; `SsrGatewayTests` |
| Assertion page-file lookup | Search later directories and retain extension/explicit-override checks; testing `ParityTests` |

### Tooling and ecosystem boundaries

Malformed JSON and invalid UTF-8 are deliberately omitted rather than exposed as unredactable raw text. Uploads are summarized
only when the application has already parsed the form; the recorder never spools arbitrary uploads solely to match PHP's eager
request parsing. Source locations depend on retained symbols. Body/retention limits are configurable and need not equal Laravel's
constants. These choices preserve the existing security and bounded-memory guarantees.

PHP provider interfaces that receive Laravel's response-factory context are not reproduced as .NET interfaces; compose
application-owned shared values in typed/scoped delegates instead. Redirect flash assertions follow the redirect and inspect
the final page rather than exposing encrypted cookie contents on a bare `302`. Per-dictionary-key source inference, share
declaration locations and route-action source discovery are not promised; recorded render callsites and component paths are
best-effort symbol-backed diagnostics. These are optional ecosystem interfaces, not additional page-protocol requirements.

Artisan commands, Blade compiler caching, PHP callable-name ambiguity, facade identity, PHP conversion interfaces, reflection-based
broadcast event discovery and Laravel HTTP-client configuration callbacks are ecosystem APIs rather than required .NET surfaces.
Their meaningful outcomes are covered through managed SSR, Razor/templates, typed serialization, scoped delegates, explicit live
listeners and ASP.NET request isolation. The thirteen page-protocol deviations remain documented in [protocol.md](protocol.md).
No claim of byte-for-byte PHP API or assertion parity is implied.

## v1.9.6 changes

| Area | Implementation | Regression coverage |
|---|---|---|
| QUERY redirects | `InertiaMiddleware.RequiresSeeOther` | `AdapterParityTests`, shared `ProtocolConformanceTests`, `RateLimitingTests` |
| Flash removal and lifetime | `InertiaFeature.ForgetFlashed` | `AdapterParityTests`, shared `ProtocolConformanceTests` |
| Per-response big integers | `InertiaResult.PreserveBigIntegers`, immutable writer settings | `AdapterParityTests`, AOT `/parity` smoke |
| Broadcast prop resolution | `BroadcastPropsAsync`, broadcast mode in `PropsWriter` | `AdapterParityTests`, AOT `/parity` smoke |
| SSR failure notification | `Ssr.OnRenderFailed`, `SsrRenderFailed` | `SsrGatewayTests` |
| Component transformation | `InertiaOptions.ComponentTransformer` | `AdapterParityTests` |
| Assertion page-file checks and integers | `InertiaTestingOptions`, `AssertableInertia` | `Inertia.Net.Testing.Tests/ParityTests` |
| DevTools URL/base path/state | `InertiaDevTools`, middleware read bypass | `DevToolsTests`, `AdapterParityTests` |
| DevTools classification | Prop metadata captured during resolution, boolean live/once flags | `DevToolsTests` |
| Post-response work | Native ASP.NET Core `OnCompleted` | `MiddlewareCompositionTests` |

## Equivalent APIs and deliberate omissions

| Laravel concern | .NET choice |
|---|---|
| `shareOnce` | `o.Share("plans", Once(...))`; no extra helper needed. |
| `flushShared` | `context.Inertia().FlushShared()` clears shares for one request; startup options and other requests are unchanged. |
| Previous route name | Not stored. `State.RememberPreviousUrl` stores a validated local URL; use ASP.NET routing metadata in application code. |
| `handleExceptionsUsing` | `AddInertiaErrorPages`/`UseInertiaErrorPages` render configured errors. Custom redirects and handler selection use ASP.NET `IExceptionHandler`. |
| Explicit middleware replay on error | Not replayed. Startup shares and request shares already collected are available; keep required shared data before throwing middleware. |
| Per-error root template | Use `Render(...).WithRootView(...)` from a custom ASP.NET exception handler. The built-in error-page options do not replay arbitrary middleware. |
| SSR request configurator | Configure `services.AddHttpClient(InertiaSsr.HttpClientName)` and delegating handlers; do not duplicate HttpClient configuration. |
| Start/stop/check SSR commands | `Ssr.UseNodeProcess` owns process lifetime; `AddInertiaSsr` exposes health. No Laravel-style CLI. |
| Deferred callback suppression on 4xx | Not copied. ASP.NET completion callbacks run for failed responses too. A version mismatch runs before the handler. |
| PHP macros, Blade directives, enums as names, resources/providers | C# factories, Razor helpers, typed DTOs, scoped loaders and source-generated JSON contracts. |
| Cyclic objects | Follow System.Text.Json cycle/depth handling. Do not silently invent a client representation for a cycle. |
| DevTools source locations | Optional, not collected. `renderSource` is null; share/action source fields are absent. |
| DevTools persistent repository/Octane hooks | Not applicable. Entries use bounded in-memory storage and ASP.NET request lifetimes. |

## Test map

The existing prop suites port many upstream scenarios and combine related cases into theories. Counts are not a meaningful
one-to-one measure: .NET runs shared HTTP cases across three server styles and two runtimes.
This is a behavior map, **not a claim that every upstream test method has a literal port**.

| Upstream suite | Local suites |
|---|---|
| Always, optional, defer, once, merge, deep merge | `PropsResolverTests`, `PropsWriterTests`, `ResponseTests`, `FluentPropTests`, `OverloadBindingTests` |
| Nested/typed props and JSON contracts | `SerializationRegressionTests`, `GeneratedSerializerTests`, `MemberSerializationTests`, `TypedContractsTests` |
| Scroll and scroll metadata | `PropsWriterTests`, `Pagination/*`, `ProtocolConformanceTests` |
| Live props and broadcasting | `LivePropsTests`, `PropsWriterTests`, `LiveSignalRTests`, `AdapterParityTests`, browser live specs |
| Response factory and middleware | `PageWriterTests`, `ResultTests`, `ProtocolConformanceTests`, `MiddlewareCompositionTests`, `AdapterParityTests` |
| Templates/components/controllers | `RootTemplateTests`, `MvcTests`, `InertiaViewTests`, `EndpointAndRedirectTests` |
| History, errors, flash | `PageWriterTests`, `StateStoreTests`, `ProtocolConformanceTests`, `ErrorPagesTests` |
| SSR gateway, failures and commands | `SsrGatewayTests`, `SsrResilienceTests`, `SsrCacheTests`, `SsrNodeProcessTests` |
| Testing helpers | `tests/Inertia.Net.Testing.Tests/` |
| DevTools protocol, authorization, redaction and storage limits | `DevToolsTests`, `DevToolsRecorderTests` |

## Verification

Run the commands in [AGENTS.md](../AGENTS.md). The release checks include build/test, packaging, Native AOT,
sample client/browser tests, and the documentation client/SSR build. Redis and MongoDB tests need running services.
Release notes must report actual results; upstream API parity alone does not prove that the official client accepts a response.

### v1.9.7 local release checks (2026-11-26)

| Check | Result |
|---|---|
| Release solution build with warnings as errors | Passed; 0 warnings, 0 errors |
| Solution tests on .NET 10 and 11, with Redis and MongoDB available | 3,964 passed; 0 failed; 0 skipped |
| Solution packaging with warnings as errors | All 9 expected 1.9.7 NuGet packages created |
| Native AOT publish and protocol smoke | Passed on net10.0 and net11.0, including metadata-only lists and ordinary-response capture |
| Generated templates: React/Vue/Svelte × net10.0/net11.0 × auth/no-auth | All 12 server builds passed with warnings as errors |
| React browser suite, SSR enabled | 32 passed, including real SSR hydration |
| Vue browser suite | 15 passed; 17 React-only checks skipped |
| Svelte browser suite | 15 passed; 17 React-only checks skipped |
| Docs TypeScript, client bundle and SSR bundle | Passed |
| README/conformance/parity-map local links | No missing local targets |

These are local Linux checks; Windows and the full generated-template frontend matrix are verified separately by repository CI.

### v1.9.6 release checks (2026-11-26)

| Check | Result |
|---|---|
| Release solution build with warnings as errors | Passed; 0 warnings, 0 errors |
| Solution tests on .NET 10 and 11, with Redis and MongoDB available | 3,694 passed; 0 failed; 0 skipped |
| Solution packaging with warnings as errors | All 9 expected NuGet packages and symbol packages created |
| Native AOT publish and protocol smoke | Passed for both net10.0 and net11.0, including the new broadcast/flash/integer path |
| React browser suite, SSR enabled | 32 passed |
| Vue browser suite | 15 passed; 17 React-only checks skipped |
| Svelte browser suite | 15 passed; 17 React-only checks skipped |
| Docs TypeScript, client bundle and SSR bundle | Passed |
| README local links and docs page links | No missing local targets found |

These are local Linux checks. The repository CI also verifies Windows, generated templates and its own service-backed jobs.
