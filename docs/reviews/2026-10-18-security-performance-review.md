# Security and performance review: Inertia.Net 1.1.0

Reviewed at commit `2b99400` (release 1.1.0). Scope: `src/Inertia.Net`, `src/Inertia.Net.Mvc`, `src/Inertia.Net.FastEndpoints`,
`src/Inertia.Net.Testing`, the GitHub workflows, the samples and the public docs. Method: a full static read of the source with
the protocol reference (`docs/protocol.md`) and the benchmark report (`tests/Inertia.Net.Benchmarks/RESULTS.md`) as context.
The remediation plan is at the end.

Severity scale: **High** = exploitable or data-losing in a default deployment; **Medium** = exploitable or costly under a common
configuration, or a secure-by-default gap; **Low** = hardening, edge case or small win; **Info** = verified sound, or a design
tradeoff worth recording.

## Summary

| # | Severity | Area | Finding |
|---|---|---|---|
| S1 | Medium | CSRF | Antiforgery is opt-in and separate from `UseInertia()`; JSON mutations are never validated by `UseAntiforgery()`; the samples ship with no CSRF protection |
| S2 | Medium | Cookies / proxies | `Secure` is tied to `Request.IsHttps` with no policy override; behind a TLS-terminating proxy without forwarded headers the state and XSRF cookies go out non-Secure and the version-mismatch `X-Inertia-Location` is built as `http://` |
| S3 | Low | CSRF | The XSRF cookie is not `IsEssential`, so a cookie-consent policy suppresses it and every Inertia mutation fails validation |
| S4 | Low | Redirects | `Back()` accepts an `http://` Referer on an HTTPS request (same host), redirecting to plaintext |
| S5 | Low | SSR trust boundary | The SSR server exposes unauthenticated `/render` and `/shutdown`; the bind host is baked into the bundle and the adapter cannot enforce loopback; docs do not say so |
| S6 | Low | Root view | `RootView` / `WithRootView` are joined to the content root without validation and cached in unbounded dictionaries; user input there means arbitrary file read and memory growth |
| S7 | Low | Supply chain | No Dependabot, no CodeQL, actions pinned by tag, `ci.yml` has no `permissions:` block, no `SECURITY.md`, NuGet audit at default scope, NuGet publish has no environment gate |
| S8 | Info | Output encoding | HTML-safe embedding verified sound; one untested path (declared property names under an app-configured relaxed encoder) |
| P1 | Medium | SSR hot path | `File.Exists` and `Uri` parsing on every server-rendered page when `Ssr.BundlePath` is set (the sample sets it) |
| P2 | Medium | MVC root view | Whole page JSON materialized as a UTF-16 string; Vite tags rendered via string round trip and `Split` per request |
| P3 | Medium | SSR response | Head and body decoded to .NET strings and re-encoded, doubling peak memory for large SSR bodies |
| P4 | Low | Session store | `ISession.LoadAsync` on every request through the middleware, including non-Inertia API calls and brand-new sessions |
| P5 | Low | File cache | Misses are cached forever in production; a manifest that lands after first request is never seen until restart |
| P6 | Low | Results | `InertiaLocationResult` parses all nine Inertia headers to read one flag |
| P7 | Low | Serialization | One `JsonSerializerOptions` (with its own metadata cache) per property that has a custom converter or number handling |
| P8 | Low | Allocation | `ObjectEntries` iterator and the `Set` closure allocate per typed object / per render |
| P9 | Low | Template view | HTML is written to the response pipe before all dynamic segments are resolved; a `@vite` failure leaves a garbled 500 |
| P10 | Info | Buffering | Fully buffered page JSON (by design): doubling growth and LOH retention at high concurrency with large pages |
| P11 | Info | Partial reloads | Filter matching is O(filters × props) per render; same as Laravel, not a practical DoS |
| C1 | Medium | Correctness (noticed) | `Dictionary<string, TDto>` with `InertiaProp` members is serialized in one call and throws at render (reproduced) |

Nothing High was found. The protocol core (page writer, props walk, state store, middleware) is carefully built and the
dangerous surfaces (script embedding, redirect targets, cookie integrity, process spawning, SSR response bounds) are handled.
The Medium security and performance items are about defaults and deployment realities rather than bugs in what exists; the
one reproduced bug (C1) is a serialization gap, not a security issue.

## Implementation status

Every finding and plan item below was implemented on the same branch after this review; see the [changelog](../../CHANGELOG.md)
and [migration guidance](../migrating-to-1.2.md). Two findings changed while being fixed:

- **S8 was a real bug.** The regression test the review asked for failed: with `UnsafeRelaxedJsonEscaping`, a declared property name
  containing `</script>` closed the page script four times in one page. Property names now use the HTML-safe encoder whenever the
  app sets a custom encoder.
- **P7 is narrower than proposed.** Converter instances can carry per-property configuration (custom `JsonConverterAttribute`
  subclasses), so options are shared per converter *instance* and number handling, not per converter type. Members that differ only by
  number handling share one options instance.

| Finding | Resolution | Verified by |
|---|---|---|
| S1 | `UseInertiaAntiforgery()`: issue and validate (JSON and form), `419`, `DisableAntiforgery()` opt-out, `ShouldValidate`, `OnRejected`; samples and all quick starts use it | `InertiaAntiforgeryTests` (Minimal API, MVC, FastEndpoints hosts); Playwright token-less mutation spec on all three samples |
| S2 | `SecurePolicy` / `CookieSecurePolicy` options; README "Deploying behind a proxy" | `StateStoreTests`, `InertiaAntiforgeryTests` |
| S3 | XSRF cookie `IsEssential` | consent-policy integration test |
| S4 | `http` Referer upgraded to `https` on HTTPS requests | `BackResultTests` |
| S5 | Non-loopback exposure probe and warning; README trust model | `SsrNodeProcessTests` with a fixture on `0.0.0.0` and on `127.0.0.1` |
| S6 | Root views outside the content root refused; path and file caches bounded | `RootTemplateTests` |
| S7 | Dependabot, CodeQL, SHA-pinned actions, read-only CI token, `release` environment, NuGet audit `all`/`low`, `SECURITY.md` | restore and build with the stricter audit; YAML parsed |
| S8 | HTML-safe encoder for property names under a custom app encoder | `PageWriterTests` |
| P1 | Bundle presence through the file cache; cached render URL; URL string compared before parsing | `SsrGatewayTests` |
| P2 | Streaming `IHtmlContent` for the MVC tag helpers; cached entry split | `MvcRazorTests` (60,000-character multi-byte page); `--mvc-probe` |
| P3 | Pooled UTF-8 SSR parsing, kept after measuring | `SsrGatewayTests` parser cases; `--ssr-probe`; React SSR Playwright spec |
| P4 | Session and TempData loads skipped without their cookie | `StateStoreTests`, `TempDataStateStoreTests` and the TempData conformance suites |
| P5 | Misses re-checked once per second in production | `RootTemplateTests`, `SsrGatewayTests` |
| P6 | One header read in `InertiaLocationResult` | conformance suites |
| P7 | Options shared per converter instance and number handling | `SerializationRegressionTests` |
| P8 | Pooled member arrays instead of an iterator; synchronous prop building | `--render-probe` (typed page 2,592 to 2,472 B) |
| P9 | `@vite` tags resolved before the first byte | `RootTemplateTests` |
| P10 | Per-component initial page buffer size; README payload guidance | `PageWriterTests`; `--payload-probe` |
| P11 | No change (as planned) | n/a |
| C1 | Typed dictionaries walked, keys as System.Text.Json writes them | `SerializationRegressionTests` |

Measurements are in the first section of `tests/Inertia.Net.Benchmarks/RESULTS.md`.

## Security review

### S1. CSRF protection is opt-in, partial, and absent from the samples (Medium)

Inertia is a form-driven protocol: `Back().WithErrors(...)`, `WithInertiaValidation()`, `AddInertiaMvc()` and the FastEndpoints
pre-processor all exist to service POST/PUT/PATCH/DELETE from the browser. The adapter itself never checks a CSRF token.
`UseInertiaAntiforgeryCookie()` (`src/Inertia.Net/InertiaAntiforgery.cs`) only *issues* the token cookie; validation depends on
the app also calling `UseAntiforgery()`, which validates form bodies only, or writing its own middleware or filter for JSON
bodies, which is what the Inertia client sends by default. The README documents this correctly under "CSRF and antiforgery", but:

- all three samples (`samples/*/Program.cs`) register no antiforgery at all, so the reference code for the project models
  unprotected `POST /contacts` and `DELETE /users/{id}`;
- the "Getting started" path does not mention CSRF; a reader has to reach section 20 of the README;
- there is no single call that covers the common case (JSON body, Inertia request, non-GET) the way Laravel's
  `VerifyCsrfToken` does globally.

`SameSite=Lax` on the state and XSRF cookies limits classic cross-site POST, but Lax does not protect top-level navigations
with GET side effects, does not apply when the attacker is on a sibling subdomain, and is a browser default the server should
not rely on as its only line of defense.

**Recommendation.** Ship a one-call protection: `app.UseInertiaAntiforgery()` (or `UseInertiaAntiforgeryCookie(validate: true)`)
that issues the cookie as today and, for Inertia requests with an unsafe method, calls `IAntiforgery.ValidateRequestAsync`
and answers failures the way the Inertia client expects (Laravel uses `419`; the client shows the error modal, and the docs
recommend redirecting back with a flash message). Provide MVC (`AutoValidateAntiforgeryToken` guidance stays valid) and
FastEndpoints equivalents or make the middleware cover them. Update the samples to use it, and move the CSRF paragraph into
"Getting started".

### S2. Cookie `Secure` and absolute URLs depend on `Request.IsHttps` with no override (Medium)

Both cookies the adapter writes use `Secure = httpContext.Request.IsHttps`
(`src/Inertia.Net/InertiaStateStore.cs:85`, `src/Inertia.Net/InertiaAntiforgery.cs:32`). Behind a TLS-terminating proxy
that does not forward `X-Forwarded-Proto`, or when `UseForwardedHeaders()` is missing, every deployment serves the encrypted
state cookie and the XSRF token over "http" from Kestrel's point of view, so neither cookie is marked `Secure`. The same
condition makes the version-mismatch `X-Inertia-Location` (`request.GetEncodedUrl()` in `InertiaMiddleware`) an `http://`
URL, which the client follows as a full visit. The README mentions the symptom once in troubleshooting (line 1546) but there
is no deployment section and no option.

ASP.NET Core's own cookie-emitting components expose `CookieSecurePolicy` for exactly this reason.

**Recommendation.** Add `InertiaStateOptions.CookieSecurePolicy` (default `SameAsRequest` to preserve behavior) and a
`secure` policy parameter on the antiforgery helper; honor `CookiePolicyOptions.Secure` when `UseCookiePolicy` is present
(set `CookieOptions.Secure` only when the policy does not already force it). Add a README "Deploying behind a proxy" section
covering `UseForwardedHeaders`, the `Secure` flag, Data Protection key sharing and the SSR bind host (S5).

### S3. The XSRF cookie is not essential (Low)

`CookieInertiaStateStore` sets `IsEssential = true`; `UseInertiaAntiforgeryCookie` does not. With
`services.Configure<CookiePolicyOptions>(o => o.CheckConsentNeeded = _ => true)` and `app.UseCookiePolicy()`, non-essential
cookies are dropped until the user consents, so the client never receives a token and every mutation fails antiforgery with a
400. ASP.NET Core marks its own antiforgery cookie essential by default.

**Fix.** Set `IsEssential = true` in `InertiaAntiforgery.cs`; add an integration test with `CheckConsentNeeded` on.

### S4. `Back()` does not compare the Referer scheme (Low)

`InertiaBackResult.GetBackUrl` (`src/Inertia.Net/InertiaBackResult.cs:78`) accepts any `http(s)` Referer whose host and port
match the request. The comment explains the scheme is ignored so it works behind a TLS-terminating proxy. The side effect: an
HTTPS request carrying `Referer: http://same-host/...` is redirected to plaintext. Browsers rarely produce that combination,
and the redirect target is same-host, so impact is low.

**Fix.** When `request.IsHttps` and the Referer scheme is `http`, rewrite the scheme to `https` (keeps the proxy case
working) rather than falling back. One unit test in `BackResultTests`.

### S5. The SSR server trust boundary is undocumented (Low)

The Inertia v3 SSR server binds the host and port baked into the bundle by `@inertiajs/vite` (upstream default `0.0.0.0`,
see `docs/protocol.md` §8) and exposes `POST /render` and `POST /shutdown` with no authentication. The adapter requires a
loopback `Ssr.Url` for the managed process, but cannot influence what the Node process binds. The React sample correctly sets
`host: '127.0.0.1'` in `vite.config.ts`; the README does not say why, and the Vue and Svelte samples do not build SSR.
Anyone who can reach the port can stop the renderer (the supervisor restarts it, with backoff), burn CPU with render
requests, or feed arbitrary page JSON to the app's components. The adapter also injects the SSR `head` and `body` verbatim
into the HTML, which is correct for a trusted renderer and must stay that way, but should be stated.

**Recommendation.** Document the trust model and the `host: '127.0.0.1'` requirement next to `UseNodeProcess()`; have the
managed process log a warning at startup when `INERTIA_SSR_PORT` is set but the bundle's bind host cannot be confirmed (a
single `GET /health` against the non-loopback interface is enough to detect `0.0.0.0`).

### S6. Root view names are trusted paths (Low)

`RootTemplateView.RenderAsync` joins `context.RootView` to the content root and caches the result in a `ConcurrentDictionary`
with no bound (`src/Inertia.Net/RootTemplateView.cs:41`); `FileCache` caches every path it is asked for. `RootView` comes from
options or `InertiaResult.WithRootView(string)`. If an application ever derives that from a route value or query string, a
request can read any file the process can read (the parse fails without `@inertia`, but the exception message includes the
path and a template that happens to contain `@inertia` is rendered) and grow the caches without limit.

**Fix.** Reject rooted paths and `..` segments in `WithRootView` and `RootView` (resolve with `Path.GetFullPath` and require
the content root prefix), and say in the XML docs that the value must not come from the request. Cheap, and it removes a
whole class of misuse.

### S7. Repository and release hygiene (Low)

- No `.github/dependabot.yml`: four `package-lock.json` files, the NuGet packages in `Directory.Packages.props` and the GitHub
  Actions versions are updated by hand only.
- No static analysis in CI (CodeQL or equivalent).
- `ci.yml` has no top-level `permissions:` block, so the token has whatever the repository default is; `release.yml` grants
  `contents: write`, which is needed, but the job also holds the NuGet API key with no environment protection rule, so any
  pushed `v*` tag publishes.
- Actions are pinned by major tag (`actions/checkout@v7`, `softprops/action-gh-release@v2`), not by commit SHA.
- NuGet audit runs at the SDK default (direct dependencies, moderate and above). `NuGetAuditMode=all` and
  `NuGetAuditLevel=low` in `Directory.Build.props` make `-warnaserror` builds fail on known-vulnerable transitive packages.
- No `SECURITY.md` with a disclosure contact.

### S8. Verified sound (Info)

These were checked and need no change:

- **Script embedding.** `InertiaPageWriter.CreateHtmlSafeEncoder` forbids `< > & / ' " + \`` and U+2028/2029, so
  `</script>` and `<!--` cannot appear in the embedded JSON; `PageWriterTests` runs a `</script>` corpus through component
  name, URL, flash, shared and page prop keys and values. The root element id is HTML-encoded in `data-page` and `id`.
  Restored flash `JsonElement`s are re-encoded through the writer (STJ unescapes before writing), so the invariant holds for
  state carried across redirects. One gap: STJ pre-encodes *declared* property names with the application's options encoder,
  not the writer's. With the default encoder that is still safe; if an application configures `UnsafeRelaxedJsonEscaping` in
  `ConfigureHttpJsonOptions`, a `[JsonPropertyName]` containing `<` on a DTO that is serialized in one call (no `InertiaProp`
  members) would be emitted raw. Property names are developer-controlled, so this is a test gap rather than a vulnerability:
  add a regression test that pins HTML safety with a relaxed app encoder.
- **Vite output.** Manifest file names and the dev origin are HTML-encoded; `ValidateOrigin` rejects quotes, angle brackets,
  backslash and whitespace before the origin is interpolated into the React refresh script; the hot file is only read in
  Development (protocol deviation 10).
- **Redirect state.** Data Protection with a dedicated purpose, HttpOnly, `SameSite=Lax`, `IsEssential`; undecryptable or
  malformed state is rejected atomically and the cookie deleted; size over 4 KB is logged.
- **Open redirect.** `Back()` only follows a Referer with the request's host and port (S4 aside); the version-mismatch and
  fragment 409s echo the app's own URLs.
- **Process spawning.** `ProcessStartInfo` with `UseShellExecute = false` and `ArgumentList`; no shell, no string
  concatenation; environment and working directory are config.
- **SSR bounds.** `Content-Length` is checked before reading, `SsrResponseStream` enforces `MaxResponseBytes` on streamed and
  chunked bodies, the linked token makes `Ssr.Timeout` the single deadline, and failures fall back to client rendering.
- **Validation messages.** `InertiaModelStateFilter` replaces binding-exception messages with a generic string so internals
  are not echoed to the client.
- **Header parsing.** List headers are parsed in two passes into exact-size arrays and bounded by Kestrel's header limits.

## Performance review

The benchmark suite and `RESULTS.md` are unusually thorough for a library of this size, and the hot path (header parse, prop
walk, pooled page buffer, allocation-free middleware for non-Inertia requests) is already well tuned. The findings below are
either outside what the benchmarks exercise (MVC, SSR, stores) or small follow-ups.

### P1. Filesystem and URI work on every SSR render (Medium)

`SsrGateway.RenderAsync` (`src/Inertia.Net/SsrGateway.cs:54-61`) does, per server-rendered page:

- `File.Exists(Path.Combine(ContentRootPath, bundle))` when `Ssr.BundlePath` is set (the React sample sets it, so this is the
  documented configuration): a synchronous stat on the request thread;
- `nodeProcess.IsReadyFor(ssr.Url)` which calls `Uri.TryCreate` and allocates a `Uri`;
- `$"{ssr.Url.TrimEnd('/')}/render"` string building.

**Fix.** Resolve bundle presence through the existing `FileCache` (or once at startup with a Development-only recheck), and
cache the parsed `Uri` and render URL alongside the options (`IOptionsMonitor.OnChange` invalidates). This removes a syscall
and three allocations from every first visit.

### P2. The MVC root view copies the page twice (Medium)

`InertiaTagHelper` calls `GetBodyHtml()` (`src/Inertia.Net/IInertiaRootView.cs:63`), which encodes the page JSON into an
`ArrayBufferWriter`, decodes it to a UTF-16 `string` (2× the bytes, LOH above ~42 KB of JSON), hands it to Razor as
`SetHtmlContent(string)`, and Razor re-encodes it to UTF-8 on the way out. `ViteTagHelper` does `Entry.Split(...)` per request
and goes through `RenderTags` → `Encoding.UTF8.GetString` of bytes that were already cached as UTF-8. For the 1 MiB page in
`RESULTS.md` the MVC path adds roughly 2 MiB of transient allocation that the template view does not.

**Fix.** Implement `IHtmlContent` types that write the UTF-8 bytes to the `TextWriter` in pooled chunks
(`Encoding.UTF8.GetDecoder()` into a rented `char[]`), and cache the split entry array per tag helper instance or per `Entry`
string. Add an MVC row to `RenderBenchmarks`.

### P3. SSR head and body round-trip through strings (Medium)

`SsrResponse` deserializes `head` and `body` into .NET strings, then `Utf8Output.Write` re-encodes them. For a 1 MiB SSR
body that is a 2 MiB string plus the re-encode; `RESULTS.md` records the streaming gateway as a memory win with a CPU cost.
Bounded by `MaxResponseBytes`, so this is a tradeoff rather than a hazard.

**Option.** Read the response into a pooled buffer and walk it with `Utf8JsonReader`; copy `ValueSpan` straight to the
response when `!ValueIsEscaped`, else `CopyString` into a rented buffer. Medium effort; measure with `--ssr-probe` before
committing to it.

### P4. The session store loads the session on every request (Low)

`SessionInertiaStateStore.LoadAsync` calls `ISession.LoadAsync` unconditionally, and the middleware calls it for every request
that passes through `UseInertia()`, including non-Inertia API calls and requests with no session cookie. `DistributedSession`
issues a cache `Get` even for a brand-new key, so this is one distributed-cache round trip per request for opt-in users.
`TempDataInertiaStateStore.Peek` has the same shape at lower cost (a cookie decrypt).

**Fix.** Skip the load when the request carries no session cookie (`IOptions<SessionOptions>.Value.Cookie.Name`), and
document that `UseInertia()` should be mapped after static files and outside pure-API branches.

### P5. Production file cache never forgets a miss (Low)

`FileCache.Get` stores `null` for a missing file and, with `reload = false`, never stats again
(`src/Inertia.Net/FileCache.cs:34-36`). If the app starts before the Vite build is copied in (container layering, blue/green
asset sync), `ManifestHash()` returns `""` and `LoadManifest()` throws for every first visit until the process restarts.

**Fix.** Keep hits permanent in production but re-check misses on the same one-second cadence used in Development.

### P6. `InertiaLocationResult` parses every header (Low)

`new InertiaRequest(httpContext.Request.Headers).IsInertia` (`src/Inertia.Net/InertiaResult.cs:120`) parses nine headers and
allocates the list arrays to read one boolean. Use the static `X-Inertia` check the middleware already has.

### P7. One `JsonSerializerOptions` per converter-bearing property (Low)

`InertiaPageWriter.PropertyTypeInfo` creates a new `JsonSerializerOptions` (which carries its own metadata cache) for each
`(JsonPropertyInfo, NumberHandling)` with a custom converter or number handling. It is one-time per property and correct, but a
large DTO surface with many `[JsonConverter]` members retains many duplicate caches. Key the cache by
`(converter type or instance, handling)` instead, so members sharing a converter share an options instance.

### P8. Per-object and per-render allocations (Low)

- `ObjectEntries` is a `yield` iterator: one enumerator per typed object visited, so one per row in typed arrays.
- `BuildPropsAsync`'s `Set` local function captures five locals: one closure per render.
- `sharedPropKeys.Contains` is a linear scan per shared key (bounded, fine below a few dozen).

Convert `ObjectEntries` to a struct enumerator or an index loop inside `WriteObjectAsync`, and make `Set` a static method
taking a small struct by `ref`. Verify with `RenderBenchmarks.JsonTypedProps` before and after; expected saving is tens of
bytes per row, which matters for the 2,000-prop typed-row workload in `RESULTS.md`.

### P9. Template HTML is written before dynamic segments are resolved (Low)

`RootTemplateView.RenderAsync` writes literal segments to `Response.BodyWriter` as it goes. When `@vite(...)` throws (missing
manifest, unknown entry) after the `<head>` literal has been written, the bytes are already in the response pipe; the
exception handler then appends an error page to a partial document. This only happens on deployment mistakes, but it turns a
clear error into a garbled one.

**Fix.** Resolve the Vite and ViewData segments into byte arrays first, then write. The template is small apart from the
page JSON, which is already buffered.

### P10. Full buffering of the page JSON (Info)

The page is fully buffered in a pooled array so a failing loader never produces a partial 200. `PooledBufferWriter` doubles
from 4 KiB, so a 1 MiB page copies about eight times before the write, and large arrays land on the LOH and stay in
`ArrayPool.Shared` per core; `RESULTS.md` shows a 354 MiB working set at 128 concurrent 1 MiB pages. This is a deliberate,
documented tradeoff. Two cheap improvements: start from a per-component size hint (exponential moving average of the last
rendered size) to skip the growth copies, and document a guidance figure for prop payload size next to
`Ssr.MaxResponseBytes`.

### P11. Partial-reload filter matching (Info)

`MatchesAny` / `LeadsToAny` are O(filters × props). A client can send a long `X-Inertia-Partial-Data` header (bounded by
Kestrel's 32 KB header limit) against a page with thousands of props and force millions of short span comparisons. Laravel has
the same shape; it is not a practical denial of service. No action.

## Correctness note (out of scope, noticed in passing)

### C1. Typed dictionaries holding props throw at render (Medium, reproduced)

`ComputeContainsProps` returns `false` for `JsonTypeInfoKind.Dictionary` unless the type is assignable to
`IDictionary<string, object?>` / `IReadOnlyDictionary<string, object?>` (`src/Inertia.Net/InertiaPageWriter.cs:514-543`).
A `Dictionary<string, UserRow>` where `UserRow` has an `InertiaProp` member is therefore serialized in one
`JsonSerializer.Serialize` call, and `InertiaPropJsonConverter.Write` throws "InertiaProp values can only be serialized by
Inertia.Net", which surfaces as a 500 on the page.

Reproduced with a throwaway unit test against the `Harness` (not committed): `["rows"] = new Dictionary<string, Row> { ["a"] =
new("first", Defer(() => 10)) }` throws from `PropsWriter.WriteValueAsync` line 261, while the same row in a `List<Row>` renders
`rows.0.extra` in `deferredProps` as expected. The fix is to treat `Dictionary` kind like `Enumerable` (check `info.ElementType`)
and add a dictionary branch in `PropsWriter.WriteValueAsync` that walks `IEnumerable<KeyValuePair<,>>` with the key converted
the way STJ would (`DictionaryKeyPolicy`, non-string keys). Add the failing test first; the probe above is a ready template.

## Remediation and upgrade plan

Ordered by value per effort. Each phase is independently shippable. Verification for every phase is the AGENTS.md
sequence (`dotnet build ... -warnaserror`, `dotnet test`), plus `bash eng/aot-smoke.sh` for anything touching
`src/Inertia.Net`, and the Playwright suite for anything touching samples or `InertiaAntiforgery`.

### Phase 0: repository hygiene (half a day, no code)

1. Add `.github/dependabot.yml` for `nuget` (root), `npm` (`samples/MinimalApi.React`, `samples/Mvc.Vue`,
   `samples/FastEndpoints.Svelte`, `tests/e2e`) and `github-actions`, weekly, grouped minor/patch.
2. Add a CodeQL workflow (`csharp`, `javascript-typescript`) on push to `main` and pull requests.
3. Add `permissions: contents: read` at the top of `ci.yml`; pin every action to a commit SHA with the version in a comment.
4. Put the NuGet push step behind a protected `release` environment with required reviewers.
5. Set `NuGetAuditMode=all` and `NuGetAuditLevel=low` in `Directory.Build.props`.
6. Add `SECURITY.md` (supported versions, private disclosure address, response time).

### Phase 1: security quick wins (one to two days) → 1.1.1

1. S3: `IsEssential = true` on the XSRF cookie; integration test with `CheckConsentNeeded`.
2. S4: upgrade `http` Referer to `https` on HTTPS requests in `GetBackUrl`; unit tests for same-host http/https and the proxy
   case.
3. S6: validate `RootView` / `WithRootView` (no rooted paths, no `..`, must resolve under the content root); XML doc note.
4. S8: regression test that the embedded page stays HTML-safe when the app configures `UnsafeRelaxedJsonEscaping`
   (including a DTO with a `[JsonPropertyName]` containing `<`); fix if it fails.
5. S5 docs: trust model and `host: '127.0.0.1'` next to `UseNodeProcess()`; startup warning when the renderer answers on a
   non-loopback interface.
6. README: new "Deploying behind a proxy" section (forwarded headers, `Secure`, key ring, SSR bind host).
7. CHANGELOG entries under Fixed/Security.

### Phase 2: CSRF story (two to three days) → 1.2.0

1. Design `app.UseInertiaAntiforgery()` (or `UseInertiaAntiforgeryCookie(validate: true)`): issue the cookie as today; for
   Inertia requests with an unsafe method call `IAntiforgery.ValidateRequestAsync`; on failure return `419` with an Inertia
   error page or, when a same-host Referer exists, redirect back with a flash message. Decide the default in the PR; both are
   compatible with the client.
2. S2: `InertiaStateOptions.CookieSecurePolicy` and a matching parameter on the antiforgery helper; honor `CookiePolicyOptions`.
3. MVC: document `AutoValidateAntiforgeryToken` as the alternative; FastEndpoints: validate in `InertiaPreProcessor` or rely on
   the middleware; conformance tests for all three hosts.
4. Update the three samples to register antiforgery and the new middleware; extend the Playwright `sample.spec.ts` with a
   token-less POST expecting rejection.
5. README: move CSRF into "Getting started"; migration note (additive, opt-in).

### Phase 3: performance quick wins (one to two days) → with 1.1.1 or 1.2.0

1. P1: bundle presence via `FileCache`; cache parsed `Uri` and render URL; `SsrGatewayTests` for the cached path.
2. P6: static `X-Inertia` check in `InertiaLocationResult`.
3. P5: re-check misses in production on the one-second cadence; `ViteAssetsTests` for "manifest appears later".
4. P8: struct enumerator for `ObjectEntries`, static `Set`; confirm with `RenderBenchmarks` (`JsonTypedProps`, `Json20Props`)
   and the `--render-probe` run; update `RESULTS.md`.
5. P9: pre-render Vite and ViewData segments before the first write; `RootTemplateTests` for a failing `@vite` producing a
   clean exception with nothing written.

### Phase 4: larger performance work (three to five days) → 1.2.x

1. P2: `IHtmlContent` streaming for `<inertia />` and `<vite />`; cache split entries; add an MVC render benchmark and a
   1 MiB MVC row to `RESULTS.md`.
2. P4: skip session/TempData load without the cookie; `TempDataStateStoreTests` and `StateStoreTests` coverage.
3. P7: options cache keyed by converter and handling; measure retained memory with the 200-DTO payload benchmark.
4. P10: per-component initial capacity hint; document payload guidance.
5. P3 (spike first): `Utf8JsonReader`-based SSR body copy; keep only if `--ssr-probe` shows a win at 1 MiB.

### Phase 5: correctness fix (one day) → 1.1.1

1. C1: failing test (`Dictionary<string, Dto>` with a `Defer` member, plus `Dictionary<int, Dto>`), then `Dictionary` kind in
   `ComputeContainsProps` and a dictionary branch in `WriteValueAsync`; protocol doc note under §4. It is a pure bug fix with
   no API change, so it belongs in the first patch release.

### Release mapping

| Version | Content |
|---|---|
| 1.1.1 | Phase 0, Phase 1 fixes (no new public API apart from validation of existing inputs), Phase 3 items 1 to 3, Phase 5 |
| 1.2.0 | Phase 2 (new middleware and options), remaining Phase 3 |
| 1.2.x | Phase 4 as each item lands with benchmarks |

## Verification performed and not performed

Done, on Linux x64 with SDK 10.0.402 (installed into a scratch directory for this review):

- `dotnet build Inertia.Net.slnx -c Release -warnaserror`: succeeded, 0 warnings.
- `dotnet test --solution Inertia.Net.slnx -c Release --no-build`: 786 tests across the three test projects, all passed.
- C1 reproduced with a temporary test (removed again); every other finding is from reading the code.

Not done:

- No Native AOT smoke run (`eng/aot-smoke.sh` needs clang and zlib), no Playwright run, no benchmark run.
- No dynamic testing of the samples or the SSR server.
- No dependency vulnerability scan of the npm lockfiles or NuGet graph; Phase 0 puts that in CI.
