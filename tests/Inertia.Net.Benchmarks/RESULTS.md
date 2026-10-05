# Inertia.Net benchmark results

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
