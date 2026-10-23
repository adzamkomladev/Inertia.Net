# Inertia.Net benchmark results

## Published v1.5.0 versus v1.5.1 - 2026-10-23

Both core DLLs were downloaded from their published NuGet packages. Their SHA-256 hashes match the loaded libraries recorded in [paired.json](results/1.5.0-vs-1.5.1/paired.json); [sources.json](results/1.5.0-vs-1.5.1/sources.json) records the package URLs and hashes.
Identical benchmark fixtures, .NET 10.0.13 under WSL2, pinned CPU 7, warmed libraries in separate assembly contexts, 32 alternating-order paired rounds. No builds or other benchmarks overlapped measurement.
Baseline = v1.5.0; current = v1.5.1. Times below are microseconds. Percentages are medians of within-round ratios, so they can differ from ratios of separate time medians. Intervals are exploratory percentile-bootstrap 95% intervals with 5,000 resamples.

| Workload | Before B/op | After B/op | Before us | After us | Paired time change (95% interval) |
|---|---:|---:|---:|---:|---:|
| Json20 | 2760 | 2152 | 74.929 | 75.110 | -0.4% (-2.1% to +1.2%) |
| Partial2Of20 | 1592 | 984 | 31.758 | 31.885 | +1.3% (-1.6% to +2.2%) |
| Html20 | 2824 | 2216 | 79.746 | 77.909 | -1.3% (-2.1% to +1.0%) |
| ReferenceSerialize | 312 | 312 | 39.146 | 39.359 | +0.2% (-0.8% to +1.9%) |
| CacheWrite1KiB | 4880 | 1240 | 1.814 | 1.034 | -43.3% (-48.1% to -36.1%) |
| CacheWrite1MiB | 4195405 | 1048871 | 1131.886 | 485.719 | -56.5% (-59.0% to -51.4%) |
| IntegerKeys1000 | 216992 | 177008 | 1136.159 | 1025.162 | -8.1% (-10.7% to -5.8%) |
| ConstructProps | 4312 | 4232 | 3.339 | 3.323 | -1.2% (-3.0% to +0.4%) |
| Nested20 | 744 | 568 | 9.205 | 9.014 | -1.6% (-3.2% to -0.3%) |
| Nested2000 | 5496 | 568 | 603.290 | 594.951 | -0.6% (-1.6% to +1.1%) |
| ParallelCacheHit1 | 736 | 584 | 4.522 | 4.347 | -4.3% (-7.5% to -0.4%) |
| ParallelCacheHit32 | 3000 | 2104 | 30.961 | 31.444 | +0.2% (-1.2% to +3.5%) |
| ColdBurst32 | 132696 | 83784 | 95836.900 | 12464.150 | -87.0% (-88.4% to -86.4%) |

The cold-burst fixture runs 32 requests with four backend slots and 10 ms simulated asynchronous I/O: v1.5.0 executes 32 loaders; v1.5.1 executes one. Its bytes and times are per burst, not per individual request. Cache write and other measurements are per operation. Large-entry allocation figures contain small background/pool variation.

Ordinary JSON, partial and HTML rendering show no statistically clear release-to-release regression. Cache writes, integer-key dictionaries and cold-cache bursts improve significantly. This comparison supersedes interpreting the earlier incremental memory experiment's approximately 3% cost as a release-to-release regression. That earlier baseline already included the cache optimizations and was not the published v1.5.0 binary.


## Process RSS, GC and retained pool measurements - 2026-10-23

This section compares an intermediate implementation that already included cache optimizations with the final memory changes. It is not a published v1.5.0-versus-v1.5.1 comparison. RSS, collection counts, GC pause time and retained prop-list storage were measured.
The library implementation is unchanged from the render-allocation comparison below; this follow-up adds only a benchmark probe.

Twelve independent processes: three baseline and three updated processes for each workload, 300,000 renders per process after
10,000 warmup renders. Version order alternates per repeat. Ubuntu/WSL2, .NET 10.0.13, workstation GC, affinity CPUs 6 and 7
(.NET reports two processors). The baseline already includes the preceding cache optimizations. Both versions use the same probe.
Each JSON records the loaded core assembly hash. No builds or tests overlap measurement.

`render` reuses the 20-prop fixture and HTTP context. `handler` constructs those props on every render, reusing the context.
Both execute the adapter result into Stream.Null, not a Kestrel/network load test. RSS is observed after warmup/full GC, during the
loop every 4,096 renders, at completion, and after a final full GC. Peak RSS is the maximum of those in-loop samples, not a kernel
high-water mark. Natural Gen 0/1/2 counts and cumulative stop-the-world pause duration come from the runtime; the forced warmup
and final collections are excluded from their measured deltas. Process sampling adds about 3.3 allocated bytes per render equally
to both variants. GC heap figures after full GC include small common probe/inspection overhead.

All table cells give the median followed by the observed range across the three processes. MiB = 1,048,576 bytes; KiB = 1,024 bytes.


render - 300,000 operations per process, 3 independent processes per version
| Metric | Baseline median [min-max] | Updated median [min-max] |
|---|---:|---:|
| RSS before (MiB) | 67.574 [67.508-67.812] | 67.891 [67.605-67.988] |
| Sampled peak RSS (MiB) | 74.219 [74.180-74.223] | 74.383 [74.344-74.691] |
| RSS after (MiB) | 74.219 [74.180-74.223] | 74.383 [74.344-74.691] |
| RSS after full GC (MiB) | 74.402 [74.359-74.406] | 74.609 [74.562-74.910] |
| Managed live heap before (KiB) | 326.977 [326.969-326.977] | 328.445 [328.445-328.445] |
| Managed live heap after full GC (KiB) | 343.422 [343.391-343.422] | 345.359 [345.359-345.367] |
| GC committed before (MiB) | 4.676 [4.676-4.676] | 4.676 [4.676-4.676] |
| GC committed after full GC (MiB) | 4.738 [4.738-4.738] | 4.738 [4.738-4.738] |
| Total allocated (MiB) | 790.591 [790.586-790.592] | 616.641 [616.641-616.641] |
| Allocated bytes/render | 2763.317 [2763.298-2763.318] | 2155.315 [2155.315-2155.317] |
| Gen 0 collections | 264.000 [264.000-264.000] | 206.000 [206.000-206.000] |
| Gen 1 collections | 0.000 [0.000-0.000] | 0.000 [0.000-0.000] |
| Gen 2 collections | 0.000 [0.000-0.000] | 0.000 [0.000-0.000] |
| Total GC pause (ms) | 57.110 [56.689-61.575] | 43.917 [43.384-45.144] |
| GC pause fraction (%) | 0.898 [0.833-0.918] | 0.730 [0.703-0.744] |
| Elapsed (ms) | 6360.602 [6174.090-7392.541] | 6067.265 [5946.570-6244.040] |

handler - 300,000 operations per process, 3 independent processes per version
| Metric | Baseline median [min-max] | Updated median [min-max] |
|---|---:|---:|
| RSS before (MiB) | 67.914 [67.906-67.977] | 67.832 [67.668-68.086] |
| Sampled peak RSS (MiB) | 74.480 [74.473-74.684] | 74.551 [74.438-74.734] |
| RSS after (MiB) | 74.480 [74.473-74.684] | 74.551 [74.438-74.734] |
| RSS after full GC (MiB) | 74.660 [74.656-74.867] | 74.773 [74.656-74.957] |
| Managed live heap before (KiB) | 326.969 [326.969-326.977] | 328.453 [328.453-340.453] |
| Managed live heap after full GC (KiB) | 343.422 [343.391-343.469] | 345.383 [345.383-345.422] |
| GC committed before (MiB) | 4.676 [4.676-4.676] | 4.676 [4.676-4.738] |
| GC committed after full GC (MiB) | 4.738 [4.738-4.738] | 4.738 [4.738-4.738] |
| Total allocated (MiB) | 2024.259 [2024.259-2024.264] | 1827.426 [1827.403-1827.426] |
| Allocated bytes/render | 7075.298 [7075.297-7075.316] | 6387.318 [6387.237-6387.318] |
| Gen 0 collections | 676.000 [676.000-676.000] | 610.000 [610.000-610.000] |
| Gen 1 collections | 0.000 [0.000-0.000] | 0.000 [0.000-0.000] |
| Gen 2 collections | 0.000 [0.000-0.000] | 0.000 [0.000-0.000] |
| Total GC pause (ms) | 148.246 [135.222-157.999] | 132.967 [117.186-135.476] |
| GC pause fraction (%) | 1.955 [1.900-2.024] | 1.737 [1.699-1.897] |
| Elapsed (ms) | 7582.408 [6679.336-8316.870] | 7140.681 [6748.240-7827.974] |

RSS does not improve in this workload. Median sampled peak RSS increases by 0.164 MiB (0.22%) for render-only and 0.070 MiB
(0.09%) when constructing props. GC committed memory after full GC stays at 4.738 MiB in both versions. Managed live heap after
full GC is about 1.94 KiB larger for render-only and 1.96 KiB larger for handler construction.

Gen 0 collections fall 264 -> 206 (22.0%) for render-only, and 676 -> 610 (9.8%) with prop construction. Median total natural
GC pause time falls 57.110 -> 43.917 ms (23.1%) and 148.246 -> 132.967 ms (10.3%), respectively, per 300,000 renders. Neither
version has natural Gen 1 or Gen 2 collections in these runs. These are total pauses, not maximum or p99 single-pause durations.
Elapsed batch time is reported for completeness; these separate-process sustained runs do not replace the earlier paired latency
comparison or establish a universal throughput gain.

Two additional isolated retention inspections read the pool after ordinary rendering and after 32 simultaneous renders held at an
async loader. Pool queue slots are inspected without enumerating/freezing the queue. Every list count, capacity and backing-array
entry is checked. List/array sizes are measured through allocation counters using equivalent objects of those exact capacities;
fixed infrastructure is sized with the same platform pool constructor and a fieldless policy. This includes the pool, policy,
delegates, queue and its one segment. The probe verifies stable sizing, a single segment, the capacity/count bounds, and cleared
references. It does not use a heap dump. The two-CPU run has a maximum of four retained lists.

| Pool state | Baseline total bytes | Updated retained lists | List capacities | Updated list/array bytes | Fixed infrastructure bytes | Updated total bytes |
|---|---:|---:|---|---:|---:|---:|
| Ordinary 20-prop rendering | 0 | 1 | 23 | 608 | 1,040 | 1,648 |
| 20 fields, 32 concurrent renders | 0 | 4 | 24, 24, 24, 24 | 2,528 | 1,040 | 3,568 |
| 1000 fields, 32 concurrent renders | 0 | 4 | 1004, 1004, 1004, 1004 | 96,608 | 1,040 | 97,648 |
| 2000 fields, 32 concurrent renders | 0 | 0 | none | 0 | 1,040 | 1,040 |

All inspected returned lists were empty, with every backing-array entry cleared. The 2,000-field pages exceed the 1,024-entry
retention cap: their lists are discarded, leaving just 1,040 bytes of fixed infrastructure. At the configured four-list maximum,
the calculated cap including these object sizes is 99,568 bytes; the observed 1,000-field case retains 97,648 bytes.

Raw process snapshots, counter deltas, hashes, retained capacities, reference checks, runner and analysis are saved under
`artifacts/memory-review/process/`. The final retention records are `baseline-retention-final.json` and
`current-retention-final.json`. Each RSS/GC run is named `<version>-<render|handler>-<0|1|2>.json`.

Reproduce one process (launch a fresh process for each version/repeat):

```sh
dotnet tests/Inertia.Net.Benchmarks/bin/Release/net10.0/Inertia.Net.Benchmarks.dll \
  --process-memory render /absolute/path/to/render.json
dotnet tests/Inertia.Net.Benchmarks/bin/Release/net10.0/Inertia.Net.Benchmarks.dll \
  --process-memory handler /absolute/path/to/handler.json
dotnet tests/Inertia.Net.Benchmarks/bin/Release/net10.0/Inertia.Net.Benchmarks.dll \
  --process-memory retention /absolute/path/to/retention.json
```


## Render allocation reductions � 2026-10-23

This comparison is incremental: its baseline already includes the cache-buffer, dictionary-buffer and shared-fill changes below.
The implementation reuses the top-level prop-entry list through the platform object pool, clears references in `finally`, and
rejects lists with capacity above 1,024. Pool count is bounded by `DefaultObjectPool` (twice the processor count). Dot-key expansion
keeps its existing separate output list. Prop wrappers store their original sync or async delegate in one field; constants take
a null fast path. No loader adapter, API change or dependency was added.

| Workload | Before B/op | After B/op | Reduction |
|---|---:|---:|---:|
| JSON, 20 mixed props | 2,760 | 2,152 | 22.0% |
| Typed POCO JSON | 2,480 | 1,968 | 20.6% |
| Partial reload, 2 of 20 | 1,592 | 984 | 38.2% |
| HTML, 20 mixed props | 2,824 | 2,216 | 21.5% |
| Nested object, 2,000 fields | 5,496 | 568 | 89.7% |
| Construct the 20-prop fixture | 4,312 | 4,232 | 1.9% |

Render measurements reuse the fixture's props and context; constructing props is measured separately. They are adapter costs,
not total ASP.NET request allocations. Three Windows render-probe rounds agreed on every allocation value. The paired probe
also records allocations for cache hits, nested pages and concurrent misses. Pooling retains bounded empty backing arrays;
these earlier numbers describe allocation rate; the process-memory follow-up above supplies RSS and GC measurements. Cache payload ownership is unchanged.

Final validation: Release solution build passed with zero warnings/errors; the full suite passed 1,011 of 1,012 tests.
The sole failure is the previously reproduced `SsrNodeProcessTests.Several_instances_share_the_renders_and_cover_for_each_other`
process-failover assertion at line 435. The new failed-render/concurrent-render isolation regression passes. Linux Native AOT
publish and every smoke check passed without trimming/AOT warnings; 120 tested source/configuration files matched the working
copy (shell line endings normalized).

Final paired measurements: .NET 10.0.13 on Ubuntu 22.04/WSL2, pinned to logical CPU 7. Identical updated fixtures load the saved
baseline and final library in separate assembly contexts. Both variants are warmed, and their order alternates across 32 rounds;
no builds or tests overlap measurement. Intervals below are exploratory percentile bootstrap intervals from 5,000 resamples of
paired time ratios. Ratios within rounds differ from ratios of separate medians.

| Workload | Before B/op | After B/op | Before us | After us | Paired time change (95% interval) |
|---|---:|---:|---:|---:|---:|
| Json20 | 2760 | 2152 | 91.361 | 86.418 | +3.0% (+0.7% to +5.6%) |
| Partial2Of20 | 1592 | 984 | 29.935 | 29.852 | -0.2% (-1.9% to +2.4%) |
| Html20 | 2824 | 2216 | 89.782 | 92.502 | +3.1% (+0.5% to +4.8%) |
| ReferenceSerialize | 312 | 312 | 53.982 | 54.237 | +2.8% (-0.2% to +11.8%) |
| CacheWrite1KiB | 1240 | 1240 | 0.912 | 0.901 | -0.2% (-4.7% to +8.1%) |
| CacheWrite1MiB | 1048985 | 1049068 | 740.113 | 798.505 | +2.5% (-13.9% to +19.2%) |
| IntegerKeys1000 | 177160 | 177008 | 1264.419 | 1329.450 | -0.1% (-5.5% to +11.4%) |
| ConstructProps | 4312 | 4232 | 2.565 | 2.799 | -4.6% (-8.7% to -0.2%) |
| Nested20 | 744 | 568 | 11.811 | 11.672 | +2.7% (-3.6% to +5.8%) |
| Nested2000 | 5496 | 568 | 767.237 | 767.097 | +2.6% (-3.8% to +7.7%) |
| ParallelCacheHit1 | 736 | 584 | 7.784 | 7.904 | -1.3% (-5.8% to +8.4%) |
| ParallelCacheHit32 | 3000 | 2104 | 28.214 | 28.770 | +2.6% (+1.6% to +7.1%) |
| ColdBurst32 | 83832 | 83784 | 12980.275 | 12174.325 | +0.1% (-8.3% to +2.6%) |

In this incremental experiment the memory reductions are repeatable; its timing results do not establish a release-to-release slowdown. The published-release comparison above supersedes that interpretation. The final paired estimates indicate approximately 3% slower JSON,
HTML and 32-prop warm-cache rendering. The unchanged serialization control also shifts +2.8%, and host variation remains high,
but that does not establish that the render cost is zero. These changes are retained for allocation reduction, not advertised as
latency improvements. The initial Windows and Linux probes used an earlier delegate-check ordering; the final implementation
checks constants before generic delegate types, avoiding unnecessary checks for constant props.

Raw hashes, samples, logs, analysis and saved benchmark binaries are in `artifacts/memory-review/`. `paired-linux.json` and
`comparison-linux.md` describe the final code; `paired.json` and `paired-linux-first.json` retain the initial diagnostics.
For another final comparison using the saved variant binaries:

```sh
dotnet tests/Inertia.Net.Benchmarks/bin/Release/net10.0/Inertia.Net.Benchmarks.dll \
  --memory-comparison artifacts/memory-review/variants artifacts/memory-review/paired-repeat.json
```


## Latency measurements and planner experiments — 2026-10-23

Compared `f13f602e7e3ca4564e7787a61be8cde40db6ebbd` with the current cache-buffer, dictionary-buffer and shared-fill changes.
Ubuntu 22.04 under WSL2, Intel Core i5-8265U, SDK 10.0.402, runtime 10.0.13; measurements pinned to one logical CPU. All variants
were built before measuring, and no builds or tests overlapped measurement. `ReviewLatencyBenchmarks` supplies identical fixtures.

Separate BenchmarkDotNet runs had substantial host variation: even the unchanged reference serializer ranged from 17.4 to 140.3 µs.
Those runs are retained as diagnostics, but are not the basis of the comparisons below. `PairedLatencyProbe` loads each library in
its own assembly context, warms every fixture, then counterbalances variant order across 32 rounds in one process.

| Workload | Original median | Current median | Reduction in median time |
|---|---:|---:|---:|
| Write a 1 KiB cache entry | 2.395 µs | 1.121 µs | 53.2% |
| Write a 1 MiB cache entry | 2.434 ms | 1.385 ms | 43.1% |
| Render 1,000 integer-keyed props | 1.587 ms | 1.283 ms | 19.2% |
| 32 simultaneous cold-cache requests | 96.364 ms | 10.863 ms | 88.7% |
| Ordinary JSON, 20 mixed props | 65.871 µs | 65.878 µs | no clear change |
| Partial reload, 2 of 20 props | 13.902 µs | 13.618 µs | no clear change |
| HTML, 20 mixed props | 28.683 µs | 28.470 µs | no clear change |

The cold-cache fixture uses four backend slots and 10 ms of simulated asynchronous I/O, not a real database. The original executes
32 loaders; the current version executes one. Its first BenchmarkDotNet comparison also measured 96.019 → 11.997 ms.

For the first four rows, percentile-bootstrap 95% intervals of the paired median time ratios exclude zero improvement. Paired
reductions are 50.1% (44.3–53.1%), 48.2% (26.3–60.5%), 10.7% (6.6–16.8%) and 88.7% (87.3–88.8%), respectively. These estimates
use ratios within each round, so they differ from ratios of the independent medians in the table. The unchanged serialization
reference has a paired change of +2.2%, with an interval of −0.5% to +8.1%.

Both planner proposals were tested as isolated source variants:

| Experiment against current code | Paired latency change, 95% interval | Allocation impact | Decision |
|---|---:|---:|---|
| Skip planning work for non-parallel leaf values, 2,000 nested fields | −2.6%, −9.0% to +2.6% | unchanged | no reliable gain; not applied |
| Retain synchronous planner cache hits, one cached prop | +1.8%, −2.7% to +6.2% | 736 → 1,040 B/render | no reliable gain; not applied |
| Retain synchronous planner cache hits, 32 cached props | −15.2%, −26.5% to −11.3% | 3,000 → 5,888 B/render | faster, but 96% more allocation; not applied globally |

The leaf variant adds an early type/parallel check before constructing the path. The retained-hit variant stores hits in a
per-render dictionary behind `PropsWriter.Uncommon`, then reuses them while writing. Neither prototype was added to production code.

A second probe measures the entire planning walk on/off on the **same fixture and library instance**, alternating order for 64
rounds. The selected pages contain no parallel loaders. Disabling the walk reduced paired times by 2.9% for mixed JSON (95% interval
0.7–3.8%), 4.7% for 20 nested fields (3.4–6.2%) and 9.7% for 2,000 nested fields (8.6–11.1%). This establishes the potential overhead;
turning the flag off globally is not a valid implementation because other pages need parallel loading. The tested safe leaf
optimization did not recover a reliable gain. A different way to avoid traversal would need to retain early-start and nested-prop
semantics; it is not included in this change.

Raw logs, prototype preparation, samples and bootstrap analysis are saved in `artifacts/performance-review/`, including
`paired-report.md`, `paired-samples.json`, `planner-cost-summary.json` and the four BenchmarkDotNet result folders. To repeat the
paired measurements once the four variant outputs have been prepared with the same fixtures:

```sh
taskset -c 7 dotnet tests/Inertia.Net.Benchmarks/bin/Release/net10.0/Inertia.Net.Benchmarks.dll \
  --paired-latency /path/to/variant-root /path/to/paired-samples.json
taskset -c 7 dotnet tests/Inertia.Net.Benchmarks/bin/Release/net10.0/Inertia.Net.Benchmarks.dll \
  --planner-cost /path/to/variant-root /path/to/planner-cost.json
```

These are measured fixture latencies on this machine, not predictions of production response time. The native AOT smoke result
below still applies: this follow-up changes benchmark fixtures and reporting, not the library implementation.

## Cache writes, dictionary keys and concurrent misses — 2026-10-22 (Windows)

Compared the original core library at `f13f602e7e3ca4564e7787a61be8cde40db6ebbd` with this change, using the same updated benchmark
assembly for both. The original library was built separately and substituted into a copy of the benchmark output. Both used SDK
10.0.402 and .NET 10.0.13 on Windows. These are allocation probes, not controlled timing benchmarks: other build/test processes and
local activity overlapped some runs, and the reported times varied too much to claim latency improvements.

Three rounds of `--allocation-probe`:

| Workload | Original allocated bytes/op | Updated allocated bytes/op | Reduction |
|---|---:|---:|---:|
| Create a cache entry for a 1 KiB string | 4,880 | 1,240–1,241 | 75% |
| Create a cache entry for a 1 MiB string | 4,196,270–4,196,839 | 1,258,978–1,426,930 | 66–70% |
| Render 1,000 integer-keyed props | 216,992 | 177,160–177,184 | 18% |
| Render the equivalent string-keyed props | 96,856 | 96,856 | unchanged |

Large cache writes still allocate their owned stored array. The temporary serialization buffer is pooled; pool misses/trimming
and background allocations account for variation in the large-entry rows. Dictionary keys retain the serializer's converter path,
but reuse a pooled buffer and JSON writer instead of allocating serialized bytes for every key.

The existing `--render-probe` also ran against both versions. Every round retained the original allocations: JSON 2,760 B,
typed POCO 2,480 B, partial reload 1,592 B, HTML 2,824 B per request. No per-render field or allocation was added to `PropsWriter`.

Concurrent-miss regression tests verify that 32 requests for one prop key run one loader, including parallel loaders and expired
entries; 16 identical SSR requests perform one renderer call, including after expiry. The original review probes observed 32 and
16 calls respectively. Claims end after storage, failure or cancellation. Waiting requests retain their own cancellation and scoped
loaders; SSR waits consume the existing render deadline. Tests also cover rescued loaders, serialization failures and custom key
converters with source-generated and reflection metadata.

Native AOT validation also passed on Ubuntu 22.04 (WSL2), targeting `linux-x64` with SDK 10.0.402 and runtime 10.0.13:
`bash eng/aot-smoke.sh` published the native executable without AOT/trimming warnings, and every HTTP smoke check passed.
The tested Linux copy matched all 112 current working files used by the smoke project; only shell line endings were normalized.

The initial implementation left planner behavior unchanged. The follow-up latency section above now records measured planning
overhead, a leaf-pruning experiment and the speed/allocation tradeoff of retaining cache hits.

Reproduce from `tests/Inertia.Net.Benchmarks`:

```sh
dotnet run -c Release --no-build -- --allocation-probe
dotnet run -c Release --no-build -- --render-probe
dotnet run -c Release --no-build -- --cache-probe
dotnet run -c Release -- --filter '*CacheWriteBenchmarks*' '*DictionaryPropsBenchmarks*' --memory
```

## Cache invalidation, tags, stale-while-revalidate and the cache store — 2026-10-22

Measured on the same kind of 4-vCPU cloud Linux VM as the 1.4.0 section below (Intel Xeon @ 2.10 GHz, 15 GiB, Ubuntu 24.04.5),
SDK 10.0.402, .NET 10.0.13, BenchmarkDotNet 0.15.8. "1.4.0" is commit `4cde6f0` built from its own worktree; the render
benchmarks and probes are the same source on both sides (the cache probe gained the tagged and stale rows). Compare rows within this
section only.

Allocation is exact. Every path that does not use the new features allocates what 1.4.0 does, and a cache hit no more:

| Workload | 1.4.0 | This change |
|---|---:|---:|
| JSON, 20 mixed props (`--render-probe`) | 2,760 B | 2,760 B |
| Typed POCO page | 2,480 B | 2,480 B |
| Partial reload, 2 of 20 | 1,592 B | 1,592 B |
| HTML + Vite tags, 20 props | 2,824 B | 2,824 B |
| Deferred group load (BenchmarkDotNet) | 1,600 B | 1,600 B |
| Build 20 props, handler side (BenchmarkDotNet) | 4,312 B | 4,312 B |
| SSR gateway call (`--ssr-probe`) | 2,568 B | 2,568 B |
| A prop whose loader maps 500 rows, uncached (`--cache-probe`) | 25,480 B; 85–94 µs | 25,480 B; 84–93 µs |
| The same prop, cache hit | 1,152 B; 3.6–5.1 µs | 1,152 B; 3.7–5.3 µs |
| Cache hit, prop with two tags | — | 1,216 B; 3.8–5.2 µs |
| Cache hit, stale prop (its refresh already claimed) | — | 1,168 B; 3.8–5.0 µs |
| Server-rendered page (64 KiB of HTML), uncached | 3,432 B | 3,432 B |
| Server-rendered page, cache hit | 1,312 B; 9.4–18.4 µs | 1,256 B; 10.5–18.2 µs |
| Server-rendered page, stale hit | — | 1,256 B; 8.9–18.7 µs |

What decides those numbers:

- **No new per-render state.** The props writer, allocated by every render, keeps its size: the stale-refresh list shares one field
  with the rescued-props list, and the page writer, which has the `HttpContext`, registers the refreshes after the walk.
- **No key strings on a lookup.** `InertiaCacheKey` keeps the kind and the key apart, so the memory store looks a prop up exactly as 1.4
  did; only a store keyed by strings builds `inertia:prop:{key}`. A render's key is the hex SHA-256 alone (one string, no hash array),
  which is the 56 bytes an SSR hit saves.
- **Settings sized by use.** A prop cached without `staleFor` or tags carries the same 32-byte settings object as in 1.4; one with tags
  pays for an exact-size tag array (+64 B for two tags), one with `staleFor` 16 bytes more. A stale hit whose refresh another request
  claimed allocates nothing more; the request that claims it allocates the refresh's state and, for SSR, a copy of the page JSON.

Times, BenchmarkDotNet `--filter '*RenderBenchmarks*' --memory`, alternating with 1.4.0. Rounds 1–4 measured a first build that
awaited cached props at a second await site in the walk: the deferred group load was 6–7% slower in three of those rounds. The final
build shares the walk's existing await (as the walk's comment asks), and rounds 5–6 measured it. Medians in µs:

| Workload | Round 5: 1.4.0 | This change | Round 6: 1.4.0 | This change |
|---|---:|---:|---:|---:|
| JSON, 20 mixed props | 14.52 | 14.41 (−0.7%) | 14.01 | 14.64 (+4.5%) |
| Typed POCO page | 9.76 | 9.50 (−2.6%) | 10.14 | 9.53 (−6.0%) |
| Partial reload, 2 of 20 | 6.82 | 6.63 (−2.8%) | 6.78 | 6.69 (−1.3%) |
| Deferred group load | 5.38 | 5.40 (+0.3%) | 5.51 | 5.49 (−0.4%) |
| HTML + Vite tags, 20 props | 15.54 | 16.18 (+4.2%) | 15.57 | 15.70 (+0.9%) |
| Reference: STJ, no Inertia.Net code | 8.17 | 7.92 (−3.1%) | 8.01 | 7.38 (−7.9%) |

The reference benchmark, which runs no Inertia.Net code, moved by up to 7.9% between processes, and every render path stayed within
that band in both directions: render times are unchanged. Allocation, above, is the stable signal.

Reproduce (from each worktree's `tests/Inertia.Net.Benchmarks`):

```sh
dotnet run -c Release --no-build -- --render-probe
dotnet run -c Release --no-build -- --cache-probe
dotnet run -c Release --no-build -- --ssr-probe
dotnet run -c Release --no-build -- --filter '*RenderBenchmarks*' --memory
```

## 1.4.0 on a 4-vCPU cloud Linux VM — 2026-10-20

Measured in a cloud Linux container: a Firecracker microVM on a shared KVM host, Intel Xeon @ 2.10 GHz, 4 vCPUs (1 thread per
core), 15 GiB RAM, no swap, Ubuntu 24.04.5, SDK 10.0.100, .NET 10.0.0 (RyuJIT x86-64-v4), BenchmarkDotNet 0.15.8. The host is
shared with other tenants; nothing else ran in the VM during the measurements (load average 1.0, the benchmark itself). Compare
rows within this section only.

"1.3.0" is commit `3adc319` and "1.4.0" is `e8be5cb` (the tip of `release/1.4.0`), each built in Release from its own worktree;
the benchmark source is the same on both sides. Five rounds alternated 1.3.0 and 1.4.0, each run
`--filter '*RenderBenchmarks*' --launchCount 3 --memory` (three processes, about 295 measured iterations per benchmark per run).
Round 3's first 1.3.0 run was stopped partway by a job time limit; it was discarded and rerun. Medians are taken over every measured iteration of a run.
Times are in µs: mean ± StdDev, then the median.

**JSON, 20 mixed props**

| Round | 1.3.0 | 1.3.0 median | 1.4.0 | 1.4.0 median | Δ median |
|---|---:|---:|---:|---:|---:|
| 1 | 10.74 ± 1.07 | 10.74 | 10.59 ± 0.92 | 10.56 | −1.7% |
| 2 | 10.43 ± 0.80 | 10.49 | 10.72 ± 0.84 | 10.77 | +2.7% |
| 3 | 10.24 ± 0.89 | 10.29 | 10.22 ± 0.93 | 10.27 | −0.2% |
| 4 | 10.29 ± 0.86 | 10.33 | 10.18 ± 0.75 | 10.30 | −0.3% |
| 5 | 9.70 ± 0.91 | 9.82 | 9.97 ± 0.82 | 10.12 | +3.0% |

**JSON, typed POCO props**

| Round | 1.3.0 | 1.3.0 median | 1.4.0 | 1.4.0 median | Δ median |
|---|---:|---:|---:|---:|---:|
| 1 | 7.09 ± 0.65 | 7.12 | 6.98 ± 0.57 | 6.96 | −2.2% |
| 2 | 6.94 ± 0.49 | 6.97 | 7.00 ± 0.54 | 7.05 | +1.1% |
| 3 | 6.92 ± 0.51 | 6.94 | 6.71 ± 0.62 | 6.77 | −2.4% |
| 4 | 6.73 ± 0.45 | 6.76 | 6.74 ± 0.62 | 6.81 | +0.8% |
| 5 | 6.60 ± 0.54 | 6.73 | 6.66 ± 0.57 | 6.73 | +0.1% |

**Partial reload, 2 of 20**

| Round | 1.3.0 | 1.3.0 median | 1.4.0 | 1.4.0 median | Δ median |
|---|---:|---:|---:|---:|---:|
| 1 | 5.14 ± 0.57 | 5.11 | 4.89 ± 0.46 | 4.87 | −4.7% |
| 2 | 4.92 ± 0.37 | 4.92 | 4.89 ± 0.33 | 4.91 | −0.3% |
| 3 | 4.92 ± 0.40 | 4.95 | 4.74 ± 0.44 | 4.81 | −2.9% |
| 4 | 4.63 ± 0.39 | 4.67 | 4.83 ± 0.38 | 4.83 | +3.4% |
| 5 | 4.59 ± 0.40 | 4.64 | 4.79 ± 0.34 | 4.85 | +4.6% |

**Deferred group load**

| Round | 1.3.0 | 1.3.0 median | 1.4.0 | 1.4.0 median | Δ median |
|---|---:|---:|---:|---:|---:|
| 1 | 4.15 ± 0.35 | 4.11 | 4.06 ± 0.35 | 4.05 | −1.4% |
| 2 | 4.08 ± 0.31 | 4.09 | 3.96 ± 0.30 | 3.99 | −2.4% |
| 3 | 3.97 ± 0.31 | 3.99 | 3.96 ± 0.31 | 3.96 | −0.6% |
| 4 | 3.81 ± 0.37 | 3.84 | 3.87 ± 0.33 | 3.91 | +1.9% |
| 5 | 3.78 ± 0.35 | 3.86 | 3.92 ± 0.35 | 3.96 | +2.7% |

**HTML + Vite tags, 20 props**

| Round | 1.3.0 | 1.3.0 median | 1.4.0 | 1.4.0 median | Δ median |
|---|---:|---:|---:|---:|---:|
| 1 | 11.62 ± 1.11 | 11.58 | 11.57 ± 0.92 | 11.57 | −0.1% |
| 2 | 11.47 ± 0.86 | 11.50 | 11.52 ± 0.90 | 11.57 | +0.6% |
| 3 | 11.17 ± 0.89 | 11.22 | 11.27 ± 0.91 | 11.30 | +0.7% |
| 4 | 10.62 ± 1.05 | 10.82 | 11.14 ± 0.85 | 11.20 | +3.5% |
| 5 | 10.71 ± 0.89 | 10.91 | 11.29 ± 0.80 | 11.33 | +3.8% |

**Reference: STJ, same page as plain data** (no Inertia.Net code: the noise floor)

| Round | 1.3.0 | 1.3.0 median | 1.4.0 | 1.4.0 median | Δ median |
|---|---:|---:|---:|---:|---:|
| 1 | 6.22 ± 0.74 | 6.09 | 6.11 ± 0.53 | 6.17 | +1.3% |
| 2 | 5.99 ± 0.43 | 6.00 | 5.99 ± 0.39 | 6.03 | +0.5% |
| 3 | 5.86 ± 0.43 | 5.88 | 5.78 ± 0.52 | 5.82 | −1.0% |
| 4 | 5.78 ± 0.50 | 5.80 | 5.54 ± 0.52 | 5.65 | −2.6% |
| 5 | 5.57 ± 0.48 | 5.63 | 5.82 ± 0.48 | 5.85 | +3.9% |

**Build 20 props (handler side)**

| Round | 1.3.0 | 1.3.0 median | 1.4.0 | 1.4.0 median | Δ median |
|---|---:|---:|---:|---:|---:|
| 1 | 1.100 ± 0.060 | 1.099 | 1.083 ± 0.069 | 1.088 | −1.0% |
| 2 | 1.085 ± 0.071 | 1.088 | 1.064 ± 0.065 | 1.068 | −1.8% |
| 3 | 1.065 ± 0.071 | 1.075 | 1.064 ± 0.069 | 1.066 | −0.9% |
| 4 | 1.079 ± 0.067 | 1.085 | 1.035 ± 0.063 | 1.046 | −3.6% |
| 5 | 1.043 ± 0.068 | 1.046 | 1.067 ± 0.054 | 1.065 | +1.8% |

Summary: the median of the five per-round medians. Allocation was the same in every run of a version.

| Benchmark | 1.3.0 median | 1.4.0 median | Difference | Allocated, 1.3.0 → 1.4.0 |
|---|---:|---:|---:|---:|
| JSON, 20 mixed props | 10.33 µs | 10.30 µs | −0.03 µs (−0.3%) | 2,752 B → 2,760 B |
| JSON, typed POCO props | 6.94 µs | 6.81 µs | −0.12 µs (−1.7%) | 2,472 B → 2,480 B |
| Partial reload, 2 of 20 | 4.92 µs | 4.85 µs | −0.07 µs (−1.5%) | 1,584 B → 1,592 B |
| Deferred group load | 3.99 µs | 3.96 µs | −0.03 µs (−0.6%) | 1,592 B → 1,600 B |
| HTML + Vite tags, 20 props | 11.22 µs | 11.33 µs | +0.11 µs (+0.9%) | 2,800 B → 2,824 B |
| Reference: STJ, plain data | 5.88 µs | 5.85 µs | −0.03 µs (−0.5%) | 312 B → 312 B |
| Build 20 props (handler side) | 1.085 µs | 1.066 µs | −0.02 µs (−1.8%) | 4,232 B → 4,392 B |

How far to trust it:

- **No round was tight.** On this shared VM each run's StdDev is 5–12% of its mean, so no round meets a 5% bar on both sides. Most of
  the spread is between iterations within a process: the three launch means of a run agree within 0.5–11% (usually under 5%), the
  99.9% error of each mean is 1.1–2.4%, and none of the 10–90% swings between processes seen on the laptop showed up here.
- **The noise floor is ±4% per round.** The STJ reference runs no Inertia.Net code and still moved −2.6% to +3.9% between the two
  sides of a round. Every Inertia.Net benchmark's per-round difference falls inside −4.7% to +4.6%, and every summary difference is
  under 2%, so no benchmark crossed the 5% threshold for a confirmation rerun. Both versions got faster as the session went on:
  1.3.0 by 5–9% from round 1 to round 5 and 1.4.0 by 0–5%, the STJ reference included (−7.5% and −5.1%), which points to the host
  rather than the code for the late rounds where 1.4.0 looks slower. Alternating rounds is what keeps that drift out of the medians.
- **HTML** is the only path whose 1.4.0 median is above 1.3.0's: +0.11 µs, from two rounds (4 and 5) where the 1.3.0 run was
  unusually fast; the 1.4.0 median stayed at 11.2–11.6 µs throughout. While CSP nonces, the SSR cache and DevTools are off, this
  path adds a null delegate check (`CspNonce.Resolve`), one more 1-byte write in `InertiaRootViewContext.WriteBody` and 16 bytes on
  `InertiaRootViewContext`, which is in line with a cost below what these runs can resolve.

`--render-probe` (one process per version; the third repetition, after 2,000 warm-up renders and the tiered-JIT transition):

| Workload (probe) | 1.3.0 | 1.4.0 |
|---|---:|---:|
| JSON, 20 mixed props | 2,752 B; 7.7 µs | 2,760 B; 10.5 µs |
| Typed POCO page | 2,472 B; 5.3 µs | 2,480 B; 6.8 µs |
| Partial reload, 2 of 20 | 1,584 B; 3.7 µs | 1,592 B; 4.8 µs |
| HTML + Vite tags, 20 props | 2,800 B; 9.4 µs | 2,824 B; 10.4 µs |

That single 1.3.0 process was 13–22% faster than the fastest 1.3.0 BenchmarkDotNet median. Two more alternating pairs of probe processes
(not part of the plan, added to check this) gave third repetitions of 10.2/11.4 µs (1.3.0) and 10.3/9.7 µs (1.4.0) for the
20-prop page, and 4.4/5.2 µs against 3.8/4.6 µs for the partial reload: one probe process cannot separate the versions. Its
allocation is exact and matches the benchmarks.

| Workload (`--cache-probe`, 1.4.0) | Without the cache | With the cache |
|---|---:|---:|
| A prop whose loader maps 500 rows | 25,488 B; 66–88 µs | 1,128 B; 2.9–3.7 µs |
| A server-rendered page (64 KiB of HTML, in-memory SSR server) | 3,432–3,439 B; 80–136 µs | 1,312 B; 7.2–13.4 µs |

The cache probe allocates 8–16 bytes less here than in the Windows laptop section above; the ratios are the same.

Conclusion: on this machine 1.4.0 renders every page in the same time as 1.3.0. The render paths' summary differences are −1.7% to +0.9%
(at most 0.12 µs), below the ±4% that a benchmark with no Inertia.Net code moves between rounds, and none reaches the 5% bar for a
rerun. The typed page is not slower (−1.7%), so the laptop's 1.5–3% does not hold. What is certain is allocation: +8 B per JSON
render, +24 B per HTML render, and +16 B for each `InertiaProp` a handler creates (+160 B for the 20-prop page's 10 props), from
`CacheKey` and `CacheDuration` in `InertiaProp`. Those two fields have since become one reference set only by `Cache()`: the 20-prop
handler side now allocates 4,312 B (+80 B over 1.3.0, +8 B per prop).

Reproduce (from each worktree's `tests/Inertia.Net.Benchmarks`):

```sh
dotnet run -c Release --no-build -- --filter '*RenderBenchmarks*' --launchCount 3 --memory
dotnet run -c Release --no-build -- --render-probe
dotnet run -c Release --no-build -- --cache-probe
```

## 1.4 caches and per-response settings — 2026-10-20

Measured on the Windows laptop (i5-8265U, .NET 10.0.13, Release). "Before" is commit `3adc319`
(1.3.0) built from a separate worktree with the same probe source.

| Workload (probe) | 1.3.0 | 1.4.0 |
|---|---:|---:|
| JSON, 20 mixed props (`--render-probe`) | 2,752 B | 2,760 B |
| Typed POCO page (`--render-probe`) | 2,472 B | 2,480 B |
| Partial reload, 2 of 20 (`--render-probe`) | 1,584 B | 1,592 B |
| HTML + Vite tags, 20 props (`--render-probe`) | 2,800 B | 2,824 B |

Timings, from BenchmarkDotNet (`--filter '*JsonTypedPoco*' '*Json20Props*'`), alternating 1.3.0 and 1.4.0 from clean worktrees.
The final 1.4.0 skips two lookups per render that the first 1.4.0 build made (the pages check from DI, the DevTools capture from the
request features) unless that feature is on:

| Round | 20 props, 1.3.0 | 20 props, 1.4.0 | Typed, 1.3.0 | Typed, 1.4.0 |
|---|---:|---:|---:|---:|
| First build, 1 | 15.06 ± 0.30 µs | 14.73 ± 0.22 µs | 10.37 ± 0.21 µs | 12.38 ± 0.87 µs |
| First build, 2 | 28.08 ± 3.03 µs | 16.65 ± 0.81 µs | 10.49 ± 0.39 µs | 10.64 ± 0.21 µs |
| First build, 3 | 15.06 ± 0.22 µs | 14.52 ± 0.25 µs | 10.27 ± 0.19 µs | 10.42 ± 0.21 µs |
| Final, 1 | 16.85 ± 0.24 µs | 14.44 ± 0.29 µs | 9.88 ± 0.17 µs | 11.26 ± 0.41 µs |
| Final, 2 | 16.36 ± 0.32 µs | 18.00 ± 0.34 µs | 13.58 ± 0.42 µs | 11.95 ± 0.20 µs |
| Final, 3 | 17.68 ± 0.28 µs | 15.11 ± 0.28 µs | 9.81 ± 0.19 µs | 9.96 ± 0.08 µs |

The same build moves by 10–90% between processes on this laptop, so single rounds disagree in both directions. Where both sides of a
round ran with a tight error, the typed page is 0.15–0.3 µs (1.5–3%) slower and the 20-prop page is level or faster; that is about
the size of the work added to every render, and below what this machine can resolve. Allocation is exact and is the stable signal.

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
