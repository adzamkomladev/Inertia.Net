# Inertia.Net benchmark results

## 1.4 caches and per-response settings — 2026-10-20

Measured on the Windows laptop (i5-8265U, .NET 10.0.13, Release). "Before" is commit `3adc319`
(1.3.0) built from a separate worktree with the same probe source.

| Workload (probe) | 1.3.0 | 1.4.0 |
|---|---:|---:|
| JSON, 20 mixed props (`--render-probe`) | 2,752 B | 2,760 B |
| Typed POCO page (`--render-probe`) | 2,472 B | 2,480 B |
| Partial reload, 2 of 20 (`--render-probe`) | 1,584 B | 1,592 B |
| HTML + Vite tags, 20 props (`--render-probe`) | 2,800 B | 2,824 B |

Timings, from BenchmarkDotNet (`--filter '*RenderBenchmarks.Json*'`), alternating 1.3.0 and 1.4.0 twice from clean worktrees:

| Benchmark | 1.3.0, run 1 | 1.4.0, run 1 | 1.3.0, run 2 | 1.4.0, run 2 |
|---|---:|---:|---:|---:|
| JSON, 20 mixed props | 17.13 ± 0.86 µs | 18.24 ± 0.68 µs | 31.25 ± 3.93 µs | 17.09 ± 0.51 µs |
| JSON, typed POCO props | 11.38 ± 0.26 µs | 20.31 ± 3.29 µs | 10.59 ± 0.20 µs | 13.13 ± 0.98 µs |

The laptop's load changes between runs (the same build differs by up to 2x), so these runs cannot resolve a difference of 10%. The
20-prop page is level. The typed page was slower in both 1.4.0 runs, with wide errors, while the alternating render probes had it
level; re-measure on a quiet machine before relying on either. Allocation is exact and is the stable signal.

| Workload (`--cache-probe`) | Without the cache | With the cache |
|---|---:|---:|
| A prop whose loader maps 500 rows | 25,496 B; 123–170 µs | 1,136 B; 5.0–6.3 µs |
| A server-rendered page (64 KiB of HTML, in-memory SSR server) | 3,448 B; 129–221 µs | 1,328 B; 11.6–22.3 µs |

What changed:

- **Per-response settings.** `InertiaResult` holds the SSR cache duration (8 bytes, a `TimeSpan` with a sentinel rather than a
  nullable, which takes 16), and `InertiaRootViewContext` holds it and the CSP nonce (16 bytes). That is the whole allocation difference
  of an ordinary render; the cache checks themselves allocate nothing when no prop uses `Cache()`.
- **Cached props.** A hit writes the stored HTML-safe JSON with `WriteRawValue`: no loader, no serialization. The probe's loader does no
  I/O, so a real query makes the gap wider.
- **SSR cache.** A hit costs a SHA-256 of the page JSON and one `SsrRender`; the probe's SSR server answers from memory, so a real Node
  render (milliseconds) makes the gap far wider.

Reproduce:

```sh
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --render-probe
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --cache-probe
```

## Antiforgery, SSR and MVC streaming changes — 2026-10-18

Measured in a cloud Linux container (Intel Xeon @ 2.10 GHz, 4 vCPUs, SDK 10.0.402, .NET 10.0.13), a different and noisier host than
the 1.1 laptop runs below, so compare rows within this section only. Each probe runs three repetitions in one process; "before" is
commit `2b99400` (1.1.0) or, for the large-page probe, the commit before the change, built from a separate worktree with the same
probe source. Allocation is exact; timings vary by tens of percent between runs, so ranges are given.

| Workload (probe) | Before | After |
|---|---:|---:|
| 1 MiB SSR response, gateway (`--ssr-probe`) | 2,100,575 B; 1.36–1.57 ms | 1,051,738 B; 0.48–0.57 ms |
| 1 MiB SSR response, 1.0 buffered reference (`--ssr-probe`) | 4,198,038 B; 2.05–2.45 ms | unchanged (reference) |
| MVC `<inertia />` body, 1 MiB page (`--mvc-probe`) | 3,146,347 B; 1.33–1.37 ms | 80 B; 0.12–0.19 ms |
| MVC `<inertia />` body, 2 KiB page (`--mvc-probe`) | 6,576 B; 1.9–7.8 µs | 80 B; 0.5–0.8 µs |
| HTML render, one 1 MiB prop (`--payload-probe`) | 785–1,107 µs | 581–886 µs |
| JSON render, one 1 MiB prop (`--payload-probe`) | 298–672 µs | 434–568 µs (no clear change) |
| Typed POCO page (`--render-probe`) | 2,592 B; 8.1–10.3 µs | 2,472 B; 6.5–7.9 µs |
| JSON, 20 mixed props (`--render-probe`) | 2,752 B; 11.0–27.5 µs | 2,752 B; 11.3–18.1 µs |
| Partial reload, 2 of 20 (`--render-probe`) | 1,584 B; 5.7–6.8 µs | 1,584 B; 4.7–5.7 µs |
| HTML + Vite tags, 20 props (`--render-probe`) | 2,792 B; 13.5–14.4 µs | 2,792 B; 10.8–14.7 µs |

What changed:

- **SSR.** The response is read into a pooled buffer and `head`/`body` are unescaped once into UTF-8 arrays that the root view writes
  as-is. The 1.1 gateway materialized UTF-16 strings (twice the bytes) and re-encoded them on output; the output re-encoding is not
  part of this probe, so the end-to-end saving is larger than the table shows. `MaxResponseBytes` still bounds the read.
- **MVC.** The `<inertia />`, `<vite />` and `<vite-react-refresh />` tag helpers write pre-encoded UTF-8 through `IHtmlContent` in
  pooled 4 KiB chunks instead of building strings. The probe writes both forms to a `StreamWriter` the way Razor's response writer
  encodes them. The 80 B left is the content object and the decoder.
- **Large pages.** The page buffer starts at each component's recent page size (plus 1/8, at least 4 KiB, at most 8 MiB; it shrinks
  back gradually), so a large page skips the doubling copies from 4 KiB. The HTML render improved; the JSON measurement did not separate
  from noise. Pooled buffers mean this shows in time, not allocation. The probe's HTML allocation (about 546 KiB) comes from the test
  `StreamPipeWriter` over `Stream.Null`; Kestrel's response pipe pools large writes (see the 1.1 Kestrel table).
- **Typed objects.** Members are collected into a pooled array instead of a `yield` iterator: one enumerator less per typed object.
- **Small pages** are otherwise unchanged in allocation; their timings stayed within run-to-run noise.

Not measured with a probe: the session and TempData stores skip loading without their cookie (one distributed-cache read or TempData
decrypt less per request through `UseInertia()`), the SSR bundle check is cached (one `stat` less per server-rendered page), and
`Location()` reads one header instead of parsing nine.

Reproduce:

```sh
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --render-probe
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --ssr-probe
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --mvc-probe
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --payload-probe
```

## 1.1 correctness and resource measurements — 2026-10-13

These are local Release measurements, not a production capacity guarantee. The environment was Windows 11
10.0.26300.9550, Intel Core i5-8265U (4 cores/8 threads), SDK 10.0.401, .NET 10.0.12, concurrent workstation GC,
and BenchmarkDotNet 0.15.8. Laptop timing varied substantially; allocation results were more stable.
The in-process toolchain avoids duplicate benchmark project discovery in local worktrees.

Raw measurements are retained in [microbenchmarks](results/1.1-microbenchmarks.csv),
[render probes](results/1.1-render-probe.csv), [SSR probes](results/1.1-ssr-probe.txt), and
[Kestrel load results](results/1.1-kestrel.json). CSV timing/allocation units are retained as printed by BenchmarkDotNet;
its `KB` allocation unit means 1,024 bytes. Error columns expose the uncertainty of the short runs.

### Small pages and regression investigation

The baseline is the repository at `6c3b3ae`. Original production source was extracted into an isolated directory;
only the identical `--render-probe` measurement entry was added to its benchmark project. Three alternating baseline/current
Release processes ran the same mixed 20-prop JSON, partial (`users,count`), and template HTML workloads.
Each method warmed for 2,000 requests and measured 100,000 requests per repetition, with three repetitions per process.
The table uses the mean of repetitions 2 and 3 in each process, then the median of the three process results;
the first repetition, which includes the observed tiered-JIT transition, remains in the raw data.

| Warm render | Original | 1.1 | Original allocation | 1.1 allocation |
|---|---:|---:|---:|---:|
| JSON, 20 mixed props | 12.95 µs | 13.43 µs | 2,744 B | 2,752 B |
| Partial reload, 2 of 20 | 6.10 µs | 6.15 µs | 1,576 B | 1,584 B |
| HTML with Vite tags, 20 props | 14.67 µs | 14.62 µs | 2,784 B | 2,792 B |

An initial short BenchmarkDotNet comparison showed a partial-render regression above 10%. Investigation found
array checks resolving metadata before filtering omitted loaders. Filtering now comes first; omitted types do not
require registered serializer metadata, covered by a regression test. Final short-run partial timings still ranged
from 5.87–8.16 µs (baseline 6.10–7.19 µs). The longer controlled probes did not show a repeatable regression above 10%.
The additional shared-index reference costs 8 bytes on these small pages; the index itself is not allocated.

Inertia middleware now allocates 48 B for write tracking even when an endpoint writes no body; stream/pipe wrappers
are created only when used. The three short-run empty-endpoint measurements were 0.80–1.51 µs.
Non-Inertia middleware requests remained at 0 B and 0.23–0.26 µs. These checks are separate from the render table,
which executes `InertiaResult` directly and does not include the HTTP middleware.

### Payload sizes and shared-prop lookup

The matrix covers 20, 200 and 2,000 props, logical string payloads of 1 KiB, 100 KiB and 1 MiB, and 2 or 32 shared props.
Each combination runs full JSON, typed rows with a loader in each row, and a one-prop partial response.
Logical payload size excludes JSON keys and structural overhead. Three comparable Release runs used one warmup,
three measured 100 ms iterations and the short in-process job.

The 32-shared-prop workload identified repeated scans while building page entries. Pages with at least 200 props
and a shared prefix of at least 32 entries now index shared positions. Smaller pages retain the scan.
For 2,000 props, 32 shares and a logical 1 MiB payload:

| Workload | Before index: median (range) | Final: median (range) | Allocation before → final |
|---|---:|---:|---:|
| Full JSON | 913.8 µs (911.5–1,123.9) | 731.9 µs (620.4–736.2) | 48.55 → 49.66 KiB |
| One-prop partial | 341.9 µs (315.4–347.0) | 143.3 µs (120.4–152.3) | 48.57 → 49.69 KiB |

That is about 20% less full-render time and 58% less partial-render time, with about 1.1 KiB more allocation.
The partial improvement includes the earlier filtering correction. Typed-row pages contain one parent page prop,
so they do not create the index; loading a selected array still loads all its nested loaders under the whole-array contract.
The full matrix and intermediate results are retained rather than extrapolating these improvements to every page.

### SSR streaming and response bounds

The benchmark compares the previous HTTP buffering/read-to-byte-array/deserialization sequence with the real streaming
gateway, using identical source-generated metadata, page bytes, timeout tokens, client factory and a controlled HTTP handler.
The body contains a 1 MiB ASCII string plus JSON framing. It excludes Node rendering and final HTML output.
Three comparable Release runs used one warmup and three measured 100 ms iterations.

| 1 MiB SSR response | Median time (range) | Allocated/request |
|---|---:|---:|
| Buffered reference | 791.4 µs (743.5–831.8) | 4,101.1 KiB |
| Streaming gateway | 941.7 µs (913.5–974.0) | 2,052.5–2,052.8 KiB |

Warm allocation fell by approximately **50%**, exceeding the 40% acceptance target. The microbenchmark's median CPU
time increased by about 19%. A separate three-batch probe (20 warmups, 100 requests per batch) measured 4.20 MB versus
2.10 MB allocated/request and 1.20–1.30 ms versus 1.17–1.28 ms; it also records the first call separately.
This is a memory improvement with a workload-dependent time tradeoff, not an across-the-board speed claim.

`SsrBufferingBenchmarks` disables forced GC between iterations to measure warm pooled-buffer reuse. An exploratory
forced-GC run allocated about 3.79 MiB in streaming, so cold or pressured pools need not achieve the warm 50% saving.
The production byte limit works independently of pooling: declared oversized responses fail before reading, while
unknown or misleading lengths are counted. Success, error, malformed JSON, timeout and cancellation paths have regressions.
The matrix includes oversized fallback, a controlled timeout and request cancellation. A 1 ms configured timeout took
about 15 ms on this Windows timer; it is not a claim of millisecond scheduling precision.

UTF-8 HTML output reserves space for bounded chunks rather than the maximum encoded size of an entire SSR string.
Prop JSON remains buffered so loader failure cannot leave a partly written successful response.

### Redirect state and MemoryPack evaluation

Restored and pending flash/errors now share their effective dictionaries. Pending keys win, restored missing keys are
added once, and getters do not rebuild merged dictionaries. The state-check benchmark allocates 0 B; its near-zero
reported duration can be optimized away and is not a useful speed measurement. Redirect-chain and precedence tests
verify behavior. No cookie, session or TempData format changed.

MemoryPack 1.21.4 is a **benchmark-only** dependency. The compared envelope has 20 restored flash values, 20 error fields,
a pending error override, history flags, and a varying flash string. Arbitrary flash values still require JSON encoding
inside the binary envelope. Setup checks restored state against JSON with `JsonElement.DeepEquals`.
Cookie measurements include data protection and Base64URL encoding with the same protector.

| 1 KiB flash workload | JSON | MemoryPack envelope |
|---|---:|---:|
| Stored bytes | 1,780 B | 2,164 B |
| Protected Base64URL characters | 2,459 | 2,971 |
| Serialize allocation | 6,632 B | 4,824 B |
| Restore allocation | 16,233 B | 17,281 B |
| Protect/encode allocation | 13,490 B | 13,090 B |
| Protect/encode median time | 9.60 µs | 10.80 µs |

Serialization allocation improved by 27%, but protected-cookie allocation improved by only 3%, restoration allocation
increased, and the cookie became larger. With a 16 KiB flash value, serialization allocation improved by about 53%,
but restoration allocation increased by about 52%; protection/encoding was still slower in all three runs.
The binary envelope was 384 bytes larger than JSON at every tested size. These results do not justify a production
codec dependency or storage migration for this workload. They do not rule out MemoryPack for a different, fully typed
application-owned cache. Browser and SSR page transport retain the Inertia JSON protocol.

### Kestrel concurrency and retained memory

`--load` starts an owned, separate Kestrel process on an ephemeral loopback port and stops its process tree afterwards.
The client and server run on the same laptop, so CPU and loopback transfer limit these results. Server allocation counters
exclude client allocations. The endpoints render the mixed 20-prop page or one 1 MiB ASCII string, with two shared props.
For each endpoint, the first request is recorded separately, then 100 warmups precede concurrency levels 1, 32 and 128.
Each level has three two-second traffic windows followed by one second idle. No forced GC runs between windows.

| Payload | Concurrency | Median requests/s | Median p50 / p95 / p99 (ms) | Allocation/request median (range) | GC collections across three windows: Gen0 / 1 / 2 |
|---|---:|---:|---:|---:|---:|
| 20 mixed props | 1 | 3,296 | 0.28 / 0.48 / 0.72 | 3,237 B (3,236–3,245) | 17 / 1 / 0 |
| 20 mixed props | 32 | 22,641 | 1.28 / 2.69 / 3.86 | 3,237 B (3,236–3,267) | 118 / 1 / 1 |
| 20 mixed props | 128 | 25,571 | 4.54 / 8.40 / 10.45 | 3,237 B (3,236–3,263) | 171 / 1 / 0 |
| 1 MiB string | 1 | 529 | 1.36 / 2.27 / 17.73 | 2,221 B (2,212–6,711) | 2 / 1 / 0 |
| 1 MiB string | 32 | 951 | 29.55 / 59.43 / 86.55 | 2,216 B (2,214–91,407) | 7 / 1 / 1 |
| 1 MiB string | 128 | 1,048 | 117.62 / 177.59 / 221.01 | 4,146 B (2,221–225,134) | 7 / 2 / 1 |

Each percentile column is the median of that percentile across the three windows, not a percentile over pooled requests.
The first small-page request took 297 ms, including connection establishment and JIT/metadata initialization.
The first large-endpoint request took 5.95 ms after the small-page workload had already warmed the process;
it is an endpoint-first observation, not a second cold process. The first traffic window at a new concurrency can
still include tiered compilation, connection creation and pool growth; all windows remain in the raw report.

Small-page working sets were approximately 69–79 MiB. Large-page working sets grew from 79 MiB at concurrency 1 to
144 MiB at 32 and 354 MiB at 128, retaining approximately those levels after one second idle. The final GC snapshot
at 128 showed about 669 MiB of managed heap and 531 MiB of LOH; these GC snapshots describe the last collection,
not a current live-object measurement, and need not equal resident working set. The process peak working set reached
354 MiB. This shows why warmed allocation/request alone is not a peak-memory estimate.

Runtime `GCAllocationTick` events sampled about 132 MiB of LOH allocation in the first 32-way large-page window and
377 MiB in the first 128-way window, versus no sampled LOH allocations in their final windows. The raw file retains
sample byte counts, event counts, last-GC LOH size, heap/committed memory, peak working set and idle working set.
These are sampled allocation events, not exact object counts; the event listener and metrics endpoint add measurement
overhead. One-second idle retention is not a memory-leak diagnosis or a guarantee about later pool trimming.
Application payload and concurrency limits still matter. The SSR limit bounds each SSR response, not total page-prop
memory or aggregate concurrent requests. This run establishes a local load baseline; no original-version load comparison,
multi-host load, long soak or production deployment was measured.

### Reproducing the runs

Run from the repository root:

```sh
dotnet build Inertia.Net.slnx -c Release -warnaserror
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --render-probe
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --ssr-probe
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --load artifacts/kestrel.json
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --filter '*PayloadBenchmarks*' '*SsrBufferingBenchmarks*' '*StateBenchmarks*' '*MiddlewareBenchmarks*' --inProcess --job short --warmupCount 1 --iterationCount 3 --iterationTime 100
dotnet run --project tests/Inertia.Net.Benchmarks -c Release --no-build -- --filter '*RenderBenchmarks.Json20Props*' '*RenderBenchmarks.PartialReload*' '*RenderBenchmarks.Html20Props*' --inProcess --job short --warmupCount 2 --iterationCount 5 --iterationTime 500
```

Repeat measurement processes three times on an otherwise quiet host. Use the raw ranges and longer probes when
short-run timings disagree; investigate repeatable small-page regressions above 10% before expanding optimizations.

## Historical 1.x measurements

The following results and allocation-tuning history predate 1.1. In particular, the middleware now allocates a write tracker.

Run with:

```sh
cd tests/Inertia.Net.Benchmarks
dotnet run -c Release -- --filter '*' --job short --iterationCount 10
dotnet run -c Release -- --check   # prints what each render benchmark produces
```

**Environment.** BenchmarkDotNet 0.15.8, Windows 11 (10.0.26300), Intel Core i5-8265U 1.60 GHz (laptop, 4 cores/8 threads),
.NET SDK 10.0.401, .NET 10.0.12 (RyuJIT x86-64-v3), ShortRun job with 10 iterations. Laptop timings are noisy (expect ±10%);
the allocation numbers are exact and stable.

**What is measured.** Every render benchmark runs `InertiaResult.ExecuteAsync` on a reused `DefaultHttpContext` whose body is
`Stream.Null`: header parsing, shared props, the prop walk, the pooled page buffer, and the copy to the response. The services are
built once, like a running app: `AddInertia` with two shared props (one static, one per request), a source-generated
`JsonSerializerContext` chained into the HTTP JSON options, and a template root view with a Vite manifest.

The props are built once and reused, so the render rows show Inertia.Net's own cost. **Build 20 props** is the extra cost of a
handler creating the same `InertiaProps` on every request.

The 20 props mix literals (string, int, bool, decimal, DateTimeOffset), arrays, two nested dictionaries, a 10-item typed record
array, two lazy props, an async lazy prop, a Task prop, an always prop, an optional prop, three deferred props in two groups, a once
prop and a merge prop. The typed page is a POCO with seven members, including `Defer`, `Once` and `Merge` props.

## Results

| Benchmark | Mean | Allocated |
|---|---:|---:|
| JSON, 20 mixed props | 13.7 µs | 2,744 B |
| JSON, typed POCO props | 9.5 µs | 2,464 B |
| Partial reload, 2 of 20 props | 6.1 µs | 1,576 B |
| Deferred group load (2 props) | 4.7 µs | 1,584 B |
| HTML + Vite tags, 20 props (template view) | 14.7 µs | 2,784 B |
| *Reference: `JsonSerializer.Serialize` of the same page as plain data* | 9.3 µs | 312 B |
| *Build 20 props (handler side)* | 0.9 µs | 4,232 B |
| Middleware: endpoint only (baseline) | 40 ns | 0 B |
| Middleware: `UseInertia`, non-Inertia request | 193 ns | 0 B |
| Middleware: `UseInertia`, Inertia request (version check) | 345 ns | 0 B |
| Parse partial-reload headers (`InertiaRequest`) | 460 ns | 272 B |
| Match 20 dot paths against only + except filters | 190 ns | 0 B |

A full 20-prop render costs about 1.5 times what serializing the finished page as plain data costs, and it allocates about
2.7 KB per request regardless of payload size. The page JSON is written to a pooled buffer, not allocated. The middleware
allocates nothing.

## Allocation tuning (before and after)

The "before" numbers are from the same benchmarks run on the Phase 2 code (main @ 0577c2b).

| Benchmark | Before | After | Change |
|---|---:|---:|---:|
| JSON, 20 mixed props | 6.18 KB | 2.68 KB | −57% |
| JSON, typed POCO props | 3.98 KB | 2.41 KB | −39% |
| Partial reload, 2 of 20 | 4.98 KB | 1.54 KB | −69% |
| Deferred group load | 4.99 KB | 1.55 KB | −69% |
| HTML + Vite tags, 20 props | 6.41 KB | 2.72 KB | −58% |
| Parse partial-reload headers | 536 B | 272 B | −49% |
| Build 20 props (handler side) | 4.48 KB | 4.13 KB | −8% |
| JSON, 20 mixed props (time) | 18.5 µs | 13.7 µs | −26% |
| Partial reload, 2 of 20 (time) | 8.1 µs | 6.1 µs | −25% |

The changes, in the order they were measured:

1. **Round 1** (JSON 20 props: 6.18 KB → 3.44 KB; partial reload: 4.98 KB → 2.30 KB).
   - The prop entry list is sized up front.
   - A cached `errors` prop is used when there are no errors, instead of allocating a dictionary and an `Always` wrapper per request.
   - `InertiaProps`/`Dictionary` values are enumerated with the struct enumerator, not the boxed interface one.
   - `JsonTypeInfo.Properties` is walked with an index, because `foreach` over the `IList` boxes an enumerator.
   - Several `x ?? []` fallbacks are removed. For `List`/`Dictionary`, those allocate a new empty collection on every call (merge metadata, request shared props).
   - Sync `Func<T>` loaders are stored as they are, instead of being wrapped in a closure (−350 B on the handler side).
   - The `X-Inertia-Partial-*` list headers are parsed into an exact-size array, without an intermediate `List` (536 B → 272 B).
   - The template path is cached per root view name, instead of calling `Path.Combine` per request.
2. **Round 2** (JSON 20 props: 3.44 KB → 2.68 KB; partial reload: 2.30 KB → 1.54 KB). The key-to-position dictionary used for
   shared/page prop overrides is gone. Page prop keys are unique by construction, so an override can only hit one of the few
   errors/shared entries at the front of the list, and a linear scan of that prefix is enough.

What remains per request: the `InertiaResult`, the prop entry list, `Utf8JsonWriter`, the props walker, the `sharedProps` list,
the metadata paths that the page actually emits, and boxed value-type results of lazy props.

## Notes for apps

Most per-request allocation in a real handler is the props themselves (see "Build 20 props"), not the render.
