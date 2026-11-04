# Review fixes: upstream checks (2026-11-04)

## Precognition and rate limiting (plan 2.1)

Checked `laravel/precognition` main at `3a186e29e6e3e4dbda65f9b1160afc4b5430cb0b`.

- The validator declares `let debounceTimeoutDuration = 1500`; it configures lodash debounce with
  `{ leading: true, trailing: true }`. A burst can therefore send its first validation immediately and another after the quiet
  interval. `setValidationTimeout` changes the debounce interval, not the HTTP request deadline. The validator separately defaults
  that deadline to five seconds. [Validator source](https://github.com/laravel/precognition/blob/3a186e29e6e3e4dbda65f9b1160afc4b5430cb0b/packages/core/src/validator.ts#L210-L302)
- For both successful and unsuccessful HTTP responses, a precognitive request first checks for the response header
  `Precognition: true`. An absent header produces the client's missing-Precognition-response error before status-specific
  handling. The server's rate-limit rejection hook should set that header for precognitive requests.
  **A 429 still rejects:** there is no dedicated 429 handler in the client, and the validator suppresses only cancellation and
  expected 422 errors. The header prevents a misleading protocol error; it does not turn a throttled request into successful
  validation. Applications should handle the rejected request or show a retry message.
  [Client source](https://github.com/laravel/precognition/blob/3a186e29e6e3e4dbda65f9b1160afc4b5430cb0b/packages/core/src/client.ts#L79-L211),
  [validator error handling](https://github.com/laravel/precognition/blob/3a186e29e6e3e4dbda65f9b1160afc4b5430cb0b/packages/core/src/validator.ts#L246-L273)
- Laravel documents that precognitive requests execute route middleware while skipping the controller, recommends checking
  precognition before middleware side effects, and shows `setValidationTimeout(3000)`. Its React example validates on blur.
  [Precognition guide](https://laravel.com/docs/13.x/precognition#managing-side-effects),
  [React example](https://laravel.com/docs/13.x/precognition#using-react)
- Laravel's routing guide describes named rate limiters, request-derived partition keys and attaching throttling middleware to
  routes. The Inertia.Net recipe should retain a bounded validation bucket and a separate strict submission bucket, selecting
  the validation bucket only when endpoint metadata guarantees the handler is skipped. This partition design is an adapter
  recommendation based on that middleware ordering, rather than a Laravel-prescribed Precognition policy.
  [Laravel rate limiting](https://laravel.com/docs/13.x/routing#rate-limiting)

Plan wording correction: replace “adds `Precognition: true` to the 429 so the client does not throw” with “adds
`Precognition: true` to the 429 so the client recognizes the response; the throttled request still rejects and needs a retry/error
UI.” No unbounded exemption should be introduced.

## Kestrel response completion and HTTP/1.1 (plan 1.2)

Checked ASP.NET Core `v10.0.14`, commit `0ef4bbfa3291b306a21e5001cb0491277bdd35bc`.
`Http1Connection` inherits `HttpProtocol`; that protocol's request-processing method contains the keep-alive loop. Within each
iteration, it finishes the response and then executes:

```csharp
await FireOnCompleted();
```

Only after completion callbacks finish does it dispose the context, drain remaining request-body state and begin the next loop
iteration. `FireOnCompleted` itself awaits each callback. Consequently an awaited refresh in `OnCompleted` can delay the next
request on the same HTTP/1.1 connection even though the prior response bytes have reached the client. HTTP/2 and HTTP/3 use this
logic per stream and should not be described as serializing every request on their shared connection.
[HTTP/1 implementation](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L14),
[request loop and callback await](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs#L628-L785),
[callback processing](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs#L838-L865)

The plan's premise is confirmed. The callback must only enqueue owned background work and return promptly; the worker owns its
scope, shutdown token and refresh-key cleanup. A same-connection regression test must force HTTP/1.1 and one connection, rather
than rely on the client's default protocol or connection pooling.

## Performance and packaging reassessment against v1.6.2

GitHub identifies `v1.6.2` as the latest release, published 2026-11-04 at 12:26:18 UTC. Its tag resolves to
`2dd4a4b4c09d6b4c5d7abdfd751d0b5409e5c183`, matching the retained benchmark baseline. The release includes EF Core pagination and
testing pagination helpers; neither is a pending dependency. [Release metadata](https://github.com/adzamkomladev/Inertia.Net/releases/tag/v1.6.2)

- **3.1:** [`DictionaryKeys`](../src/Inertia.Net/InertiaPageWriter.cs) allocates its probe dictionary, buffer and writer outside
  its key loop. Serialization and reading the property name still occur per key. The proposed buffer reuse already exists.
  Its `CreateObject == null` fallback uses invariant `Convert.ToString`; compare that path with STJ before prescribing a fix.
  Enums require special care: STJ's dictionary writer accounts for naming policies, defined/undefined values, flags and declared
  JSON field names. A direct `Enum.ToString` replacement is not equivalent.
  [STJ enum key implementation](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/Converters/Value/EnumConverter.cs#L175-L211)
- **3.2:** [`BigIntegerConverters`](../src/Inertia.Net/BigIntegerConverters.cs) parses raw DOM number text as an integer; decimal
  and exponent tokens pass through. `TryGetInt64` is a plausible allocation-free safe-integer shortcut, but writing a parsed
  double would change precision/token spelling. The span-only value/property-name readers need segmented-input coverage;
  escaped property names also require decoding. No runtime regression or allocation improvement was measured in this review.
- **3.3:** [`PropsPlanner.DrainAsync`](../src/Inertia.Net/PropsPlanner.cs) observes secondary failures without reporting them.
  [`PropsWriter`](../src/Inertia.Net/PropsWriter.cs) passes the request token to unplanned loaders. The proposed failure logging
  and linked token require regression evidence, including simultaneous failures and rescued loaders.
- **3.4:** Scroll wrapper list construction, boxed `PropertyValue`, eager fill candidates and repeated planned cache lookups
  are visible in source. Their importance is unmeasured. The ordinary render probe does not exercise every candidate: add
  converter-bearing typed props, scroll metadata and contention workloads before claiming allocation wins.
  [`MemberOptions`](../src/Inertia.Net/InertiaPageWriter.cs) deliberately uses converter instance identity: arbitrary custom
  converter attributes can create differently configured instances of one converter type. There is no general public equality
  contract for converter settings. Preserve that correctness boundary instead of deduplicating by type.
- **3.5:** [`SsrCircuitBreaker`](../src/Inertia.Net/SsrCircuitBreaker.cs) adds duration ticks to UTC ticks. A monotonic opening
  timestamp plus elapsed-time comparison avoids UTC jumps and timestamp-deadline addition. Current tests in
  [`SsrResilienceTests`](../tests/Inertia.Net.Tests/SsrResilienceTests.cs) already call `FakeTimeProvider.Advance`; add a wall
  clock jump test rather than merely rewriting them. Any duration validation bound needs evidence from the chosen implementation.
- **4.1:** The testing framework reference is used by shipped `LoadScrollPageAsync`: `QueryHelpers.ParseQuery` and
  `QueryString.Create` occur in [`InertiaPage`](../src/Inertia.Net.Testing/InertiaPage.cs). Retain it. Both .NET hosted-service
  registration overloads already use `TryAddEnumerable`; duplicate TypeScript hosted-service execution is not established.
  [Runtime registration source](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/ServiceCollectionHostedServiceExtensions.cs#L20-L37)
  API inventories must include the EF Core package, and analyzer dependencies should remain development-only. Enabling recommended
  and build-time style analysis can affect the whole solution; no diagnostic count or implementation-size estimate was measured.
  [Analyzer configuration](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)
- **4.2:** [`InertiaFastEndpointsConfig`](../src/Inertia.Net.FastEndpoints/InertiaFastEndpointsConfig.cs) replaces the existing
  endpoint configurator. Chaining it before the Inertia setup matches the proposed change; preserve the optional callback's
  existing position after setup, and run the shipped conformance and pagination cases.

These are source-level findings, not claims that new regression tests or performance gates passed.

## Integration and release-scope corrections

[`InertiaErrorPages`](../src/Inertia.Net/InertiaErrorPages.cs) declines responses that have started, 409 and 419. Its exception
handler otherwise forwards exceptions to error-page rendering, including SSR exceptions. The default error page only receives a
status; configured `Props` callbacks receive the original exception. Truncation and removing an explicitly added endpoint URL do
not make arbitrary renderer-provided error/type text universally safe to disclose. A required-SSR integration test needs to cover
this handler and avoid recursively invoking the failing renderer for the replacement error page.

The current Precognition repository has `core`, `react`, `vue` and `alpine` packages, with no official Svelte wrapper. Three-client
examples must use verified Inertia form integration or the core client where appropriate.
[Package inventory](https://github.com/laravel/precognition/tree/3a186e29e6e3e4dbda65f9b1160afc4b5430cb0b/packages)

Phase 7 was already excluded from the 1.6.3 release text but was still required by its definition of done. Its spike, measurements
and implementation now belong solely to the separate post-release branch. The future gate also needs a design-specific decision:
a memory win for design A alone cannot establish a TTFB case for shipping design B.

Recorded test evidence shows that both real size-limited `MemoryCache` replacement tests for 1.4 passed against the baseline: the cache
removed the replaced entry rather than preserving the old value assumed by the plan. Post-commit tag-index publication can remain
a defensive consistency improvement, but must not be reported as a reproduced surviving-entry defect. This subsection records the
supplied test output; this source review did not run those tests independently.
