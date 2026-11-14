# Performance plan for 1.9.1 (research, 2026-11-14)

Scope: every library package, audited at `main` `9df898d` (v1.9.0). A patch release: no public API change, no protocol change,
Native AOT and source-generated JSON kept. Each item is kept only if its named measurement shows the gain, as in
[plan-performance-and-dx.md](plan-performance-and-dx.md) section 3b.

## 1. Where things stand

The render path that 1.5-1.7 tuned is still tight. Warm in-process renders into `Stream.Null`, this host, 2026-11-14:

| Workload (`--render-probe`, `--cache-probe`, BenchmarkDotNet short job) | Allocated | Time |
|---|---:|---:|
| JSON, 20 mixed props | 1,744 B | 26-29 µs |
| JSON, typed POCO props | 1,560 B | 18-20 µs |
| Partial reload, 2 of 20 | 576 B | 12-14 µs |
| HTML + Vite tags, 20 props | 1,808 B | 29-32 µs |
| Reference: plain STJ of the same page | 312 B | 17 µs |
| `UseInertia`, Inertia request (middleware only) | 48 B | 1.2 µs |
| Cached prop hit / tagged / stale | 568 / 632 / 584 B | 6-8 µs |
| Cached SSR render hit | 672 B | 17-19 µs |

JSON and HTML are 8 B above the 1.6.2 rows in RESULTS.md (1,736 and 1,800 B); not chased.

What is left is in the code added in 1.6-1.9, none of which has a benchmark: head tags, live props, the Redis store, the EF Core
and MongoDB pagers, the SignalR transport, the analyzers. That is where this plan spends its effort.

Environment: Linux 6.8.0-149 x64 VM, 8 logical CPUs, SDK 11.0.100, benchmarks on the net10.0 runtime (10.0.14), no CPU pinning, other services running on the host. Allocation
figures are exact; times are indicative only and no item below relies on a timing difference smaller than the noise seen here.

## 2. Work items

Sizes: S is under an hour of code, M is a few hours. "Measured" means run for this plan; "read" means found by reading the code
and not yet measured.

### 2.1 Fixes that matter outside a benchmark

**A1. Redis tag sets keep the keys of expired entries (M). Measured.**
`src/Inertia.Net.Redis/RedisInertiaCacheStore.cs:36-51`. The `Set` script adds the entry to each tag set and extends the set's
TTL, but an entry that expires through its own `PX` is never removed from the sets. Every cached prop carries the
`inertia:props` tag, so with per-user or per-query keys that set grows for as long as the app keeps writing.

- Reproduced with the store's own script against Redis 8.2: 20,000 keys with a 1 s TTL, one writer kept alive. Three seconds
  after every entry had expired the database held 3 keys and the tag set held 20,001 members (1,033,360 bytes).
- Fix: in the `Set` script, for each tag, take `SRANDMEMBER KEYS[i] 2` and `SREM` the members whose entry key no longer
  `EXISTS`. Two swept per add keeps the set bounded. Keys are already built inside the script, so cluster behavior is unchanged.
- Test (`Inertia.Net.Redis.Tests`): write N short-lived keys, wait, keep writing one key, assert `SCARD` falls well below N.
  Existing tag-invalidation tests must pass unchanged.
- Risk: medium, it is a Lua change. `RemoveByTagAsync` gets faster as a side effect (fewer dead members per 256-entry batch).

**A2. Skip the COUNT query when the page already gives the total (S). Read.**
`src/Inertia.Net/Pagination/Pagination.cs:39`. `PaginateAsync` always awaits `count`. When the fetch returned no extra row and
either returned rows or is page 1, the total is exactly `skip + rows.Count`. This is the code behind `ToPageAsync` in both
`Inertia.Net.EntityFrameworkCore` and `Inertia.Net.MongoDB`.

- Fix: `!hasMore && (rows.Count > 0 || skip == 0) ? skip + rows.Count : await count(ct)`.
- Gain: one database round trip on single-page lists and last pages; nothing on middle pages.
- Tests: a counting delegate is not called on a short first page and on a last page, is called on a middle page and on an empty
  page past the end. Update any test that asserts the delegate always runs, and the XML remark on line 24.
- Risk: low. The total can no longer disagree with the rows under a concurrent write, which is an improvement.

**A3. TempData state store may read a distributed session synchronously on every request (S). Read, not confirmed.**
`src/Inertia.Net.Mvc/TempDataInertiaStateStore.cs:38`. `factory.GetTempData(httpContext).Peek(Key)` with
`SessionStateTempDataProvider` loads the session through the blocking `ISession` path. The core session store already awaits
`session.LoadAsync` first (`src/Inertia.Net/InertiaStateStore.cs:121`).

- First confirm it: an integration test with an `IDistributedCache` whose sync `Get` throws. If it does not throw, drop the item.
- Fix: when the provider is the session one, `await httpContext.Session.LoadAsync(RequestAborted)` before `GetTempData`, inside
  a try/catch that keeps today's tolerance of a failing store.
- Gain: no blocked thread-pool thread per request with Redis or SQL sessions. Nothing with in-memory sessions or cookie TempData.

### 2.2 Render-path allocations in 1.9 features

**B1. Head tags (M). Measured.**
Five tags (`Title`, `Meta`, two `Property`, `Link`) on the 20-prop page:

| Render | Without head | With 5 tags | Cost |
|---|---:|---:|---:|
| HTML | 1,808 B, 31-33 µs | 4,968 B, 40-43 µs | +3,160 B, about +8 µs |
| JSON | 1,744 B | 3,624 B | +1,880 B |

Where it goes, in order of ease:

1. `RootTemplateView.cs:128`: `Encoding.UTF8.GetBytes(string.Join('\n', tags))` builds one string and one array the size of the
   head. Write each tag with the existing `Utf8Output.Write` and a `"\n"u8` separator. Output is byte-identical.
2. `InertiaHead.cs:76-99`: `ToHtml` fills a `List<string>` and copies it to an array. Fill a `string[]` directly.
3. `InertiaHead.cs:29`: `IsEmpty` runs LINQ over the dictionary values; use a loop.
4. `InertiaResult.cs:109-117`: `WithHead` builds one `InertiaHead` (a dictionary) on the result and copies it into a second one
   on the feature. When the feature has no head yet, hand the result's instance over instead of copying.

Target: under +1,500 B on HTML. Tests: `--check`-style byte comparison of the HTML and the `head` prop before and after; the
existing head tests (shared tag override, `Remove`, title template) unchanged.

**B2. Live props (S). Measured.**
Five lazy props built and rendered: 1,592 B. The same five with `.Live(evt)`: 3,288 B. That is 339 B per live prop.

1. `InertiaProp.cs:198-202`: `Live(LiveEvent)` calls the `params string[]` overload, a 32 B array per call. Add a private
   single-listener path.
2. `LiveChannel.cs:135`: `Listeners = []` grows to four 24-byte tuples on the first add; one listener is the common case. Use
   `new(1)`.
3. `PropsWriter.cs:1004`: every render allocates a `Dictionary<string, LiveSpec>` and a path string per live prop. Check whether
   a path can be collected twice in one walk; if not, a pooled list of `(path, spec)` replaces the dictionary.

Target: under 200 B per live prop. Tests: existing live-prop metadata tests, plus a prop with two listeners and a nested live prop.

### 2.3 Small items (S each, do together per file)

| # | Where | Now | Fix | Status |
|---|---|---|---|---|
| C1 | `InertiaAntiforgery.cs:118` | `Items[key] = true` boxes 24 B per request | a static boxed value | read |
| C2 | `SsrGateway.cs:525` | new `MediaTypeHeaderValue` per uncached render | static readonly instance | read |
| C3 | `SsrGateway.cs:315` | a stale SSR hit calls `TryAdd`, which locks even when the key is present | check `IsRefreshing(key)` first, as `PropsWriter.cs:573` does | read |
| C4 | `SsrNodeProcess.cs:71` | one global lock and `Process.HasExited` per instance per render | a volatile ready flag; measure first with a 4-instance `Select` benchmark, drop if under 1% | read |
| C5 | `Live.SignalR/InertiaLiveSignalROptions.cs:31` | `ActivatorUtilities.CreateInstance<T>` per hub invocation | `ActivatorUtilities.CreateFactory<T>([])` once | measured: 0.25-0.37 µs to 0.07 µs, same 24 B |
| C6 | `Live.SignalR/LiveOrigin.cs:32-34` | `Uri.TryCreate` per allowed origin per cross-origin request | parse once | read |
| C7 | `EntityFrameworkCore/KeysetExpression.cs:20,38,56` | `Queryable` methods and `CompareTo` found by name per cursor page | static `MethodInfo` fields | read |
| C8 | `Analyzers/InertiaPagesGenerator.cs:45`, `ComponentNameAnalyzer.cs:71` | `ToDisplayString()` per interface per type on every edit | compare `Name` and namespace first; hoist the member lookup | read |
| C9 | `Mvc/RazorInertiaRootView.cs:33` | `new ActionDescriptor()` (a GUID string and two dictionaries) per first visit | one shared instance | read |
| C10 | `Redis/RedisInertiaCacheStore.cs:111` | `Distinct().ToArray()` builds a hash set for one to three tags | skip when there are fewer than two tags | read |
| C11 | `BigIntegerConverters.cs:32,48` (only with `PreserveBigIntegers`) | a name string per object property; raw text and a failed parse per decimal | `property.WriteTo` for non-numbers; check the raw bytes for `.`/`e` before parsing | read |

C5 is cheap but hub invocations are rare; it is in because it is one line. C8 affects the IDE, not the app.

### 2.4 Benchmark coverage for what 2.1-2.3 touch

Added to `tests/Inertia.Net.Benchmarks`, in the style of the existing probes, and recorded in a new RESULTS.md section with
before and after rows:

- `--feature-probe`: HTML and JSON with five head tags, five live props, and the hub authorizer factory. (The throwaway probe
  used for the numbers above did exactly this: `BenchApp.Props20()` plus `WithHead(...)`, and five `Prop(() => ids).Live(evt)`.)
- A pagination row in `PaginationProbe` that counts fetch and count calls for a first, middle and last page.
- A Redis tag-set check in `Inertia.Net.Redis.Tests` (the A1 test); no separate benchmark.
- An antiforgery row (GET through `UseInertiaAntiforgery`) so C1 and later changes are visible.

## 3. Order

1. Add the 2.4 fixtures and record the baseline on `main`.
2. A1, A2, A3 (each its own commit, with its test).
3. B1, then B2.
4. C1-C11, grouped by package.
5. Re-run the probes, write the RESULTS.md section, update CHANGELOG `[Unreleased]`.
6. Verification as in `AGENTS.md`: build with `-warnaserror`, the full test run, `bash eng/aot-smoke.sh` (A1's Lua and C5 touch
   AOT-covered paths), and the Redis and MongoDB test projects against real servers.

## 4. Looked at and left alone

- The main render walk (`PropsWriter`, `InertiaPageWriter`, the middleware): already at 1.7 KB for 20 props. The open
  experiments from plan 3b.3 (primitive leaf fast path, pooled async state machines, nonce tags) are larger than a patch.
- DevTools, TypeScript and routes generation, `PageComponentCheck`: nothing runs per request in production.
- Metrics and activities: guarded by `Enabled`; bool tags are pre-boxed.
- Redis get, set and remove: one round trip each; scripts are cached by StackExchange.Redis.
- EF Core: `EF.Parameter` keeps one cached plan across cursors; compiled queries do not fit a user-supplied `IQueryable`.
- MongoDB `$facet` to merge count and page: capped at 16 MiB and not shown to be faster; A2 removes the count where it can.
- SignalR group sends: the message is serialized once per protocol per send (`SerializedHubMessage`).
- `PagesFrom(assembly)` reflecting at production startup: a few milliseconds once; moving it changes when `Pages` is filled.
- `AsNoTracking` in the EF pager: a behavior change, not for a patch.
- .NET 11-only APIs: `Utf8JsonWriter.Reset(JsonWriterOptions)` is the only relevant one and the writer pool does not need it.
  .NET 11's runtime gains (JSON escaping, antiforgery tokens about 20% cheaper) arrive without code changes.
- A CI regression gate on allocated bytes (`benchmark-action/github-action-benchmark`): useful, but tooling rather than 1.9.1.

## 5. Sources

- Redis scripting and transactions: <https://github.com/StackExchange/StackExchange.Redis/blob/main/docs/Scripting.md>,
  <https://github.com/StackExchange/StackExchange.Redis/blob/main/docs/Transactions.md>; ASP.NET Core's Redis output cache store
  (tags as expiring sets with periodic cleanup): <https://source.dot.net/Microsoft.AspNetCore.OutputCaching.StackExchangeRedis/RedisOutputCacheStore.cs.html>
- SignalR group fan-out: <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/server/Core/src/DefaultHubLifetimeManager.cs>
- EF Core pagination and compiled queries: <https://learn.microsoft.com/en-us/ef/core/querying/pagination>,
  <https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics>
- MongoDB count and `$facet`: <https://www.mongodb.com/docs/drivers/csharp/current/crud/query/count/>,
  <https://www.mongodb.com/docs/manual/reference/operator/aggregation/facet/>
- Incremental generators: <https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md>
- .NET 11 performance and antiforgery: <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-11/>,
  <https://github.com/dotnet/aspnetcore/pull/64751>
- BenchmarkDotNet arguments and comparison: <https://github.com/dotnet/BenchmarkDotNet/blob/master/docs/articles/guides/console-args.md>,
  <https://github.com/dotnet/performance/blob/main/src/tools/ResultsComparer/README.md>
