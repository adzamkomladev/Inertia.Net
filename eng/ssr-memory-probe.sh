#!/usr/bin/env bash
# RSS of an Inertia SSR server process (the managed Node or Bun process): at rest after one render of each page, peak while rendering,
# and 3 s after. Linux only (reads /proc), needs curl.
# Usage: eng/ssr-memory-probe.sh <runtime: node|bun> <bundle: ssr/ssr.js> <port the bundle listens on> <renders> <page.json>...
# RUNTIME_ARGS adds runtime flags before the bundle, as SsrNodeProcessOptions.Arguments does: RUNTIME_ARGS=--smol (Bun), RUNTIME_ARGS=--max-old-space-size=128 (Node).
# Page JSON files are Inertia responses, e.g. curl -H 'X-Inertia: true' -H 'X-Inertia-Version: <version>' <url> > page.json.
set -euo pipefail

RUNTIME=${1:?runtime}
BUNDLE=${2:?bundle}
PORT=${3:?port}
RENDERS=${4:?renders}
shift 4
PAGES=("$@")
[ ${#PAGES[@]} -gt 0 ] || { echo "No page JSON files given." >&2; exit 1; }
URL="http://127.0.0.1:$PORT"

rss() { awk '/^VmRSS:/ { printf "%.0f", $2 / 1024 }' "/proc/$1/status"; }
render() { curl -fs -o /dev/null -X POST -H 'Content-Type: application/json' --data-binary "@$1" "$URL/render"; }

# shellcheck disable=SC2086 # RUNTIME_ARGS is a list of flags
NODE_ENV=production "$RUNTIME" ${RUNTIME_ARGS:-} "$BUNDLE" >/dev/null 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null; wait $PID 2>/dev/null || true' EXIT
for _ in $(seq 1 100); do curl -fs -o /dev/null "$URL/health" && break; sleep 0.1; done
curl -fs -o /dev/null "$URL/health" || { echo "The SSR server did not start on port $PORT." >&2; exit 1; }
STARTED=$(rss $PID)

for page in "${PAGES[@]}"; do render "$page"; done
sleep 2
REST=$(rss $PID)

PEAK_FILE=$(mktemp)
echo "$REST" >"$PEAK_FILE"
( while kill -0 $PID 2>/dev/null; do r=$(rss $PID); [ "$r" -gt "$(cat "$PEAK_FILE")" ] && echo "$r" >"$PEAK_FILE"; sleep 0.1; done ) &
SAMPLER=$!
START=$(date +%s.%N)
for ((i = 0; i < RENDERS; i++)); do echo "${PAGES[i % ${#PAGES[@]}]}"; done | xargs -P 8 -I{} curl -fs -o /dev/null -X POST -H 'Content-Type: application/json' --data-binary @{} "$URL/render"
SECONDS_TAKEN=$(echo "$(date +%s.%N) - $START" | bc)
sleep 3
AFTER=$(rss $PID)
kill $SAMPLER 2>/dev/null || true
PEAK=$(cat "$PEAK_FILE")
rm -f "$PEAK_FILE"

echo "$RUNTIME${RUNTIME_ARGS:+ $RUNTIME_ARGS} $(basename "$(dirname "$(dirname "$(realpath "$BUNDLE")")")"): started ${STARTED} MB, at rest ${REST} MB, peak ${PEAK} MB, after ${AFTER} MB; $RENDERS renders of ${#PAGES[@]} pages in ${SECONDS_TAKEN%.*} s"
