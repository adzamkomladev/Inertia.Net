# November review-fixes measurements

`before-*` are standalone probes from commit `2dd4a4b` using the SDK and core DLL hashes recorded in `environment.json`.
They preserve an allocation baseline; concurrent development builds/tests were not excluded, so their latency is exploratory.
No speed claim should be derived from these standalone timings.

The render probe measures 100,000 warmed requests for each of four ordinary render workloads, repeated three times. The cache
probe measures 20,000 prop requests and 5,000 SSR requests per workload, repeated three times. SSR responses contain about 64 KiB
of HTML. These in-process probes do not measure HTTP connection reuse. Their stale cache fixtures retain a claimed refresh
without executing response-completion callbacks: they measure serving stale data while another request owns refresh, not worker
execution or shutdown. The gated HTTP/1.1 integration regression covers the connection-delay behavior separately.

Commands (from a clean checkout at the baseline commit; choose an available CPU):

```sh
export PATH=/root/.dotnet:$PATH
dotnet build tests/Inertia.Net.Benchmarks/Inertia.Net.Benchmarks.csproj -c Release -warnaserror
DOTNET_PROCESSOR_COUNT=1 taskset -c 7 dotnet tests/Inertia.Net.Benchmarks/bin/Release/net10.0/Inertia.Net.Benchmarks.dll --render-probe
DOTNET_PROCESSOR_COUNT=1 taskset -c 7 dotnet tests/Inertia.Net.Benchmarks/bin/Release/net10.0/Inertia.Net.Benchmarks.dll --cache-probe
```

For final paired timing, keep the fixture source identical between the baseline and fixed assemblies, stop builds/tests/servers,
and run the existing `--memory-comparison` harness twice. Each process warms every version and uses 32 alternating-order paired
rounds. Add `GuidKeys500` to that harness before measuring dictionary optimization; allocation claims for scroll and converter
changes require their own exercised workloads. Reuse the existing deterministic summary script with the actual method names,
retaining controls and raw samples rather than selecting only favorable rounds.
