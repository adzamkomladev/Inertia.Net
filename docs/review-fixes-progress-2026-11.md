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

## Phase 4

Enabled private PublicApiAnalyzers 5.6.0, recommended analysis and build-time style checks. Six assembly packages track 781
canonical signatures; the content-only template package has no assembly surface. No compatibility baseline or obsolete
forwarders were added. Existing hosted-service deduplication and the Testing framework reference remain intact, with a repeated
registration regression. Culture-invariant testing helpers have a custom-negative-sign regression.

Renamed FastEndpoints Config setup to `ConfigureInertia`, preserving the previous configurator before Inertia setup and the
optional callback afterward. FastEndpoints 8.3's getter is internal; the narrow static accessor is verified by both the regression
and an actual Native AOT startup assertion. FastEndpoints tests share their framework's required serialized collection.

Version, template package pin, docs package pin and docs header are 1.6.3. Strict build passed with zero warnings/errors, all
1,494 tests passed with none skipped, strict pack produced seven packages and six symbol packages without analyzer leakage,
and actual Native AOT smoke passed without IL warnings. Docs generation/client+SSR build and browser suites passed:
React 25; Vue/Svelte 14 each with 11 framework-specific skips each. Phase 3 PR #37 merged after all CI checks succeeded.

## Phases 5 and 6

Updated root/package READMEs and the feature guides to current APIs, removed version/migration pages and regenerated navigation,
search and llms exports. Installation guides explicitly support npm, Bun, pnpm and Yarn. The first-ten-minutes guide now includes
an actual starter form and a cookie-preserving XSRF round-trip test. Precognition's HTTP, three client examples and test use the
sample login route; the copied server rate-limit and route methods match the sample byte for byte. Client snippets are abridged
examples and demonstrate a longer debounce setting for expensive rules.

The React sample has a labeled email/password demo with blur validation, accessible errors and processing feedback. Separate
bounded per-client buckets allow 30 validations and five submissions per minute. The browser regression checks an actual 422,
seven further successful blur validations without executing the handler, five real submissions, and both 429 response paths.
The sample opts 429 into error pages explicitly. Its reset endpoint gives each E2E test fresh limiter partitions.

Final local checks: strict Release build (zero warnings/errors), 1,494 tests passed with none skipped, strict pack (seven packages
and six symbol packages), Native AOT smoke without IL warnings, three frontend builds (React includes SSR), and regenerated
docs client/SSR build. Playwright: React 26 passed; Vue/Svelte 14 each with 12 expected framework-specific skips each. Initial
parallel browser processes collided in their shared output directory; isolated-output reruns passed, without source changes.
The docs browser checks opened 20 changed pages, checked all three Precognition tabs, highlighting, search and llms.txt;
subsequent snippet edits were rebuilt, with the changed examples opened again. README local links and diff whitespace checks passed.

Desktop/mobile default, invalid and 429 login captures passed review with no clipping. Screenshots of each sample's initial page
and invalid form were captured. Build logs, docs captures and sample captures are preserved in the task's external verification
folder so worktree cleanup cannot remove them. The design detector returned no findings; inherited native sample styling was
retained. Existing docs design-sidecar drift was reported, without changing the docs site's design system.

The isolated slow-renderer browser trace and upstream protocol follow-up list are in
[phase 6 evidence](review-fixes-phase6-evidence-2026-11.md). On one verified reused HTTP/1.1 connection, a stale SSR response took
18.4 ms and the next request 18.6 ms while a 4,004 ms real renderer refresh remained active. This is ordering evidence, not a
throughput or general latency claim. Streaming remains excluded from 1.6.3 and belongs to the separate phase 7 follow-up.
Phase 4 PR #38 merged after all CI checks succeeded.

Phase 5 PR #39 merged after every CI check succeeded, including Linux/Windows tests, all three sample browser suites,
all three starter templates, docs, Native AOT and CodeQL. GitHub’s reported Actions incident delayed final check statuses;
merging waited for all checks to report success. The release branch now includes all five phase PRs; phase 6 evidence is committed.
