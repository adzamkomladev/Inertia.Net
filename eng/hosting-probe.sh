#!/usr/bin/env bash
# Hosting cost of Inertia.Net next to a plain Minimal API serving the same data: RSS, requests per second, latency, CPU and startup,
# for JIT and Native AOT builds with server and workstation GC. Linux only; needs taskset (util-linux), clang and zlib for AOT.
# Usage: eng/hosting-probe.sh [output.md] [--seconds 10] [--warmup 5] [--connections 64] [--repeat 3] [--server-cpus FIRST-LAST] [--only TEXT] [--rates 1000,4000]
# The server runs on --server-cpus and the load client on the remaining CPUs. --rates adds latency at those fixed request rates (server GC builds).
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT=${1:-$ROOT/artifacts/hosting-probe.md}
shift || true
PROJECT=$ROOT/tests/Inertia.Net.HostingProbe/Inertia.Net.HostingProbe.csproj
WORK=$ROOT/artifacts/hosting-probe
mkdir -p "$WORK" "$(dirname "$OUT")"

dotnet publish "$PROJECT" -c Release -o "$WORK/jit" -warnaserror -nologo -v q
dotnet publish "$PROJECT" -c Release -o "$WORK/aot" -warnaserror -nologo -v q -p:HostingProbeAot=true

# Server on the first half of the CPUs (or --server-cpus FIRST-LAST), load client on the rest.
CPUS=$(nproc)
SERVER=0-$((CPUS / 2 - 1))
for ((i = 1; i <= $#; i++)); do [ "${!i}" = --server-cpus ] && { j=$((i + 1)); SERVER=${!j}; }; done
CLIENT=$(for ((c = 0; c < CPUS; c++)); do ((c < ${SERVER%-*} || c > ${SERVER#*-})) && printf '%s,' "$c"; done; true)
CLIENT=${CLIENT%,}

taskset -c "$CLIENT" dotnet "$WORK/jit/Inertia.Net.HostingProbe.dll" bench \
  "$WORK/jit/Inertia.Net.HostingProbe.dll" "$WORK/aot/Inertia.Net.HostingProbe" "$OUT" --server-cpus "$SERVER" "$@"
