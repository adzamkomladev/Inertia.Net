# Adapter conformance

Inertia.Net targets the **Inertia.js v3** protocol. The official Laravel adapter is the behavior reference.
The repository keeps intentional differences in [protocol.md](protocol.md). A framework-specific PHP test is not automatically
a requirement for a .NET API with the same name.

## Reference applications

The official v3 example is [Kitchen Sink](https://demo-v3.inertiajs.com), with [source](https://github.com/inertiajs/demo-v3).
It uses Laravel and Vue. Use it to learn the request flows; use [our samples](../samples/README.md) for runnable .NET integrations.

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
| `flushShared` | No process-global mutable response factory. Startup options and request feature state have separate lifetimes. |
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
