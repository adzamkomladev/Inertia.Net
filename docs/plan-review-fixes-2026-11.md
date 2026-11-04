# Security, performance and developer-experience fixes (review, November 2026)

Status: open, targeting **1.6.3**. Original review: 1.5.3 (`5d0fa8a`). **Corrected baseline: fetched `origin/main`
at `2dd4a4b`, with GitHub release `v1.6.2` confirmed on 2026-11-04.** The earlier reassessment used the stale local checkout
at `6bafde2` and incorrectly treated shipped features as future dependencies. This correction supersedes that assessment.
Line references below are navigation hints from earlier revisions; resolve symbols against the fetched baseline before editing.
The remaining review findings need regression evidence before implementation, not a blanket assumption that all are confirmed.

### Already shipped — preserve and integrate

| Release | Delivered features | Evidence |
|---|---|---|
| 1.6.0 | Live props, SignalR transport, client transport, live assertions and sample | `c337f37`, PRs #23 and #24 |
| 1.6.1 | Production Inertia error pages, previous-URL support for `Back()`, error/status testing helpers and sample/template error pages | `e707697`, PR #31, GitHub release `v1.6.1` |
| 1.6.2 | Core numbered/cursor pagination, scroll integration, testing helpers, EF Core offset/keyset pagination and measured pagination benchmarks | `5452737`, `59b3d3e`, `2dd4a4b`, PRs #32–#34, GitHub release `v1.6.2` |

These are existing capabilities, not implementation tasks or pending prerequisites. Their older planning documents may have stale
status headings; use the released source, changelog, tests and GitHub releases as the source of truth. This update inspected
remote history, release listings, source changes and test inventory; it did not run builds, tests or benchmarks.

It is written to be executed by a **Sol (medium/high) coordinator** delegating code implementation to **Sol 6.1 (medium/high) workers**
and browser/research work to **Terra 6.1 (medium/high) workers**, following the same
shape as [plan-pagination-2026-11.md](plan-pagination-2026-11.md) and [plan-back-and-error-pages-2026-11.md](plan-back-and-error-pages-2026-11.md);
the delegation map, gates and stop conditions are in the last sections. Each task names the files it touches, its size (S: hours,
M: a day or two, L: more), how it is verified, and the worker profile:

- **Sol high**: core protocol, cache, SSR and prop-writer changes where a wrong edit silently corrupts output. The coordinator
  does these itself or reviews every diff line.
- **Sol medium**: contained code changes, tests, docs and packaging.
- **Terra medium/high**: anything that needs a browser or the network: running the samples under Playwright, screenshots,
  fetching upstream docs or client source to confirm behaviour, checking the docs site.

**Release.** This plan ships as **`1.6.3`** (maintainer decision, 2026-11-04): phases 1 to 5 land on one release branch, in
phase order, and `src/Directory.Build.props` is bumped once. The library has no production users yet, so behaviour changes
(2.3, 2.5, 4.2) are made outright: **no `[Obsolete]` forwarders, no compatibility shims, no "new in version" or migration pages.**
Old names and old behaviour are removed and the replacement is documented as the only way; `CHANGELOG.md` records the change.
Phase 7 (streaming) is not part of `1.6.3`: it runs afterwards on its own branch and gets its own version if its gate passes.

**Sequencing.** Start implementation from current `origin/main` containing all three releases, carrying this plan forward
without overwriting local work. There is no dependency on a future pagination, live-props, previous-URL or error-pages merge.
Phase 7 follows this plan's performance work. Changes to rendering, caching, validation and middleware must preserve the shipped
pagination/scroll behaviour, previous-URL cookies and error-page handling. In particular, test SSR failures through the existing
`InertiaExceptionHandler`; do not assume SSR exceptions are excluded from its handling. Protocol 409 and antiforgery 419 are
explicitly excluded by the shipped error-page renderer.

Rules that apply to every task (from [AGENTS.md](../AGENTS.md)): keep changes small, reuse existing helpers, trace callers before
changing shared behaviour, preserve Native AOT and source-generated JSON, keep nullable annotations and public XML docs, add
regression coverage to the existing test projects, and run from the repository root:

```sh
dotnet build Inertia.Net.slnx -c Release -warnaserror
dotnet test --solution Inertia.Net.slnx -c Release --no-build
```

Report the checks actually run. Never claim an unrun check passed. No assistant attribution anywhere.

Work in phases on the `1.6.3` release branch, one pull request per phase into it (or per task where a task is M or larger).
Write the failing regression test **before** the fix in every correctness task so the test demonstrably catches the bug.

## Phase 1: correctness and security (ship first)

### 1.1 `Cache()` is bypassed when dot keys sit beneath the prop (M, Sol high)

**Problem.** `ToTraversable` (`src/Inertia.Net/InertiaPageWriter.cs:612-622`) wraps any eager, non-always, non-once, non-merge,
non-rescue prop in `LazyDotObject` without checking `Caching` (nor `LiveSpec`: a live prop under dot keys loses its `liveProps`
entry the same way; cover it in the same change). The `LazyDotObject` branch of `WriteValueAsync`
(`src/Inertia.Net/PropsWriter.cs:614-619`) then calls `ResolveAsync(lazy.Source)` without `cached: true`, so
`["auth"] = Prop(...).Cache("auth", ...)` next to `["auth.user"] = ...` runs the loader on every request and never stores.
With `.Parallel()` the planner (`src/Inertia.Net/PropsPlanner.cs:165-167`) does go through `LoadCachedPropAsync` and
`lazy.Apply(source)` receives a boxed `CachedProp`, which `CopyObject` turns into an empty object: the response is likely `{}` plus
the dot keys. Confirm the parallel symptom with the test before deciding the fix shape.

**Change.** Either exclude `Caching: not null` from the `ToTraversable` pattern (the prop is then copied like other special props
and keeps going through `WriteCachedAsync`), or teach the `LazyDotObject` path to resolve through `WriteCachedAsync` and apply the
dot keys to the deserialized value. Prefer the first unless it changes the documented deviation 7 semantics for cached props; if it
does, document the choice in `docs/protocol.md`.

**Tests.** `tests/Inertia.Net.Tests/PropsResolverTests.cs`: (a) sequential cached prop with a dot key below it stores on the first
render and skips the loader on the second; (b) the same with `.Parallel()` produces the merged object, not `{}`; (c) a partial
reload that leaves the path out never runs the loader (deviation 7 still holds); (d) in `LivePropsTests.cs`, a live prop with a
dot key below it still lists its listener in `liveProps`.

**Verify.** Build, tests, and `tests/Inertia.Net.Benchmarks` `--render-probe` to show no regression on the ordinary render rows.

### 1.2 Stale-while-revalidate refresh runs inside `Response.OnCompleted` (M, Sol high)

**Problem.** `ScheduleRefresh`/`OnCompleted` (`src/Inertia.Net/SsrGateway.cs:295-305`) registers `RefreshAsync` as an
`OnCompleted` callback. Kestrel awaits `OnCompleted` callbacks before reusing an HTTP/1.1 connection, so the whole re-render (slot
wait plus HTTP, up to `Ssr.Timeout`, with `CancellationToken.None` at `:318`) stalls the browser's next request on that keep-alive
connection. **Confirmed for cached props too:** `PropRefreshes` is scheduled the same way at `src/Inertia.Net/PropsWriter.cs:584`
and runs every stale prop loader (`PropRefreshes.RunAsync`, `:1103-1140`) inside the callback. Fix both with the same worker.

**Change.** Keep `OnCompleted` as the trigger only. Hand the refresh to a background worker: the simplest AOT-safe option is a
singleton `Channel<RefreshWork>` drained by a hosted service registered in `AddInertia`, bounded (drop and log when full, since a
stale render is still served), cancelled on host shutdown. Reuse the existing `_caching.TryBeginRefresh`/`EndRefresh` ownership so
there is still one refresh per key per app instance. Record a metric for dropped refreshes on `InertiaMetrics`.

**Tests.** `tests/Inertia.Net.Tests/SsrCacheTests.cs`: a stale hit returns before the refresh render starts (use the fake renderer
with a gate), the refresh still runs exactly once, and a second stale hit during the refresh does not start another. An integration
test in `tests/Inertia.Net.IntegrationTests` that a second request on the same `HttpClient` connection is not delayed by a slow
refresh (measure with a renderer that delays 500 ms; assert the second response completes well under that).

**Verify.** Build, tests, `--render-probe` and the SSR cache benchmark rows in `tests/Inertia.Net.Benchmarks/RESULTS.md` format.

### 1.3 No connect timeout on the SSR `HttpClient` (S, Sol medium)

**Problem.** `src/Inertia.Net/InertiaServiceCollectionExtensions.cs:38-45` sets `HttpClient.Timeout = Infinite` and leaves
`SocketsHttpHandler.ConnectTimeout` infinite. `Ssr.Timeout = Timeout.InfiniteTimeSpan` is allowed
(`src/Inertia.Net/InertiaOptionsValidator.cs:15`), so a black-holed renderer pins render slots forever and every later page is
`overloaded`.

**Change.** Set `ConnectTimeout` on the primary handler (5 s; expose as `SsrOptions.ConnectTimeout`, validated positive or
infinite, documented in `README.md` SSR options). The health ping in `SsrNodeProcess.PingAsync` already bounds itself.

**Tests.** `tests/Inertia.Net.Tests`: an `Ssr.Urls` entry pointing at a non-routable address (e.g. `http://10.255.255.1:9`) with
`Ssr.Timeout = Infinite` fails with type `connection` within the connect timeout. Mark the test to skip where the network
sandbox makes the address reachable or instantly refused.

### 1.4 Tag index desync when `MemoryCache` refuses an entry for capacity (S, Sol high)

**Problem.** `MemoryInertiaCacheStore.SetAsync` (`src/Inertia.Net/MemoryInertiaCacheStore.cs:43-66`) writes the tag index to the
new array before the cache commits. When the size-limited cache rejects the entry (`EvictionReason.Capacity`), `OnEvicted`
(`:113-130`) fires for the rejected value, finds the index pointing at it, and removes the row. The surviving old entry has no tag
row, so `RemoveByTagAsync`/`ClearSsrAsync` can no longer evict it. This is exactly the refresh-into-a-full-cache path.

**Reproduction update.** Real size-limited cache replacement tests for both cache kinds passed against the release baseline:
.NET removed the replaced entry rather than leaving the old value assumed above. Do not report a reproduced surviving-entry
defect. Retain any post-commit index publication change as defensive consistency work, and report the passing-before evidence.

**Change.** After `CreateEntry` is disposed (committed), check `cache.TryGetValue(cacheKey, out current)` and only replace the
index row when `ReferenceEquals(current, value)`; otherwise leave the previous row in place. Alternatively restore the previous row
in `OnEvicted` when the reason is `Capacity` and the index already pointed at a different live value. Keep it under the existing lock.

**Tests.** `tests/Inertia.Net.Tests` cache store tests: fill `_renders` to the limit, `SetAsync` the same key with a larger value
that is refused, then `RemoveByTagAsync(tag)` must still remove the old entry. Same for props with an app `IMemoryCache` that has
`SizeLimit`.

### 1.5 `WithInertiaValidation()` is a silent no-op without `AddValidation()` (S, Sol medium)

**Problem.** `FindValidatableParameters` (`src/Inertia.Net/InertiaValidation.cs:176-181`) returns null when `ValidationOptions`
has no resolvers; the filter then passes every request through with no log or exception. Forms "succeed" with invalid data.

**Change.** In `Create`, when `validatable is null` **and** `ValidationOptions` has no resolvers, throw
`InvalidOperationException("WithInertiaValidation() needs services.AddValidation(). Add it at startup, or remove the call.")`
at endpoint build time (the factory runs at startup, so it fails fast). When resolvers exist but no parameter is validatable, keep
the pass-through (that is a legitimate endpoint without a body) but log once at Debug. Mirror the message style of
`InertiaMiddleware.cs:215`.

**Tests.** `tests/Inertia.Net.IntegrationTests`: an app without `AddValidation()` that maps a `WithInertiaValidation()` endpoint
fails to start with that message; an app with it still passes the existing validation tests.

### 1.6 Truncate `InertiaSsrException.Message` and keep internal URLs to logs (S, Sol medium)

**Problem.** `src/Inertia.Net/SsrGateway.cs:726-753`: in `Required` mode the exception message carries the renderer's untruncated
`error`/`hint` text and `endpoint.DisplayUrl`; only the log text goes through `Truncate` (`:757`), and `attempt.Type` is not
truncated at all. An app `IExceptionHandler` that echoes `ex.Message` leaks it.

**Change.** Apply the same 2 KB truncation to the message and the type; put the endpoint URL in the log only (or keep it in
`InertiaSsrException` as a property, not in `Message`). Document on the exception that `Message` is safe to show in an error page.

**Tests.** `tests/Inertia.Net.Tests` SSR gateway tests: a 4 KB renderer error yields an exception message of at most 2 KB plus a
marker, and the message does not contain the renderer URL. Include the shipped `InertiaExceptionHandler` path: default error
pages expose only status, configured props may receive the original exception, and the error response must not recurse through
the failing renderer. Truncation alone does not sanitize arbitrary renderer text or guarantee it contains no secrets; only claim
a display-safe message if its content is generic and diagnostics stay in logs or explicitly diagnostic properties.

## Phase 2: developer experience

### 2.1 Precognition and rate limiting (S, Sol medium; Terra medium for the upstream check)

**Problem.** Precognition requests are real `POST`s to the form endpoint, distinguished only by `Precognition: true`. A rate
limiter on a login endpoint counts every keystroke validation against the login bucket, and users lock themselves out. The fix is a
partitioned rate-limit policy, but today the app has to reimplement the header check because `InertiaPrecognition.IsPrecognitive`
is `internal` (`src/Inertia.Net/InertiaPrecognition.cs:13-20`), and there is no endpoint metadata that says "this endpoint
short-circuits precognitive requests", so a policy cannot tell `WithInertiaPrecognition()` apart from `WithInertiaValidation()`.

**Change.**
1. Make a public static `InertiaPrecognition.IsPrecognitive(HttpRequest)` (or `HttpContext`) with XML docs, keeping the header
   constants public as well.
2. Have `WithInertiaPrecognition()` add an endpoint metadata marker (`IInertiaPrecognitionMetadata` or a sealed
   `InertiaPrecognitionMetadata` record) so a rate-limit policy can check `httpContext.GetEndpoint()?.Metadata` and only relax the
   limit where the handler is guaranteed not to run.
3. README section under Precognition: "Rate limiting a precognitive endpoint" with the partitioned `AddPolicy` example (strict
   bucket for real submits, looser sliding window for precognitive requests on endpoints carrying the metadata, never
   `GetNoLimiter`), the `OnRejected` hook that adds `Precognition: true` to the 429 so the client recognizes the response (429 still rejects and must be handled), and the
   client-side advice (`setValidationTimeout`, validate on blur). Keep it short and link to the full guide written in 5.1. Add
   the same to the template's login flow only if the template already rate-limits; otherwise the README note is enough.

**Terra check.** Fetch the current `laravel-precognition` client source (the `validateTimeout` default and what it does with a
response that lacks the `Precognition` header) and the Laravel docs on precognition and throttling, and confirm or correct the
README wording before it is written.

**Tests.** `tests/Inertia.Net.IntegrationTests`: a policy that reads the metadata relaxes the limit for a precognitive request on a
`WithInertiaPrecognition()` endpoint and does not on a `WithInertiaValidation()` endpoint; a rejected precognitive request carries
`Precognition: true`.

### 2.2 Testing package: XSRF helper and error assertions (S, Sol medium)

**Problem.** `README.md` tells every backend to add `UseInertiaAntiforgery()` first, but `src/Inertia.Net.Testing` has no helper
that reads `XSRF-TOKEN` and sends `X-XSRF-TOKEN`; the repository's own tests slice `Set-Cookie` by hand
(`tests/Inertia.Net.IntegrationTests/InertiaAntiforgeryTests.cs:89-90`). The first form test a new user writes gets a 419. There is
also no `HasErrors`-style assertion for the validation redirect flow.

**Change.** In `Inertia.Net.Testing`: `HttpResponseMessage.GetXsrfToken()` and an `HttpRequestMessage.WithXsrfToken(token)` (or a
`WithXsrfToken(HttpResponseMessage pageResponse)` overload) that follow the cookie name option; `AssertableInertia.HasErrors()`,
`HasError("email")`, `HasError("email", "message")`, `HasErrorBag("bag")` on top of the existing `Has`, following the shipped
`HasLive` and `HasScroll` assertion shapes. Reuse the existing `Status(int)`, non-success page parsing and pagination reload
helpers; do not recreate them. Keep the
package free of a reference to `Inertia.Net`. Replace the hand-rolled cookie slicing in the repository's tests with the helper.
README Testing section: one paragraph and one example for each.

**Tests.** `tests/Inertia.Net.Testing.Tests` for each helper against `FakeServer`.

### 2.3 Validation key normalization is inconsistent across paths (M, Sol high)

**Problem.** Source-generated errors use the HTTP JSON naming policy per segment; a handler-returned `ValidationProblem` is passed
to `Back()` without `ToClientKeys` (`src/Inertia.Net/InertiaValidation.cs:88`), so `Email` vs `email` depends on where the error
came from; MVC reads the naming policy from MVC `JsonOptions` while props use HTTP `JsonOptions`
(`src/Inertia.Net.Mvc/InertiaModelStateFilter.cs:44`). FastEndpoints already supplies request JSON schema, but the shared
normalizer only matches serialized property names or naming-policy names, so it cannot reliably map CLR names to
`[JsonPropertyName]` aliases; FluentValidation and
`IValidator<T>` have no Minimal API hook and the README does not say so.

**Change.**
1. Run `ToClientKeys` on handler-returned errors too (`:88`), with the schema from the endpoint's body parameter type when it can
   be found, so `[JsonPropertyName]` members map correctly where an AOT-safe CLR-to-JSON member mapping exists. Apply the same
   schema-aware normalization to source-generated failures and precognition. A supplied `JsonTypeInfo` alone does not expose
   CLR names reliably under source generation: do not assume `JsonPropertyInfo.AttributeProvider` exists, or introduce runtime
   reflection that causes trimming/AOT warnings. Document the remaining limitation precisely. Ensure a null naming policy does
   not bypass supplied schema normalization, and already-normalized paths remain unchanged.
2. MVC: resolve the naming policy from the same `JsonSerializerOptions` the page writer uses (`InertiaPageWriter.SerializerOptions`)
   and drop the "keep both aligned" note from `README.md`.
3. README: a short "Which validation libraries work where" table (DataAnnotations source-gen, `ValidationProblem` results,
   ModelState, FastEndpoints/FluentValidation), and a `WithErrors` example for a manual `IValidator<T>` call in Minimal API.

**Tests.** Extend the validation tests in `tests/Inertia.Net.IntegrationTests` (Minimal API, MVC, FastEndpoints) so the same
model with the same failure produces the same client keys on all three paths, including a `[JsonPropertyName]` member and a nested
collection index. Cover a null naming policy with renamed members, dictionary keys whose spelling must be preserved,
already-normalized aliases, duplicate normalized errors and precognition validate-only paths. Preserve the existing HTTP
binding behavior while unifying client error names. See [validation and testing reassessment](review-fixes-validation-review-2026-11.md).

### 2.4 Global `using static Inertia.Net.Inertia` collisions (S, Sol medium)

**Problem.** `src/Inertia.Net/buildTransitive/Inertia.Net.props` puts `Location`, `Prop`, `Merge`, `Scroll`, `Optional`,
`Always`, `Defer`, `Once` into scope in every consuming file. An app with a `Location` entity gets "'Location' is a type but is
used like a variable". `README.md:182-186` only explains why `Inertia.Render` does not compile.

**Change.** README: a "Name collisions" subsection next to that callout listing the imported names, the fully qualified escape
hatch (`Inertia.Net.Inertia.Location(...)`) and the `<InertiaNetImplicitUsings>false</InertiaNetImplicitUsings>` opt-out with the
explicit `using static` line. Consider (do not implement without a decision) a `global using Pages = Inertia.Net.Inertia;` alias
in the props so `Pages.Location(...)` is always available.

### 2.5 `InertiaProp` fluent API consistency (S, Sol medium)

**Problem.** `Fresh()` and `WithGroup()` are no-ops unless the prop is already `Once`/`Defer`, while `Until()`/`As()` imply
`Once` (`src/Inertia.Net/InertiaProp.cs:78,177`); `MatchOn(params)` replaces but `Append(matchOn:)`/`Prepend(matchOn:)`
accumulate (`:107` vs `:192,201`); `Append()` after `Prepend()` silently flips. Fluent methods mutate in place, so a prop kept in
a static field and `.Once()`'d in a handler leaks across requests.

**Decision (maintainer, 2026-11-01): approved.** 1.6.0 added two more data points that fix the convention: `Throttle()` throws
unless `Live()` was called first (`src/Inertia.Net/InertiaProp.cs:243-247`), and `Live()` on a merge prop throws (`:214`). So the
rule is: **a modifier implies its kind when there is exactly one obvious kind; it throws when there is not; it never silently
no-ops.** Apply it: `Fresh()` implies `Once` (as `Until`/`As` already do) and `WithGroup()` implies `Defer`; `Throttle()` stays as
it is (an event is needed, so throwing is right); `MatchOn` accumulates like `Append(matchOn:)`/`Prepend(matchOn:)`; `Append`
after `Prepend` at root (or the reverse) throws `InvalidOperationException` naming both calls, like the existing live/merge
message. Add one sentence to the `InertiaProp` class docs: do not share instances across requests. Behaviour changes are made
outright with a `CHANGELOG.md` entry; the docs describe only the new rules (phase 5).

**Tests.** `tests/Inertia.Net.Tests` prop tests for each rule, including the thrown message; the existing `LivePropsTests` and
`PropsWriterTests` for live/merge must keep passing unchanged.

### 2.6 Startup warning for DevTools recording with no reader (S, Sol medium)

**Problem.** `InertiaDevToolsOptions.Enabled = true` outside Development with `Authorize` null records every request and adds
`X-Inertia-Devtools-*` headers to every response, yet the read API always answers 404 (`src/Inertia.Net/InertiaDevTools.cs:271-272`).

**Change.** In `DevToolsRecorder`'s constructor, log a warning when `_enabled && !_development && _options.Authorize is null`
naming the option to set. Do not change behaviour.

### 2.7 Live props and the SignalR transport (added 2026-11-04 after reviewing 1.6.0)

The 1.6.0 live-props work is sound: deny-by-default channel authorizer, `Origin` check on negotiate and the WebSocket handshake,
antiforgery exemption scoped to the hub endpoint, `CloseOnAuthenticationExpiration`, client-supplied `X-Socket-Id` used only for
`GroupExcept`, resubscribe on reconnect in the template transport, Native AOT covered by the smoke test. Four items remain, none
blocking.

#### 2.7.1 Bound subscriptions per connection and channel name length (S, Sol medium) — security hardening

`InertiaLiveHub.Subscribe` (`src/Inertia.Net.Live.SignalR/InertiaLiveHub.cs:20-39`) accepts any `name` and adds the connection
to a SignalR group per call with no cap. One connection can create an unbounded number of groups (memory in the hub lifetime
manager until it disconnects), and public channels need no authorization to do so. Add `InertiaLiveSignalROptions.MaxSubscriptionsPerConnection`
(default 64) and `MaxChannelNameLength` (default 200), tracked per connection in `Context.Items`; `Subscribe` returns `false` and
logs at Debug beyond either. Validate both positive at startup. Mirror the limits in `README.md` (the SignalR package) and
`content/docs/live-props.mdx`.

**Tests.** Hub unit tests (there is a `LivePropsTests.cs`; add `InertiaLiveHubTests.cs` in the SignalR test project or the
integration tests): the 65th subscribe is refused, an over-long name is refused, unsubscribe frees a slot.

#### 2.7.2 Origin check behind a TLS-terminating proxy (S, Sol medium) — DX

`LiveOrigin.IsAllowed` (`src/Inertia.Net.Live.SignalR/LiveOrigin.cs:11-36`) compares the `Origin` scheme with `request.Scheme`.
Behind a proxy that terminates TLS and does not forward the scheme, every browser connection gets 403 with no hint. Log once at
Warning on the first refusal where the origin is `https`, the request scheme is `http` and the hosts match, naming
`UseForwardedHeaders()` and `AllowedOrigins`. Add the case to `troubleshooting.mdx` ("live props never connect: 403 on
negotiate") and to `deploying.mdx` next to the existing proxy guidance (`CookieSecurePolicy.Always`).

**Tests.** `LiveOriginTests`: the mismatch case is refused and the warning is logged once.

#### 2.7.3 Document authorization revocation (S, Sol medium) — docs only

Channel authorization runs at subscribe time; a connection removed from a resource (an order reassigned, a membership revoked)
stays in the group until it disconnects or its authentication expires. This is the usual transport model, but `live-props.mdx` and
the package README should say so and show the two mitigations: short authentication lifetimes with `CloseOnAuthenticationExpiration`
(already on), and publishing a "removed" event the page handles by navigating away. Do not add a kick API in this plan.

#### 2.7.4 Trim and bound `X-Socket-Id` (S, Sol medium) — hardening

`InertiaRequest.SocketId` (`src/Inertia.Net/InertiaRequest.cs:91,128`) is trimmed but not length-bounded; it ends up in
`GroupExcept(key, [except])`. Bound it to 128 characters (SignalR connection ids are 22) and treat longer values as null. One
test in `InertiaRequestTests`.

## Phase 3: performance

### 3.1 Non-string dictionary keys serialize per key per render (M, Sol high)

**Revalidated scope.** `DictionaryKeys` creates one probe dictionary, pooled buffer and JSON writer per dictionary walk,
outside the key loop; it serializes and re-reads once per key. The earlier per-key construction claim was incorrect. Measure
the per-key serialization cost before claiming it dominates rendering. The `CreateObject == null` fallback to `Convert.ToString` (`:835`) diverges from System.Text.Json for
`DateTime`/`DateTimeOffset`, enums with naming policies and `bool` keys.

**Evidence.** See [source reassessment](review-fixes-research-2026-11.md#performance-and-packaging-reassessment-against-v162).

**Change.** Keep the existing string-key path and consider direct formatting for `int`, `long` and `Guid` only when the active
key converter is proven to be the built-in converter. Preserve custom converters and source-generated metadata. Keep enums on
the serializer path unless an exact equivalent is demonstrated: enum dictionary names involve naming policies, flags, undefined
values and `[JsonStringEnumMemberName]`, even with the built-in converter. The existing buffer/writer is already reused across
keys; do not add an equivalent cache and call it a saving. Correct the `CreateObject == null` fallback using an AOT-safe approach
with regression evidence. Compare emitted property names against `JsonSerializer` for every optimized key type, custom
converters, enum cases and dictionaries whose metadata cannot construct a probe.

**Verify.** A benchmark row in `tests/Inertia.Net.Benchmarks` (`Dictionary<Guid, Row>` with 500 entries) before and after, reported
in `RESULTS.md` format.

### 3.2 `PreserveBigIntegers` allocations and `ValueSpan` misuse (S, Sol medium)

**Problem.** `BigIntegerConverters.WriteElement` (`src/Inertia.Net/BigIntegerConverters.cs:45-46`) calls `GetRawText()` and
`BigInteger.TryParse` for every number. `Converter<T>.Read`/`ReadAsPropertyName` (`:112,129`) use `reader.ValueSpan`, which is
empty when `HasValueSequence` is true (multi-segment input), so a chunked body parses wrongly.

**Change.** Use `TryGetInt64` to bypass allocation for exactly representable safe integers, retaining `element.WriteTo(writer)`
for unchanged numbers. Any `TryGetDouble` shortcut must preserve the original numeric token: never re-emit rounded doubles or
classify a large integer from its rounded value. Keep the raw-text/`BigInteger` fallback for unsafe integer tokens. Decimal and
exponent tokens currently remain ordinary JSON numbers; preserve that behavior. In `Read`, handle `HasValueSequence` explicitly
(e.g. copy its sequence only on that path). Property-name reads must also account for escaped names; do not assume a numeric
`TryGetInt64` call works on a property-name token.

**Tests.** Big-integer tests: multi-segment values and dictionary property names round-trip; safe/unsafe boundaries on both
sides of zero, `UInt64`/128-bit/`BigInteger` values, decimal/exponent tokens and escaped numeric property names retain exact
behavior. The allocation probe must exercise DOM numbers; unrelated ordinary prop rows cannot establish this saving.

### 3.3 Parallel loader failure reporting and cancellation (S, Sol medium)

**Problem.** `DrainAsync` (`src/Inertia.Net/PropsPlanner.cs:123-124`) observes and discards secondary exceptions; only
`FirstFailure()` is thrown. Sequential loaders receive `RequestAborted` rather than the planner's linked token
(`src/Inertia.Net/PropsWriter.cs:651-653`), so a parallel failure cannot interrupt a slow sequential loader.

**Change.** Log discarded failures at Warning with the prop path (an `ILogger` is already available on `InertiaPageWriter`), or
attach them as `AggregateException` inner exceptions to the thrown one. Pass `_planner?.Token ?? _cancellationToken` to
sequential loaders when a planner exists.

**Tests.** `tests/Inertia.Net.Tests/ParallelPropsTests` (or equivalent): two failing parallel loaders log both; a parallel failure
cancels a sequential loader's token.

### 3.4 Small per-render allocations (S, Sol medium)

Batch the following, each measured with the allocation probe before and after; drop any that do not move the number:
- `[.. prependPaths ?? [], wrapper]` per scroll prop (`src/Inertia.Net/PropsWriter.cs:793,797`): pass the wrapper as a suffix.
- `PropertyValue` boxed per custom-converter property (`src/Inertia.Net/InertiaPageWriter.cs:390`).
- `TaskCompletionSource` per `TryBeginFill` attempt including waiter loops (`src/Inertia.Net/InertiaCache.cs:265`).
- Planned cache hits do two store lookups (`src/Inertia.Net/PropsWriter.cs:229` and `:445`).
- `_memberOptions` already shares converter-free number-handling options, and intentionally keys custom converters by instance.
  Arbitrary converters expose no general settings equality contract; do not collapse them by type. Keep this item unchanged
  unless a narrower safe sharing case is demonstrated with configured converters of the same type and a measurable saving.

Each candidate needs a workload that executes it (scroll metadata, converter-bearing typed properties, fill contention, planned
cache hits, respectively). Preserve converter contracts and duplicate-prop behavior. Record unchanged or rejected candidates
with their measurements rather than implementing them to satisfy the list.

### 3.5 Monotonic circuit-breaker clock (S, Sol medium)

`SsrCircuitBreaker` uses UTC deadlines; store an opening timestamp and compare `GetElapsedTime` with `BreakDuration` so wall
clock adjustments do not change the break. Prefer elapsed comparisons over adding timestamp units to avoid introducing an
addition overflow. Define any necessary bound from the actual implementation and provider frequency; do not impose an arbitrary
timer limit on a duration that does not use a timer. Existing tests already call `FakeTimeProvider.Advance`; add independent
wall-clock jump coverage and boundary-duration cases. The validator currently accepts every positive `TimeSpan`, including
`TimeSpan.MaxValue`; reproduce any rejected-overflow case before deciding its new validation bound.

## Phase 4: packaging and the FastEndpoints rename

### 4.1 Packaging hygiene (S, Sol medium)

- Keep package validation enabled, but do not add an old-release compatibility baseline that blocks the approved API removals.
  Add `Microsoft.CodeAnalysis.PublicApiAnalyzers` with API inventories reflecting the final `1.6.3` surface; remove superseded
  signatures from those inventories rather than retaining compatibility shims. Set
  `AnalysisLevel=latest-recommended` and `EnforceCodeStyleInBuild=true`, fixing what they flag. Apply API inventories to all
  shipped library packages, including EntityFrameworkCore and SignalR, without leaking the analyzer as a runtime dependency.
  Inventory generation and recommended/style analysis can affect the whole solution; measure diagnostics before estimating
  this as a small task, and avoid unrelated public API redesign to silence style warnings.
- Preserve the testing package's `Microsoft.AspNetCore.App` framework reference: shipped pagination helpers use
  `QueryHelpers.ParseQuery` and `QueryString.Create` in `InertiaPage.LoadScrollPageAsync`. It is no longer unused.
- Verify repeated `AddInertia` registration with a regression test before changing hosted-service registrations. The .NET 10
  `AddHostedService` overloads already call `TryAddEnumerable`; the claimed duplicate TypeScript writer is not established.
  Preserve the existing idempotent registration unless a concrete remaining duplicate is found.
- `dotnet pack Inertia.Net.slnx -c Release --no-build -warnaserror -o artifacts` must stay clean.

### 4.2 FastEndpoints `UseInertia` rename (S, Sol medium)

**Decision (maintainer, 2026-11-01): approved; 2026-11-04: rename outright, no forwarder.**
`src/Inertia.Net.FastEndpoints/InertiaFastEndpointsConfig.cs:21`: the `Config` extension shares its name with the middleware
and overwrites `Endpoints.Configurator`.

**Change.** Rename it to `ConfigureInertia(this Config config, ...)` and **delete** `UseInertia` on `Config`. The new method
chains any previously set `Endpoints.Configurator` instead of replacing it (call the previous one first, then apply Inertia's).
Update every caller: the FastEndpoints sample, the template variant, the FastEndpoints integration host in
`tests/Inertia.Net.IntegrationTests`, the AOT smoke app if it uses it, the package README, `README.md` and the docs site
(phase 5). Grep the repository for `UseInertia(` on a `Config` receiver before closing the task; `-warnaserror` catches the rest.
`CHANGELOG.md` entry under Changed.

**Tests.** `tests/Inertia.Net.IntegrationTests` FastEndpoints host: a configurator set before `ConfigureInertia()` still runs,
and the existing FastEndpoints conformance tests pass unchanged against the renamed call.

## Phase 5: `README.md`, package READMEs and the documentation site (M, Sol medium; Terra medium for the rendered check)

Every code change in phases 1 to 4 is reflected in three places, in this order: the docs site page (the full version), then
`README.md` (the short version that links to the page), then the package README where the package is affected. Package-manager examples treat **npm, Bun, pnpm and Yarn as equal choices**: show equivalent install/run commands where useful
(e.g. a compact table or tabs), with no preferred/default manager in prose. Keep repository verification commands and existing
lockfiles reproducible; do not add four lockfiles to each sample.

No page, section
or callout is written as "new in 1.6.3", "changed in", or a migration note: the docs describe the library as it is after this
plan, as if the old behaviour never existed. `CHANGELOG.md` is the only place that records what changed.

**Docs site rules.** The site lives in `docs/Inertia.Net.Docs` (an Inertia.Net app; MDX pages in `content/docs` with `title`,
`description`, `section`, `order` frontmatter; `bun run generate:docs` regenerates the navigation, search index and
`wwwroot/llms.txt`/`llms-full.txt`, which must be committed with the content). The site was redesigned on 2026-11-03: **read the
repository `DESIGN.md` before touching any page or component**, and do not add runtime dependencies or change tokens. Use
`content/docs/live-props.mdx` (added in 1.6.0) as the format reference: it is the newest page and shows the house use of
`<Tabs>`/`<Tab>` for the React/Vue/Svelte switch (the choice persists across tab groups), `<Callout>` for the one thing a reader
must not get wrong, and `<Steps>` for setup. Follow the existing voice: short paragraphs, one runnable snippet per idea, "why"
before "how". Every snippet must compile against the samples (`samples/MinimalApi.React` unless the page is MVC or FastEndpoints
specific); copy snippets from a sample or test rather than writing them free-hand.

**README rules.** `README.md` stays the complete getting-started guide it is today, but a section that has a docs-site page is
the short version plus a link; do not keep two full copies. Code defaults quoted in the README must match the source after this
plan (a Terra worker greps every `Default` mention against `InertiaOptions.cs` and the option classes at the end of the phase).

### 5.0 `README.md` and package READMEs

| Location | Change |
|---|---|
| `README.md` top | Link to the new "First 10 minutes" page (5.1.0). |
| `README.md` "Using `Render` unqualified" callout (lines 182–186) | Add the "Name collisions" subsection from 2.4: the imported names, `Inertia.Net.Inertia.Location(...)`, the `<InertiaNetImplicitUsings>false</InertiaNetImplicitUsings>` opt-out. |
| `README.md` Minimal API quick start | Add `o.Pages.Directory` so a component typo gives the "did you mean" error. |
| `README.md` root template example | `@inertiaHead`/`@viteReactRefresh` in the same order as the template. |
| `README.md` Props | The fluent rules from 2.5, stated as the rules (`Fresh()` is a once prop, `WithGroup()` is a deferred prop, `MatchOn` adds keys, `Append` and `Prepend` at root are exclusive); the "do not share prop instances across requests" sentence; cached props under dot keys. |
| `README.md` Validation | The "which validation library works where" table and the `IValidator<T>` example from 2.3; the unified client-key rules; that `WithInertiaValidation()` throws at startup without `AddValidation()` (1.5). |
| `README.md` Precognition | Short version of 5.1 with the rate-limit policy snippet and a link to the page (2.1). |
| `README.md` Testing | XSRF helper and `HasError*` assertions with one full form test (2.2). |
| `README.md` SSR | `Ssr.ConnectTimeout` (1.3); stale-while-revalidate refreshes on a background worker, dropped-refresh metric (1.2); `Required` mode's exception message is safe to display (1.6); `Ssr.Cache.Duration` is for anonymous pages, prefer per-page `WithSsrCache`. |
| `README.md` Hosting / deployment | `AllowedHosts` stays configured (the version 409 echoes the request URL); `UseForwardedHeaders()` before the live hub behind a TLS-terminating proxy (2.7.2). |
| `README.md` DevTools | The no-reader warning (2.6). |
| `README.md` Live props | Subscription and channel-name limits (2.7.1); authorization is checked at subscribe time (2.7.3). |
| `README.md` Pagination, EF Core, error pages and previous URL | Preserve the shipped setup and examples, update interactions affected by this review, and link directly to feature guides. These are existing features, not upcoming announcements. |
| `README.md` FastEndpoints | `ConfigureInertia()` everywhere `UseInertia` on `Config` appeared (4.2). |
| `README.md` public API coverage | Mention `InertiaPropJsonConverter`, `InertiaSsrHealthCheck`, `InertiaCacheEntryKind`, `InertiaCacheEntryOptions`, `InertiaPagesOptions`, `InertiaPrecognition.IsPrecognitive`, `InertiaPrecognitionMetadata`. |
| `src/Inertia.Net/README.md`, `src/Inertia.Net.Mvc/README.md`, `src/Inertia.Net.FastEndpoints/README.md`, `src/Inertia.Net.Testing/README.md`, `src/Inertia.Net.Live.SignalR/README.md` | Lead with what the package does and the minimal setup; move the performance tables below; **delete the 1.4 migration notes**. FastEndpoints: `ConfigureInertia()`. Testing: the new helpers. SignalR: the limits and the proxy note. |
| `src/Inertia.Net.Templates/content/common/README.md` | Explain `JsonSerializerIsReflectionEnabledByDefault=false` and what to do for an anonymous-object prop. |
| `src/Inertia.Net.EntityFrameworkCore/README.md` | Include the shipped package in the documentation/version audit; keep offset/keyset examples aligned with the actual APIs and pagination guide. |
| `docs/migrating-to-1.1.md`, `docs/migrating-to-1.3.md`, `docs/migrating-to-1.4.md` and `docs/Inertia.Net.Docs/content/docs/migrating.mdx` | **Remove during implementation.** Move any still-useful setup or usage advice into the relevant feature guide first, then delete the pages and their inbound links. Preserve `CHANGELOG.md` history, repairing links to removed guides. |
| README and docs-site version announcements | Remove standalone "new in version", "what's new" and migration pages/sections, including any added since this plan was written. Fold useful examples into feature pages; remove navigation, search and generated exports for deleted pages. Keep installation package versions and protocol/client compatibility requirements where they are necessary. |
| `docs/Inertia.Net.Docs/content/docs/whats-new.mdx` | This page exists on the fetched baseline: move useful live/error-page/previous-URL/pagination/EF examples into their existing feature guides, then remove the page and its incoming links and generated entries. |

### 5.1.0 "First 10 minutes" page (new, `content/docs/first-10-minutes.mdx`, section `Get started`, before `quick-start.mdx`)

The five template commands, one handler, one form with validation, one test using the 2.2 helpers. Link it from the top of
`README.md` and from `introduction.mdx`.

### 5.1 Precognition guide (new page, `content/docs/precognition.mdx`, section `Loading data`)

Today precognition is a subsection of `validation-and-errors.mdx`. Give it its own page and link it from there, from
`quick-start.mdx` where the form is introduced, and from `troubleshooting.mdx` under a new "Precognition" heading.

Outline:

1. **What it is.** The client validates as the user types by sending the same `POST` with `Precognition: true`; the server
   validates and answers `204` or `422` without running the handler. Show the request/response pair as HTTP, then the
   `WithInertiaPrecognition()` one-liner, and say explicitly: *this replaces `WithInertiaValidation()`; do not use both.*
2. **Client setup** for React, Vue and Svelte using supported, source-verified APIs. Laravel Precognition has React/Vue wrappers
   but no official Svelte wrapper; never invent `laravel-precognition-svelte`. Prefer the shipped Inertia 3 form integration where
   verified, or show the core client integration for Svelte. Cover `useForm`, `form.validate('email')` where supported, showing errors,
   `form.validating`, `setValidationTimeout`. One `<Tabs>` group with a `<Tab>` per framework, as `live-props.mdx` does. Mention
   `Precognition-Validate-Only` and that Inertia.Net filters the 422 to the named fields, including nested paths.
3. **Tips** (each a short heading with a snippet):
   - *Validate on blur, not on change, for login and payment forms.* Fewer requests, no "invalid email" flashing mid-word.
   - *Raise the debounce* with `form.setValidationTimeout(…)` on forms with expensive rules (uniqueness checks).
   - *Rules that hit a database:* keep them cheap or move them to `Validate-Only` fields; precognition runs the full validator.
   - *Rate limiting (the login lockout problem).* Explain that precognition requests are real `POST`s to the endpoint and the
     rate limiter middleware runs before the filter, so every keystroke counts against the login bucket. Show the partitioned
     `AddPolicy("login", …)` from 2.1 that checks the endpoint's `InertiaPrecognitionMetadata` (not just the header), the
     `OnRejected` hook that sets `Precognition: true` on the 429, and the middleware order (`UseRouting`, `UseRateLimiter`,
     `UseInertia`, `UseInertiaAntiforgery`). A `<Callout>`: never `GetNoLimiter` for the precognitive branch; validation still
     costs CPU and may query the database. State why relaxing the limit is safe: with the metadata present the handler is
     guaranteed not to run, so no credential attempt happens.
   - *Antiforgery:* precognitive `POST`s are validated by `UseInertiaAntiforgery()` like any other; the client sends the token
     automatically, nothing to do.
   - *Authorization:* precognition runs after authorization middleware, so a protected form validates only for signed-in users.
   - *Testing a precognitive endpoint* with `Inertia.Net.Testing`: send the header, assert `204` + `Precognition-Success`, then
     assert the `422` body for a bad field (reuse the helpers from 2.2).
4. **Reference table** of headers (`Precognition`, `Precognition-Validate-Only`, `Precognition-Success`, `Vary: Precognition`) and
   status codes, and the public `InertiaPrecognition.IsPrecognitive(...)` and `InertiaPrecognitionMetadata` API from 2.1.

Add a precognitive login form with the partitioned rate-limit policy to `samples/MinimalApi.React` (it is the sample the docs
snippets are copied from), with a Playwright test that validation keeps working past the strict limit.

### 5.2 Pages to update for the other phases

| Page | Change |
|---|---|
| `validation-and-errors.mdx` | Link to the new precognition page; the "which validation library works where" table from 2.3; the `WithInertiaValidation()` startup error from 1.5; the unified client-key rules (indices, naming policy, `[JsonPropertyName]` status). |
| `testing.mdx` | XSRF helper and `HasError*` assertions from 2.2, with a full form test (page load, token, POST, redirect, errors). |
| `props.mdx` | Fluent rules from 2.5 (`Fresh` implies `Once`, `WithGroup` implies `Defer`, `MatchOn` accumulates, `Append`/`Prepend` conflict throws); "do not share prop instances across requests" callout; cached props with dot keys now cache (1.1). |
| `server-side-rendering.mdx` | `Ssr.ConnectTimeout` (1.3); stale-while-revalidate now refreshes on a background worker and the dropped-refresh metric (1.2); the cache-key note: global `Ssr.Cache.Duration` is for anonymous pages, prefer per-page `WithSsrCache` (S3). |
| `performance.mdx` | Updated benchmark rows for 3.1 and 3.4 in the `RESULTS.md` format; the `ParallelUsed`/DevTools process-wide latch note. |
| `configuration.mdx` | New options: `Ssr.ConnectTimeout`; the DevTools warning from 2.6; the `using static` collision subsection and the `<InertiaNetImplicitUsings>` opt-out from 2.4. |
| `installation.mdx`, `quick-start.mdx` | `o.Pages.Directory` in the Minimal API quick start; FastEndpoints `ConfigureInertia()` (4.2); `@inertiaHead`/`@viteReactRefresh` order matching the template. |
| `deploying.mdx` | `AllowedHosts` note (S5); SSR connect timeout and what `Required` mode's exception message now contains (1.6). |
| `troubleshooting.mdx` | New entries: "forms submit invalid data" (1.5), "419 in tests" (2.2), "login form locks users out while typing" (5.1), "'Location' is a type but is used like a variable" (2.4), "cached prop loader runs every request" (1.1). |
| Removed version/migration pages | Follow 5.0: put the current fluent rules, validation behaviour and `ConfigureInertia()` setup in their feature guides, then remove obsolete pages and links. |
| `devtools.mdx` | The no-reader warning (2.6). |
| `live-props.mdx`, `src/Inertia.Net.Live.SignalR/README.md` | Subscription and name limits (2.7.1); the proxy/origin case (2.7.2); the authorization-revocation paragraph (2.7.3). |
| `pagination.mdx`, `infinite-scroll.mdx` | Preserve core and EF pagination examples; explain affected cache/scroll/fluent behaviour using shipped APIs. Reuse the existing pagination benchmark results as the baseline. |
| `error-pages.mdx`, `redirects-and-history.mdx` | Preserve error-page registration and remembered-URL setup; verify interaction with validation redirects, SSR failures and rate-limit rejection examples. Move useful content here from the removed release-summary page. |
| `ai-agents.mdx` | Regenerated `llms.txt`/`llms-full.txt` pick up the new page automatically; check that the precognition page is present and that the rate-limiting snippet survives the plain-text export. |

### 5.3 Site checks (Terra medium)

Run `bun install && bun run build && dotnet run -c Release` in `docs/Inertia.Net.Docs`, open every changed page, and attach
screenshots of: the new precognition page (top and the rate-limiting section), the testing page's new example, the troubleshooting
entries, and the FastEndpoints setup. Confirm search finds "precognition" and "rate limit", code highlighting works for the C# and
TSX snippets, and `GET /llms.txt` lists the new page. Open the three tabbed client snippets and confirm each renders.
Check README links, navigation, search results and both llms exports for references to deleted version/migration pages;
regenerate rather than hand-editing generated files. Verify the examples use the replacement APIs throughout, with no obsolete
forwarders or compatibility instructions. The application middleware remains `app.UseInertia()`; only the FastEndpoints
`Config` extension is renamed.

## Phase 6: end-to-end verification (Terra high)

After phases 1 to 5 are merged, on Linux with Node and Playwright installed:

1. Build the three samples and run the Playwright suite as `.github/workflows/ci.yml` and `samples/README.md` describe. Attach the
   report and screenshots of each sample's first page and one form submission with a validation error.
2. In the MinimalApi.React sample's precognitive login form (added in 5.1), type fast enough to exceed the strict bucket and
   screenshot that validation keeps working, then submit past the limit and screenshot the 429 (no `Precognition` header on a
   real submit; `Precognition: true` present on a rate-limited validation). Confirm the docs snippets match the sample code
   byte for byte.
3. Enable `Ssr.Cache` with `StaleFor` in one sample, make the renderer slow (an artificial delay in the SSR entry), and record a
   browser performance trace showing that the request after a stale hit is not blocked on the same connection (1.2).
4. Run `bash eng/aot-smoke.sh` and report the result.
5. Fetch the current `inertiajs/inertia` 3.x client `requestParams.ts`/`response.ts` and the DevTools protocol page, and note any
   header or behaviour drift from `docs/protocol.md` as a follow-up list (no changes in this pass).

Report every check run, its result, and anything that could not be run and why.

## Phase 7: streaming the page JSON (separate follow-up, excluded from 1.6.3)

Neither the spike nor its implementation is a release gate or deliverable for 1.6.3. Start on a separate branch after this
release; resolve its measurement and design questions there before promising an API.

[plan-performance-and-dx.md](plan-performance-and-dx.md) §2.3 decided against streaming because the buffer is what gives
`Content-Length` and guarantees a failing loader never produces a half-written `200`, and the copy into Kestrel's pipe is a memcpy.
The maintainer wants it reconsidered. Do it in two steps: a measured spike that either justifies the work or closes the question
with numbers, then, only if the gate passes, an opt-in implementation that keeps every current guarantee for the default path.

### 7.1 Spike: where would streaming actually help? (M, Sol high; Terra medium for browser timings)

Today `InertiaResult` renders the page through `PropsWriter` into a `PooledBufferWriter`, then copies it to the response
(`src/Inertia.Net/InertiaResult.cs`, `src/Inertia.Net/InertiaPageWriter.cs`, `src/Inertia.Net/PooledBufferWriter.cs`). The
buffer is also what SSR posts (`SsrGateway`), what the SSR cache hashes, what DevTools captures (`DevToolsCapture.PageJson`),
and what `Ssr.MaxPageBytes` checks. Loaders resolve during the walk, so a prop late in the object can fail after earlier bytes
exist.

Two candidate designs, both to be prototyped on a branch (not merged) with the existing benchmark harness:

- **A. Serialization-only streaming.** Resolve every loader first (the planner already resolves parallel props up front; extend
  it to pre-resolve sequential ones into the plan), then serialize straight to `Response.BodyWriter`, flushing every N KiB. The
  failure guarantee is intact (nothing is written until all loaders succeed); what is saved is the pooled buffer and the memcpy,
  so the gain is memory on large pages, not latency.
- **B. Streaming as loaders resolve.** Serialize to `Response.BodyWriter` while walking; a loader failure after the first flush
  aborts the connection (`HttpContext.Abort()`) and logs, so the client sees a network error rather than a truncated `200`. This
  is the only design that improves time-to-first-byte for pages with slow sequential loaders, and it changes the failure contract.

Measure, in `tests/Inertia.Net.Benchmarks` with new rows in the `RESULTS.md` format (paired, alternating-order, allocation and
latency):

| Scenario | What to compare |
|---|---|
| JSON, 20 mixed props (the existing control) | Must not regress in either design. |
| JSON, 1 MiB page (large array of DTOs) | Allocation and time per op for buffered vs A vs B. |
| JSON, three sequential loaders of 100 ms each | Time to first byte and total time for buffered vs B (A is expected to match buffered). |
| HTML initial visit, SSR off, 20 props | Whether streaming the `data-page` script into the template helps at all. |
| Parallel props, 4 loaders | B must not change the resolved order or the planner's cancellation semantics. |

**Terra:** against the MinimalApi.React sample with an artificial 300 ms loader, record a browser performance trace for
buffered vs B and report TTFB and `DOMContentLoaded`; a screenshot of the waterfall for each.

**Gate (write the numbers down either way).** Implement 7.2 only if **both** hold: (1) design B improves TTFB on the slow-loader row
by at least 30 % in the benchmark and visibly in the browser trace, or design A cuts allocation on the 1 MiB row by at least 50 %;
and (2) the 20-prop control moves by less than 2 % in time and 0 B in allocation. If the gate fails, add a "Streaming (measured
2026-11)" paragraph to `docs/Inertia.Net.Docs/content/docs/performance.mdx` and the plan's "decided against" list with the numbers,
and stop. A design A allocation win alone justifies investigating A, not shipping B; resolve that choice before 7.2.

### 7.2 Opt-in implementation (L, Sol high)

Only if 7.1 passes. Design B is the one worth shipping (A alone is a memory optimization that can be done inside the buffer
instead); keep it strictly opt-in and keep the buffered path byte-for-byte unchanged.

- API: `InertiaResult.WithStreaming()` per response and `InertiaOptions.Props.Streaming` (`Off` default, `On`) as the global
  default, with `WithStreaming(false)` to opt a page back out. Document the changed failure contract on both.
- Applies only to the **JSON** response of an Inertia request. It is silently off (buffered) when any of these need the full page
  up front: SSR is enabled for the request, an SSR cache duration is set, DevTools is recording, a `Ssr.MaxPageBytes` check would
  apply, or the response has already started.
- Serialize through the existing `PropsWriter` with `Utf8JsonWriter` over `Response.BodyWriter`; flush after each top-level prop
  (and every 16 KiB inside a large one). Set `X-Inertia`, `Content-Type` and `Vary` before the first flush (they already go
  through `OnStarting`); no `Content-Length`, so Kestrel uses chunked encoding.
- Failure after the first flush: log at Error with the prop path, record the existing render failure metric with a `streamed`
  tag, and `HttpContext.Abort()`. Failure before the first flush: the normal `500`, as today.
- `ResponseWriteTracker` in `InertiaMiddleware` must treat the stream as "written" so the empty-200 redirect never triggers.
- Protocol note in `docs/protocol.md`: streaming is an adapter choice, not a protocol change; the emitted JSON is identical.
- The trailing page fields (`sharedProps`, `mergeProps`, `deferredProps`, `onceProps`, `liveProps` and the rest) are collected
  during the walk and written after `props`; streaming does not change that, but the 1.6.0 `liveProps` collection rules
  (`docs/protocol.md` deviation 13) must be covered by the byte-identity tests.
- Branch off the current implementation after this plan's phase 3; pagination and live props are already in the baseline.
  Include numbered/cursor `Scroll` pages and live metadata in streamed/buffered equivalence checks, and verify that error-page
  middleware does not attempt to render a replacement after streaming has started.

**Tests.** `tests/Inertia.Net.Tests`: identical bytes for buffered and streamed renders across the conformance fixtures; a loader
that fails after the first flush aborts the response (assert the exception type from the test server and the log entry); every
"silently off" condition falls back to buffered; the empty-200 redirect does not fire. `tests/Inertia.Net.IntegrationTests`:
the Inertia client-facing headers are present on the first chunk; Minimal API, MVC and FastEndpoints all honour the option.

**Docs.** `performance.mdx` gets a "Streaming" section with the measured numbers, when to turn it on (slow sequential loaders,
very large pages, no SSR), when not to (anything that needs `Content-Length`, pages behind a buffering proxy), and the failure
behaviour. `configuration.mdx` lists the option; `troubleshooting.mdx` gets "page request fails with a network error instead of
a 500".

## Delegation map for the Sol coordinator

Same conventions as the pagination and back-and-error-pages plans: the coordinator owns the branch, order, gates and report;
every worker prompt carries the rules paragraph at the top of this plan, the task text verbatim, the allowed files, and "report
the commands you ran and their exit status".

| Worker | Profile | Tasks | Inputs | Output |
|---|---|---|---|---|
| Coordinator (Sol high) | code, high | 1.1, 1.2, 1.4, 2.3, 3.1, 7.1/7.2; review of every other diff | this plan, `docs/protocol.md`, `AGENTS.md` | failing-test commit then fix commit per task; the phase PRs |
| Code A (Sol 6.1) | code, medium; high for complex changes | 1.3, 1.5, 1.6, 2.1 (code), 2.2, 2.4, 2.5, 2.6, 2.7.1–2.7.4 | this plan, Research memo for 2.1 | same commit shape; paste the integration test output in each PR |
| Code B (Sol 6.1) | code, medium; high for complex changes | 3.2, 3.3, 3.4, 3.5, 4.1, 4.2 | this plan | same; 3.x tasks attach the allocation probe before/after |
| Benchmark (Terra) | code + benchmark running | the `RESULTS.md` rows for 1.1, 1.2, 3.1, 3.4 and all of 7.1 | `tests/Inertia.Net.Benchmarks/RESULTS.md` method section | paired results in the existing format; `summary.json` |
| Research (Terra) | web search + fetch | Before 2.1: current `laravel-precognition` client (`validateTimeout` default; behaviour when the `Precognition` response header is missing; what it does with a 429). Before 7.1: Kestrel's `OnCompleted` ordering on HTTP/1.1 keep-alive (quote `Http1Connection`/`HttpProtocol` source) so the 1.2 premise is documented, not assumed | the URLs | memo with quotes; a contradiction stops the affected task until the coordinator amends this plan |
| Browser / e2e (Terra) | browser, Playwright | 5.1 sample login form + spec, 5.3, phase 6 | `samples/README.md`, `.github/workflows/ci.yml` | spec files, Playwright report, screenshots, traces |
| Docs (Terra) | code, medium | 5.0 (README updates and version-page cleanup), 5.1.0, 5.1 (page), 5.2 | `DESIGN.md`, `live-props.mdx` as the format reference, README voice rules | diffs; `bun run generate:docs` output committed |

**Gates.** Phase 1 must be green (build `-warnaserror`, tests, pack, AOT smoke, three-sample Playwright) before phase 2 starts
on the same release branch; the same bar applies between every later phase. Research memos accepted before 2.1 and 7.1 start. The 7.1 gate (numbers written down)
decides 7.2. Each PR opens only after build, tests, AOT smoke, e2e and docs regeneration are green.

**Stop conditions** (escalate instead of improvising): a 1.1 fix that changes deviation 7 output for non-cached props; a 1.2
design that needs a new package dependency; any change to the wire format in `docs/protocol.md` §1; the AOT smoke reports a
warning; a pagination or live-props test starts failing in a phase that should not touch them; the 7.1 gate is borderline (within
5 % of a threshold) — report and let the maintainer decide.

## Definition of done

- Every regression required by phases 1–6 exists and passes on the applicable Minimal API, MVC and FastEndpoints paths;
  phase 7 tests belong to its separate follow-up. Preserve existing coverage in
  `LivePropsTests`, `PropsWriterTests`, `tests/Inertia.Net.Tests/Pagination`, `PreviousUrlTests`, `InertiaErrorPagesTests`,
  `tests/Inertia.Net.IntegrationTests/ErrorPagesTests`, `tests/Inertia.Net.Testing.Tests/PaginationTests`, and
  `tests/Inertia.Net.EntityFrameworkCore.Tests/PaginationTests`. Run the shipped `live.spec.ts`, `pagination.spec.ts`,
  `back.spec.ts` and `errors.spec.ts` in the appropriate sample projects.
- `dotnet build … -warnaserror`, `dotnet test`, `dotnet pack … -warnaserror`, `bash eng/aot-smoke.sh`, sample build + Playwright
  (three samples), docs site build: run clean or the report says which could not run and why.
- Docs items done; `generated-nav.json`, `generated-search.json`, `llms.txt` and `llms-full.txt` regenerated and committed.
- `CHANGELOG.md` has one `[1.6.3]` section covering phases 1 to 5 (Fixed, Changed, Added, Performance, Documentation); the version
  in `src/Directory.Build.props` is bumped to `1.6.3` once.
- Include `Inertia.Net.EntityFrameworkCore` and `Inertia.Net.Live.SignalR` in release packaging; update template and docs-site
  package pins that follow the release. Preserve historical version references in the changelog and benchmark evidence.
- Root/package READMEs and Inertia.Net Docs describe the current APIs with runnable examples, including the precognition
  rate-limiting recipe. Version-announcement and migration pages are removed, useful content is retained in feature guides,
  and links/navigation/search/llms exports are clean. No `[Obsolete]` wrappers, old FastEndpoints `Config.UseInertia` method,
  or compatibility shims are introduced for the approved changes.
- Benchmark rows for 1.1, 1.2, 3.1 and retained 3.4 changes in `RESULTS.md` with the paired method and environment stated.
  Record evidence for rejected 3.4 candidates. Phase 7.1 measurements are excluded from 1.6.3.
- No assistant attribution in commits, PRs, code or docs.

## Out of scope for this plan

- In-process JavaScript SSR and `HybridCache` (decided against in [plan-performance-and-dx.md](plan-performance-and-dx.md)
  §2.3; in-process SSR would also break the Native AOT and trimming story).
- Streaming spike and implementation (phase 7): a separate post-release branch and decision.
- Reimplementing pagination, EF Core pagination, live props, previous-URL support or error pages: all are already shipped;
  integration and regression checks for them are part of this plan.
- A kick/revoke API for live channels (2.7.3 documents the model instead).
- 103 Early Hints (blocked on Kestrel).
- Renaming the `Inertia.Net.Inertia` static class (a breaking change; the collision docs in 2.4 are the mitigation for now).
