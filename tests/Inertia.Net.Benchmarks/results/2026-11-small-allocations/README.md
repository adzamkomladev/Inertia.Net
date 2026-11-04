# Small allocation candidates

`paired-1.json` and `paired-2.json` compare phase 2 (`6ab0a71`) with only the retained 3.4 changes. Each independent process loads
the same fixture DLL into two assembly contexts, with a different core adapter DLL in each. Thirty-two rounds alternate order
after warmup and iteration calibration. `environment.json` records hashes, CPU affinity, runtime and measurement limits.
`summarize.py` calculates medians of within-round latency ratios and deterministic 5,000-resample bootstrap intervals.

The fixtures exercise eight scroll wrappers with existing merge paths, eight converter-bearing properties, a waiter attempting
to claim an already-held fill, and 32 cached props marked parallel. Controls include an ordinary mixed JSON render and a loader
that suspends with `Task.Yield`. Rendering includes the result and response write to `Stream.Null`; props and contexts are reused.
The held-fill row measures the contention check itself, without a loader, network request or wait for completion.

The retained changes pass scroll wrappers as suffixes, carry member serializer metadata in pooled entries, and check for an
existing fill before allocating its completion source. The synchronous member-serializer path and indexed entry loop keep the
larger pooled entry out of suspended enumeration state. The ordinary JSON control's allocation remains unchanged; the yielding
loader allocates 40 bytes less. Pooled entries grow from 24 to 32 bytes, so these warm results do not establish a cold-memory or
concurrent-pool footprint improvement. Converter options remain keyed by converter instance.

`rejected-planned-hit-memo.json` compares an early retained candidate against one that keeps synchronous planned cache hits in
a dictionary on the pooled writer. That saves 768 bytes for 32 hits: the default memory store otherwise boxes its prop key on
each lookup. The candidate changes observable behavior: a preceding sequential loader can invalidate a cached value before the
walk reaches it, and the snapshot ignores that invalidation. `rejected-memo-regression.txt` records both failing regressions;
the shared test suite retains them. No planned-hit memoization is included in the final change.

`exploratory/` retains the initial paired runs before the synchronous serializer helper and yielding-loader control were added.
Use the top-level paired files and summary for the final implementation. Their baseline and final fixture hashes match.

Reproduce with complete benchmark output directories named `baseline` and `current`. Build identical fixture sources in both,
using phase 2 core sources in the first and phase 2 plus only the retained 3.4 patch in the second. The adapter hashes in the
paired output must differ, while the benchmark DLL hashes must match. Run from a quiet shell, choosing an available CPU:

```sh
DOTNET_PROCESSOR_COUNT=1 taskset -c 7 dotnet <current-output>/Inertia.Net.Benchmarks.dll --small-allocation-probe <variants-directory> paired-1.json
DOTNET_PROCESSOR_COUNT=1 taskset -c 7 dotnet <current-output>/Inertia.Net.Benchmarks.dll --small-allocation-probe <variants-directory> paired-2.json
python3 tests/Inertia.Net.Benchmarks/results/2026-11-small-allocations/summarize.py
```
