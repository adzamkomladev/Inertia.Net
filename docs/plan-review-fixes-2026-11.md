# Security, performance and developer-experience fixes (review, November 2026)

Status: open. This plan comes from a read-only review of 1.5.3 (`5d0fa8a`). Nothing here is implemented yet.

It is written to be executed by Codex. Each task names the files it touches, its size (S: hours, M: a day or two, L: more), how it
is verified, and which model should run it:

- **Sol high**: core protocol, cache, SSR and prop-writer changes where a wrong edit silently corrupts output.
- **Sol medium**: contained code changes, tests, docs and packaging.
- **Terra medium/high**: anything that needs a browser or the network: running the samples under Playwright, screenshots,
  fetching upstream docs or client source to confirm behaviour, checking the docs site.

Rules that apply to every task (from [AGENTS.md](../AGENTS.md)): keep changes small, reuse existing helpers, trace callers before
changing shared behaviour, preserve Native AOT and source-generated JSON, keep nullable annotations and public XML docs, add
regression coverage to the existing test projects, and run from the repository root:

```sh
dotnet build Inertia.Net.slnx -c Release -warnaserror
dotnet test --solution Inertia.Net.slnx -c Release --no-build
```

Report the checks actually run. Never claim an unrun check passed. No assistant attribution anywhere.

Work in phases, one pull request per phase (or per task where a task is M or larger). Write the failing regression test **before**
the fix in every correctness task so the test demonstrably catches the bug.

## Phase 1: correctness and security (ship first)

### 1.1 `Cache()` is bypassed when dot keys sit beneath the prop (M, Sol high)

**Problem.** `ToTraversable` (`src/Inertia.Net/InertiaPageWriter.cs:612-622`) wraps any eager, non-always, non-once, non-merge,
non-rescue prop in `LazyDotObject` without checking `Caching`. The `LazyDotObject` branch of `WriteValueAsync`
(`src/Inertia.Net/PropsWriter.cs:522-527`) then calls `ResolveAsync(lazy.Source)` without `cached: true`, so
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
reload that leaves the path out never runs the loader (deviation 7 still holds).

**Verify.** Build, tests, and `tests/Inertia.Net.Benchmarks` `--render-probe` to show no regression on the ordinary render rows.

### 1.2 Stale-while-revalidate refresh runs inside `Response.OnCompleted` (M, Sol high)

**Problem.** `ScheduleRefresh`/`OnCompleted` (`src/Inertia.Net/SsrGateway.cs:295-305`) registers `RefreshAsync` as an
`OnCompleted` callback. Kestrel awaits `OnCompleted` callbacks before reusing an HTTP/1.1 connection, so the whole re-render (slot
wait plus HTTP, up to `Ssr.Timeout`, with `CancellationToken.None` at `:318`) stalls the browser's next request on that keep-alive
connection. The same pattern should be checked for cached props (`PropRefreshes`, `src/Inertia.Net/PropsWriter.cs:1000-1040`).

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
marker, and the message does not contain the renderer URL.

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
   `GetNoLimiter`), the `OnRejected` hook that adds `Precognition: true` to the 429 so the client does not throw, and the
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
`HasError("email")`, `HasError("email", "message")`, `HasErrorBag("bag")` on top of the existing `Has`. Keep the package free of a
reference to `Inertia.Net`. Replace the hand-rolled cookie slicing in the repository's tests with the helper. README Testing
section: one paragraph and one example for each.

**Tests.** `tests/Inertia.Net.Testing.Tests` for each helper against `FakeServer`.

### 2.3 Validation key normalization is inconsistent across paths (M, Sol high)

**Problem.** Source-generated errors use the HTTP JSON naming policy per segment; a handler-returned `ValidationProblem` is passed
to `Back()` without `ToClientKeys` (`src/Inertia.Net/InertiaValidation.cs:88`), so `Email` vs `email` depends on where the error
came from; MVC reads the naming policy from MVC `JsonOptions` while props use HTTP `JsonOptions`
(`src/Inertia.Net.Mvc/InertiaModelStateFilter.cs:44`); `[JsonPropertyName]` is ignored everywhere; FluentValidation and
`IValidator<T>` have no Minimal API hook and the README does not say so.

**Change.**
1. Run `ToClientKeys` on handler-returned errors too (`:88`), with the schema from the endpoint's body parameter type when it can
   be found, so `[JsonPropertyName]` members map correctly where a schema exists. Document the remaining limitation precisely.
2. MVC: resolve the naming policy from the same `JsonSerializerOptions` the page writer uses (`InertiaPageWriter.SerializerOptions`)
   and drop the "keep both aligned" note from `README.md`.
3. README: a short "Which validation libraries work where" table (DataAnnotations source-gen, `ValidationProblem` results,
   ModelState, FastEndpoints/FluentValidation), and a `WithErrors` example for a manual `IValidator<T>` call in Minimal API.

**Tests.** Extend the validation tests in `tests/Inertia.Net.IntegrationTests` (Minimal API, MVC, FastEndpoints) so the same
model with the same failure produces the same client keys on all three paths, including a `[JsonPropertyName]` member and a nested
collection index.

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

**Decision (maintainer, 2026-11-01): approved.** Make `Fresh()` imply `Once` (as `Until`/`As` do) and `WithGroup()` imply
`Defer`; make `MatchOn` accumulate like `Append(matchOn:)`/`Prepend(matchOn:)`; throw `InvalidOperationException` when `Append`
follows `Prepend` at root or vice versa, naming both calls in the message. Add one sentence to the `InertiaProp` class docs: do not
share instances across requests. These are behaviour changes: ship under the next minor version with a `CHANGELOG.md` entry and a
short note in `docs/Inertia.Net.Docs/content/docs/migrating.mdx`.

**Tests.** `tests/Inertia.Net.Tests` prop tests for each rule, including the thrown message.

### 2.6 Startup warning for DevTools recording with no reader (S, Sol medium)

**Problem.** `InertiaDevToolsOptions.Enabled = true` outside Development with `Authorize` null records every request and adds
`X-Inertia-Devtools-*` headers to every response, yet the read API always answers 404 (`src/Inertia.Net/InertiaDevTools.cs:271-272`).

**Change.** In `DevToolsRecorder`'s constructor, log a warning when `_enabled && !_development && _options.Authorize is null`
naming the option to set. Do not change behaviour.

## Phase 3: performance

### 3.1 Non-string dictionary keys serialize per key per render (M, Sol high)

**Problem.** `DictionaryKeys` (`src/Inertia.Net/InertiaPageWriter.cs:826-850`) builds a probe dictionary, a `PooledBufferWriter`
and a `Utf8JsonWriter`, then serializes and re-reads **per key per render**. For `Dictionary<int|Guid|enum, T>` with hundreds of
entries this dominates. The `CreateObject == null` fallback to `Convert.ToString` (`:835`) diverges from System.Text.Json for
`DateTime`/`DateTimeOffset`, enums with naming policies and `bool` keys.

**Change.** Special-case `string`, `int`, `long`, `Guid` and enum keys without a custom converter (format as STJ does, honouring
`DictionaryKeyPolicy` for strings and enum naming); for everything else cache one probe `JsonTypeInfo` per dictionary type and reuse
one pooled writer across the keys of a render. Keep the exact STJ output; add tests that compare against `JsonSerializer` for each
key type including the fallback path.

**Verify.** A benchmark row in `tests/Inertia.Net.Benchmarks` (`Dictionary<Guid, Row>` with 500 entries) before and after, reported
in `RESULTS.md` format.

### 3.2 `PreserveBigIntegers` allocations and `ValueSpan` misuse (S, Sol medium)

**Problem.** `BigIntegerConverters.WriteElement` (`src/Inertia.Net/BigIntegerConverters.cs:45-46`) calls `GetRawText()` and
`BigInteger.TryParse` for every number. `Converter<T>.Read`/`ReadAsPropertyName` (`:112,129`) use `reader.ValueSpan`, which is
empty when `HasValueSequence` is true (multi-segment input), so a chunked body parses wrongly.

**Change.** Try `TryGetInt64`/`TryGetDouble` first and only fall back to `BigInteger` when the value does not fit or exceeds
2^53-1. In `Read`, use `reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan` (or `reader.TryGetInt64`).

**Tests.** `tests/Inertia.Net.Tests` big-integer tests: a multi-segment `ReadOnlySequence<byte>` input round-trips; allocation
probe shows the common case allocates nothing extra.

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
- `_memberOptions` creates one `JsonSerializerOptions` per `[JsonConverter]` attribute instance (`InertiaPageWriter.cs:331-345`):
  key by converter type and settings instead.

### 3.5 Monotonic circuit-breaker clock (S, Sol medium)

`src/Inertia.Net/SsrCircuitBreaker.cs:33,40,82` uses `GetUtcNow().UtcTicks`; use `GetTimestamp()`/`GetElapsedTime` like
`SsrGateway.cs:763`, and have the validator reject `BreakDuration` values that overflow. Tests already use a fake `TimeProvider`;
switch them to `Advance` on the timestamp.

## Phase 4: documentation, samples and packaging

### 4.1 README and package READMEs (M, Sol medium; Terra medium for the rendered check)

- A "First 10 minutes" page on the docs site (`content/docs/first-10-minutes.mdx`, section `Get started`, before
  `quick-start.mdx`) that is the five template commands, one handler, one form with validation, one test. Link it from the top of
  `README.md`. README sections that have a docs-site page should be the short version and link to the page; do not keep two
  full copies.
- Package READMEs (`src/*/README.md`) lead with what the package does and the minimal setup, moving the 1.4 migration notes and
  performance tables below.
- Mention the unmentioned public types: `InertiaPropJsonConverter`, `InertiaSsrHealthCheck`, `InertiaCacheEntryKind`,
  `InertiaCacheEntryOptions`, `InertiaPagesOptions`.
- SSR cache: state that `Ssr.Cache.Duration` global caching is for anonymous pages and that per-page `WithSsrCache` is the
  default recommendation, because the key includes the query string and any client can mint keys.
- Hosting: one line that `AllowedHosts` should stay configured because the version 409 echoes the request URL.
- Fix the `@inertiaHead`/`@viteReactRefresh` order so the template and the README example agree.
- Template README: explain `JsonSerializerIsReflectionEnabledByDefault=false` and what to do for an anonymous-object prop.
- Manual Minimal API quick start: include `o.Pages.Directory` so a component typo gives the "did you mean" error.

**Terra check.** Build and open the docs site (`docs/Inertia.Net.Docs`) and take screenshots of the new page and the changed
sections; confirm code highlighting and the `llms.txt` route still work.

### 4.2 Packaging hygiene (S, Sol medium)

- `src/Directory.Build.props`: add `PackageValidationBaselineVersion` = last released version so API breaks are caught; add
  `Microsoft.CodeAnalysis.PublicApiAnalyzers` with `PublicAPI.Shipped.txt`/`Unshipped.txt` per package; set
  `AnalysisLevel=latest-recommended` and `EnforceCodeStyleInBuild=true`, fixing what they flag.
- `src/Inertia.Net.Testing/Inertia.Net.Testing.csproj`: remove the unused `FrameworkReference` if nothing needs it.
- `AddInertia`: register the hosted services with `TryAddEnumerable` so calling it twice does not run `InertiaTypeScriptWriter`
  twice.
- `dotnet pack Inertia.Net.slnx -c Release --no-build -warnaserror -o artifacts` must stay clean.

### 4.3 FastEndpoints `UseInertia` naming (S, Sol medium)

**Decision (maintainer, 2026-11-01): approved.** `src/Inertia.Net.FastEndpoints/InertiaFastEndpointsConfig.cs:21`: the
`Config` extension shares its name with the middleware and overwrites `Endpoints.Configurator`.

**Change.** Add `ConfigureInertia(this Config config, ...)` with the same behaviour, chaining any previously set
`Endpoints.Configurator` instead of replacing it (call the previous one first, then apply Inertia's). Keep `UseInertia` on `Config`
as a thin `[Obsolete("Use ConfigureInertia(); UseInertia is the middleware on IApplicationBuilder.")]` forwarder for one minor
version, then remove it in the next major. Update the FastEndpoints sample, template variant, package README and
`docs/Inertia.Net.Docs/content/docs/installation.mdx`/`quick-start.mdx`. `CHANGELOG.md` and `migrating.mdx` entries.

**Tests.** `tests/Inertia.Net.IntegrationTests` FastEndpoints host: a configurator set before `ConfigureInertia()` still runs;
the obsolete forwarder produces the same endpoints.

## Phase 5: documentation site (M, Sol medium; Terra medium for the rendered check)

The docs site lives in `docs/Inertia.Net.Docs` (an Inertia.Net app; MDX pages in `content/docs` with `title`, `description`,
`section`, `order` frontmatter; `bun run build` regenerates the navigation, search index and `wwwroot/llms.txt`/`llms-full.txt`,
which must be committed with the content). `README.md` and the site must say the same thing: every item below has a README
counterpart in 4.1 or in the task that introduced it; write the site page first, then make the README section a shorter version
that links to it.

Follow the existing voice: short paragraphs, one runnable snippet per idea, "why" before "how", `<Callout>` for the one thing a
reader must not get wrong. Every snippet must compile against the samples (`samples/MinimalApi.React` unless the page is MVC or
FastEndpoints specific); copy snippets from a sample or test rather than writing them free-hand.

### 5.1 Precognition guide (new page, `content/docs/precognition.mdx`, section `Loading data`)

Today precognition is a subsection of `validation-and-errors.mdx`. Give it its own page and link it from there, from
`quick-start.mdx` where the form is introduced, and from `troubleshooting.mdx` under a new "Precognition" heading.

Outline:

1. **What it is.** The client validates as the user types by sending the same `POST` with `Precognition: true`; the server
   validates and answers `204` or `422` without running the handler. Show the request/response pair as HTTP, then the
   `WithInertiaPrecognition()` one-liner, and say explicitly: *this replaces `WithInertiaValidation()`; do not use both.*
2. **Client setup** for React, Vue and Svelte with `laravel-precognition-*`: `useForm`, `form.validate('email')`, showing errors,
   `form.validating`, `setValidationTimeout`. One tabbed snippet per framework (reuse the site's tab component if there is one,
   otherwise three short blocks). Mention `Precognition-Validate-Only` and that Inertia.Net filters the 422 to the named fields,
   including nested paths.
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
| `installation.mdx`, `quick-start.mdx` | `o.Pages.Directory` in the Minimal API quick start; FastEndpoints `ConfigureInertia()` (4.3); `@inertiaHead`/`@viteReactRefresh` order matching the template. |
| `deploying.mdx` | `AllowedHosts` note (S5); SSR connect timeout and what `Required` mode's exception message now contains (1.6). |
| `troubleshooting.mdx` | New entries: "forms submit invalid data" (1.5), "419 in tests" (2.2), "login form locks users out while typing" (5.1), "'Location' is a type but is used like a variable" (2.4), "cached prop loader runs every request" (1.1). |
| `migrating.mdx` | A section for the next minor: fluent rule changes (2.5), `ConfigureInertia` rename (4.3), any key-normalization output changes (2.3). |
| `devtools.mdx` | The no-reader warning (2.6). |
| `ai-agents.mdx` | Regenerated `llms.txt`/`llms-full.txt` pick up the new page automatically; check that the precognition page is present and that the rate-limiting snippet survives the plain-text export. |

### 5.3 Site checks (Terra medium)

Run `bun install && bun run build && dotnet run -c Release` in `docs/Inertia.Net.Docs`, open every changed page, and attach
screenshots of: the new precognition page (top and the rate-limiting section), the testing page's new example, the troubleshooting
entries, and the migrating section. Confirm search finds "precognition" and "rate limit", code highlighting works for the C# and
TSX snippets, and `GET /llms.txt` lists the new page. Open the three tabbed client snippets and confirm each renders.

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

## Phase 7: streaming the page JSON (spike first, then a gated implementation)

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
and stop.

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

**Tests.** `tests/Inertia.Net.Tests`: identical bytes for buffered and streamed renders across the conformance fixtures; a loader
that fails after the first flush aborts the response (assert the exception type from the test server and the log entry); every
"silently off" condition falls back to buffered; the empty-200 redirect does not fire. `tests/Inertia.Net.IntegrationTests`:
the Inertia client-facing headers are present on the first chunk; Minimal API, MVC and FastEndpoints all honour the option.

**Docs.** `performance.mdx` gets a "Streaming" section with the measured numbers, when to turn it on (slow sequential loaders,
very large pages, no SSR), when not to (anything that needs `Content-Length`, pages behind a buffering proxy), and the failure
behaviour. `configuration.mdx` lists the option; `troubleshooting.mdx` gets "page request fails with a network error instead of
a 500".

## Out of scope for this plan

- In-process JavaScript SSR and `HybridCache` (decided against in [plan-performance-and-dx.md](plan-performance-and-dx.md)
  §2.3; in-process SSR would also break the Native AOT and trimming story).
- 103 Early Hints (blocked on Kestrel).
- Renaming the `Inertia.Net.Inertia` static class (a breaking change; the collision docs in 2.4 are the mitigation for now).
