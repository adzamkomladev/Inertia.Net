#!/usr/bin/env bash
# Hosting cost of Inertia.Net next to a plain Minimal API serving the same data: RSS, requests per second, latency, CPU and startup,
# for JIT and Native AOT builds with server and workstation GC. Linux only; needs taskset (util-linux), clang and zlib for AOT.
# Usage: eng/hosting-probe.sh [output.md] [--seconds 10] [--warmup 5] [--connections 64] [--repeat 3] [--server-cpus FIRST-LAST] [--only TEXT] [--rates 1000,4000]
#        [--builds jit|aot|all] [--suite hosting|ssr]
# The server runs on --server-cpus and the load client on the remaining CPUs. --rates adds latency at those fixed request rates (server GC builds).
# --builds jit skips the Native AOT publish. --suite ssr compares client-rendered and server-rendered first loads of the React pages in
# tests/Inertia.Net.HostingProbe/ClientApp (built here with npm; needs Node, and Bun for the Bun row).
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT=${1:-$ROOT/artifacts/hosting-probe.md}
shift || true
PROJECT=$ROOT/tests/Inertia.Net.HostingProbe/Inertia.Net.HostingProbe.csproj
WORK=$ROOT/artifacts/hosting-probe
mkdir -p "$WORK" "$(dirname "$OUT")"

# Server on the first half of the CPUs (or --server-cpus FIRST-LAST), load client on the rest.
CPUS=$(nproc)
SERVER=0-$((CPUS / 2 - 1))
SUITE=hosting
BUILDS=all
for ((i = 1; i <= $#; i++)); do
  j=$((i + 1))
  case "${!i}" in
    --server-cpus) SERVER=${!j} ;;
    --suite) SUITE=${!j} ;;
    --builds) BUILDS=${!j} ;;
  esac
done

# Before the publish, which copies the client build and the SSR bundle into the output.
[ "$SUITE" = ssr ] && (cd "$ROOT/tests/Inertia.Net.HostingProbe/ClientApp" && npm ci --no-audit --no-fund && npm run build)
# The JIT build is always published: the load client runs from it.
dotnet publish "$PROJECT" -c Release -o "$WORK/jit" -warnaserror -nologo -v q
[ "$BUILDS" = jit ] || dotnet publish "$PROJECT" -c Release -o "$WORK/aot" -warnaserror -nologo -v q -p:HostingProbeAot=true
CLIENT=$(for ((c = 0; c < CPUS; c++)); do ((c < ${SERVER%-*} || c > ${SERVER#*-})) && printf '%s,' "$c"; done; true)
CLIENT=${CLIENT%,}

taskset -c "$CLIENT" dotnet "$WORK/jit/Inertia.Net.HostingProbe.dll" bench \
  "$WORK/jit/Inertia.Net.HostingProbe.dll" "$WORK/aot/Inertia.Net.HostingProbe" "$OUT" --server-cpus "$SERVER" "$@"
