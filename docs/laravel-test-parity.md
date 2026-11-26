# Laravel 3.x test coverage map

Reference: [45 upstream suites at c62b763](https://github.com/inertiajs/inertia-laravel/tree/c62b763b87d8daa9f8f90789dd29ffdf21f33fc6/tests),
reviewed for the 1.9.7 adapter-conformance work. There are 718 test methods, or 722 provider-expanded invocations before inline
scenarios. This is a normalized behavior map, not a claim of identical test method counts or PHP API compatibility.
Test filenames below are under `tests/Inertia.Net.Tests` unless qualified.

| Upstream suite | Methods | .NET coverage / applicability |
|---|---:|---|
| AlwaysPropTest | 5 | `OverloadBindingTests`, `PropsWriterTests`, `ScopedPropsTests`; strings cannot become delegates |
| BroadcastPropsTest | 16 | `AdapterParityTests`, `DevToolsParityTests`, `LivePropsTests`; transport envelope differences documented in protocol |
| Commands/CheckSsrTest | 3 | Managed readiness/health in `SsrNodeProcessTests`, `SsrResilienceTests`; Artisan interface not applicable |
| Commands/StartSsrTest | 13 | `SsrNodeProcessTests`; owned process lifecycle replaces Artisan command options |
| Commands/StopSsrTest | 5 | `SsrNodeProcessTests`; kills owned processes rather than requesting an external shutdown URL |
| ComponentTest | 14 | `RootTemplateTests`, `HeadTests`, integration `MvcTests`; Blade syntax/compiler cache not applicable |
| ControllerTest | 1 | Integration `EndpointAndRedirectTests` and shared `ProtocolConformanceTests` |
| DeepMergePropTest | 5 | `FluentPropTests`, `ResponseTests`, `PropsResolverTests` |
| DeferPropTest | 6 | `ResponseTests`, `OverloadBindingTests`, `PropsWriterTests` |
| DevTools/AuthorizeGateTest | 5 | `DevToolsRecorderTests`, `DevToolsParityTests`; HttpContext authorization replaces Laravel Gate |
| DevTools/AuthorizeMiddlewareTest | 2 | Integration `DevToolsTests`; normal ASP.NET endpoint authorization remains composable |
| DevTools/AuthorizeTest | 3 | `DevToolsRecorderTests`, integration `DevToolsTests`; explicit Authorize is authoritative even in Development |
| DevTools/CollectorIntegrationTest | 15 | `DevToolsParityTests`, integration `DevToolsTests`; optional source-inference limitations below |
| DevTools/DevToolsTest | 5 | `DevToolsRecorderTests`, `DevToolsParityTests`; exact/prefix path exclusions |
| DevTools/EntriesRepositoryTest | 11 | `DevToolsRecorderTests`, `DevToolsParityTests`; in-memory storage has no file/index corruption API |
| DevTools/EntryStoreTest | 5 | `DevToolsRecorderTests`, `DevToolsParityTests`; direct completion replaces staged filesystem flush |
| DevTools/FlashDataTest | 2 | Integration `DevToolsTests`, `AdapterParityTests`; inspector reads do not consume state |
| DevTools/HttpEndpointsTest | 4 | Integration `DevToolsTests`, `DevToolsParityTests`; full details, summaries, PathBase, filters and malformed IDs |
| DevTools/IncomingEntryBuilderMatrixTest | 21 | `DevToolsParityTests`, integration `DevToolsTests`; safe bounded capture differences below |
| DevTools/IncomingEntryBuilderTest | 3 | `DevToolsParityTests`, `DevToolsRecorderTests`; final serialized JSON is captured, not reserialized PHP objects |
| DevTools/MiddlewareDevToolsDisabledTest | 2 | Integration `DevToolsTests`, `DevToolsRecorderTests` |
| DevTools/MiddlewareDevToolsTest | 30 | Integration `DevToolsTests`, `DevToolsParityTests`, Native AOT smoke |
| DevTools/OctaneDevToolsTest | 1 | Request-scoped feature isolation replaces Octane events; `ResultTests`, integration conformance tests |
| DevTools/PropClassifierTest | 36 | `DevToolsParityTests`, integration `DevToolsTests`; normalized flags, groups, merge owners/directions and reset |
| DevTools/RecorderResilienceTest | 1 | `DevToolsParityTests.Recorder_failure_does_not_change_the_response` |
| DevTools/RedactsSensitiveDataTest | 5 | Integration `DevToolsTests`, `DevToolsParityTests`; custom keys, nested arrays, URLs, malformed/oversized bodies |
| DirectiveTest | 7 | `RootTemplateTests`, integration `MvcTests`; Razor/template output replaces Blade directives |
| ExceptionResponseTest | 9 | Integration `ErrorPagesTests`; shared omission, custom handlers, redirect/fallthrough and unregistered behavior |
| HelperTest | 3 | Static `Inertia` factories and service registration; PHP global helper identity is not applicable |
| HistoryTest | 11 | `PageWriterTests`, `StateStoreTests`, integration `ProtocolConformanceTests` |
| HttpGatewayTest | 31 | `SsrGatewayTests`, `SsrResilienceTests`, `SsrCacheTests`; named HttpClient customization replaces PHP callbacks |
| LivePropTest | 65 | `LivePropsTests`, `PropsWriterTests`, `AdapterParityTests`, integration `LiveSignalRTests`, browser live specs |
| MergePropTest | 16 | `FluentPropTests`, `ResponseTests`, `PropsResolverTests`; repeated fluent calls replace PHP array overloads |
| MiddlewareTest | 61 | `PageWriterTests`, `PreviousUrlTests`, `ResultTests`, integration `ProtocolConformanceTests`, `EndpointAndRedirectTests` |
| OncePropTest | 7 | `ResponseTests`, `PropsWriterTests`, `FluentPropTests`, `AdapterParityTests` |
| OptionalPropTest | 4 | `ResponseTests`, `OverloadBindingTests`, integration `AdapterHelperParityTests` |
| PropsResolverTest | 79 | `PropsResolverTests`, `SerializationRegressionTests`, `MemberSerializationTests`, `GeneratedSerializerTests` |
| ResponseFactoryTest | 74 | `PageWriterTests`, `ResultTests`, `AdapterParityTests`, `PreviousUrlTests`; request-local shares replace mutable factory state |
| ResponseTest | 68 | `ResponseTests`, `PageWriterTests`, `AdapterParityTests`, `PropsWriterTests`; provider-interface boundary below |
| ScrollMetadataTest | 4 | `Pagination/PageTests`, `Pagination/PaginationTests`, integration `ProtocolConformanceTests` |
| ScrollPropTest | 9 | `Pagination/ScrollPageTests`, `PropsWriterTests`; documented deferred-scroll metadata deviation |
| ServiceProviderTest | 7 | ASP.NET dependency injection, middleware and Razor integrations; Laravel facade/Blade registration is not applicable |
| SsrRenderFailedTest | 5 | `SsrGatewayTests`; exact diagnostics and source-generated serialization |
| Testing/AssertableInertiaTest | 30 | `Inertia.Net.Testing.Tests/AssertionTests`, `ParsingTests`, `ParityTests` |
| Testing/TestResponseMacrosTest | 9 | `Inertia.Net.Testing.Tests/ClientTests`, integration `AdapterHelperParityTests`; assert flash on the final page |

## Scope and deliberate differences

- The [protocol deviations](protocol.md#deliberate-deviations-from-laravel) remain intentional. Do not add assertions demanding
  unsafe Referer handling, eager lazy-dot evaluation, different array replacement behavior or late version checks.
- PHP interfaces (`Arrayable`, resources and property providers), reflective broadcast discovery, global function-name callable
  inference, Blade caching, Octane hooks and Artisan options do not require identical .NET APIs. Typed serialization, delegates,
  scoped services, explicit live listeners and managed processes provide the corresponding supported behaviors.
- Laravel's property-provider context that reads the response factory's existing shared values has no literal public .NET hook.
  Compose application-owned data in loaders instead. This is not the same feature as client-side `Merge` metadata.
- Redirect assertion helpers inspect flash after following redirects; they do not decrypt state from a bare redirect response.
- DevTools captures only bounded response data and summarizes already-parsed uploads without reading file contents. It deliberately
  omits malformed structured data/invalid UTF-8 instead of exposing potentially unredacted raw text. Limits are configurable.
- Render callsites and component paths are best effort when symbols/files are available. Per-key share-source inference and route
  action source discovery are optional diagnostics not promised by the .NET API.

See [adapter-conformance.md](adapter-conformance.md) for changes and actual release verification. A mapped suite is not a claim that
every PHP-specific assertion was ported verbatim; supported behavior and intentional boundaries are the compatibility contract.
