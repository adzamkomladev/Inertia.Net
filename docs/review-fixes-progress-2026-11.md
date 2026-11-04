# November review fixes: execution record

Release target: 1.6.3. Baseline: fetched `origin/main`, `2dd4a4b` (1.6.2).

## Phase 1

Implemented cached/live dot-parent resolution, bounded background refresh, SSR connection timeout, defensive cache-tag
publication, missing validation registration diagnostics, and generic public SSR exception messages. Background stale loaders
require singleton dependencies or a scope created inside the loader; this lifetime change was approved on 2026-11-04.
The plan was reassessed against released 1.6.2 source; the research memos record corrected assumptions.

Regression evidence before fixes: cached sequential dot parent loaded twice; parallel parent lost its cached value; live parent
lost listener metadata; response completion and the next HTTP/1.1 request waited for SSR refresh; default handler connection
establishment was unbounded; SSR exceptions disclosed renderer diagnostics; missing validation resolvers passed silently.
Real MemoryCache rejected-replacement tests already passed on the baseline; tag publication is a defensive improvement.

Checks completed:

- `dotnet build Inertia.Net.slnx -c Release -warnaserror`: passed, zero warnings/errors.
- `dotnet test --solution Inertia.Net.slnx -c Release --no-build`: 1,413 passed, none failed/skipped.
- `dotnet pack Inertia.Net.slnx -c Release --no-build -warnaserror -o artifacts`: passed, all seven packages and symbol packages.
- `bash eng/aot-smoke.sh`: passed, no trim/AOT warnings.
- Playwright: React 25 passed; Vue and Svelte each 14 passed with 11 framework-specific skips.
- Three sample frontend clean installs/builds: passed; React included its SSR bundle.
- Docs generation and client/SSR build: passed (34 pages).
- Ordinary render allocation probe: unchanged at JSON 1,736, POCO 1,552, partial 568, HTML 1,800 B/request.
- Prop cached/tagged/stale allocation: unchanged at 560/624/576 B/request; SSR cached/stale unchanged at 664 B/request.
  Raw standalone probes are under `tests/Inertia.Net.Benchmarks/results/2026-11-review-fixes`; concurrent work makes their timings
  exploratory. Final paired measurements remain pending. The stale probe measures hits while a refresh claim is retained,
  not background worker execution; the gated connection regression covers that behavior.

The documentation also explicitly supports npm, Bun, pnpm and Yarn, with equivalent install/dev/build commands. Repository CI
continues using its committed lockfiles and reproducible install commands.

## Phase 2

Implemented public Precognition request detection and endpoint metadata, rate-limit policy regression coverage, XSRF testing
helpers and validation-error assertions, schema-aware error normalization (including generated aliases), fluent prop rules,
DevTools recording diagnostics and bounded SignalR subscriptions/channel names/socket IDs with proxy diagnostics.
MVC error keys use the page serializer's policy; FastEndpoints retains its binding serializer policy. Custom metadata without
CLR member providers and ambiguous body schemas require client JSON names for aliases. Native AOT smoke now exercises alias
and indexed Precognition error paths explicitly.

Regression tests failed before the corresponding fixes; the phase 1 cache/live/merge and released pagination/error-page tests
remain covered. Checks: Release build with zero warnings/errors; all 1,457 tests passed with none skipped; seven-package pack;
AOT smoke with no trim/AOT warnings; docs generation and client/SSR build; Playwright React 25 passed and Vue/Svelte each
14 passed with 11 framework-specific skips. Phase 1 PR #35 merged after every CI check succeeded.

## Phase 3

Implemented converter-aware dictionary key formatting with built-in fast paths, allocation-free safe-integer DOM output,
segmented/escaped numeric reads, parallel failure path logging and linked cancellation, elapsed-time circuit breaking, and
measured scroll/member-wrapper/fill-waiter allocation reductions. Retain converter instance identity. Planned cache-hit
memoization was rejected: it saved allocation but served a value after a preceding loader invalidated its key/tag.
Traversed read-only dictionary contracts with derived base-type key instances need a mutable dictionary; unsupported factories
fail explicitly instead of silently emitting `ToString()` keys.

Checks: Release build with zero warnings/errors; all 1,491 tests passed with none skipped; seven-package pack; AOT smoke
(including GUID and read-only date keys), docs generation/client+SSR build, and three browser suites passed. React 25 and
Vue/Svelte 14 each (11 framework-specific skips each). Two paired processes each ran 32 rounds; integer keys allocate 9,904 B
less per 1,000 entries and GUID keys 408 B less per 500 rows. Ordinary render allocation is unchanged. Parallel path reporting
adds 160 B per 20 completed loaders; yielding loaders show net 16 B less after the entry-loop optimization. Timings, intervals,
raw samples, isolated DOM/small-allocation results and limitations are recorded in the benchmark RESULTS document.
