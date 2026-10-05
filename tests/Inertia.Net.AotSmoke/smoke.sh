#!/usr/bin/env bash
# Starts the published Inertia.Net.AotSmoke binary and checks the Inertia protocol over HTTP (curl + grep only).
# Usage: smoke.sh <path-to-binary>    Env: PORT (default 5199). Exits non-zero when any check fails.
set -uo pipefail

BIN=${1:?usage: smoke.sh <path-to-binary>}
PORT=${PORT:-5199}
BASE="http://127.0.0.1:$PORT"
T=$(mktemp -d)
JAR="$T/jar"
FAILURES=0

ASPNETCORE_URLS="$BASE" "$BIN" >"$T/app.log" 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null; wait $PID 2>/dev/null; rm -rf "$T"' EXIT

for _ in $(seq 1 100); do
  curl -fs "$BASE/healthz" >/dev/null 2>&1 && break
  if ! kill -0 $PID 2>/dev/null; then echo "app exited during startup:"; cat "$T/app.log"; exit 1; fi
  sleep 0.1
done
curl -fs "$BASE/healthz" >/dev/null || { echo "app not ready:"; cat "$T/app.log"; exit 1; }

# req METHOD PATH [curl args...]: response headers in $T/h, body in $T/b, status in $STATUS.
req() {
  local method=$1 path=$2
  shift 2
  STATUS=$(curl -s -X "$method" -o "$T/b" -D "$T/h" -w '%{http_code}' -b "$JAR" -c "$JAR" "$@" "$BASE$path")
  CURRENT="$method $path"
}

inertia() { req "$@" -H 'X-Inertia: true' -H "X-Inertia-Version: $VERSION"; }

fail() { echo "FAIL [$CURRENT] $1"; FAILURES=$((FAILURES + 1)); }
status_is() { [ "$STATUS" = "$1" ] || fail "status $STATUS, expected $1"; }
header_is() { tr -d '\r' <"$T/h" | grep -qix "$1: $2" || fail "header '$1: $2' missing"; }
has() { grep -qF -- "$1" "$T/b" || fail "body lacks: $1"; }
lacks() { ! grep -qF -- "$1" "$T/b" || fail "body should not contain: $1"; }

# 1. First visit: HTML with the page script, Vite tags and view data.
req GET /
status_is 200
header_is Vary X-Inertia
has '<script data-page="app" type="application/json">'
has '<div id="app"></div>'
has '<link rel="stylesheet" href="/build/assets/app-1c9b6e2d.css">'
has '<link rel="stylesheet" href="/build/assets/vendor-55aa01ff.css">'
has '<script type="module" src="/build/assets/app-4f2a91c3.js"></script>'
has '<link rel="modulepreload" href="/build/assets/vendor-8d1e0b7a.js">'
has '<title>Smoke &amp; Mirrors</title>'
has '"deferredProps":{"default":["stats"]}'
VERSION=$(sed -n 's/.*"version":"\([0-9a-f]*\)".*/\1/p' "$T/b")
[ ${#VERSION} -eq 32 ] || fail "expected a 32-char manifest hash version, got '$VERSION'"

# 2. Inertia visit with typed POCO props: JSON, headers, $bigint, defer/once/merge metadata.
inertia GET /
status_is 200
header_is X-Inertia true
header_is Vary X-Inertia
header_is Content-Type 'application/json; charset=utf-8'
has '"component":"Home"'
has '"big":{"$bigint":"9007199254740993"}'
has '"plans":["free","pro"]'
has '"feed":[{"name":"item","value":1}]'
has '"mergeProps":["feed"]'
has '"onceProps":{"plans":{"prop":"plans","expiresAt":null}}'
has '"preserveBigIntegers":true'
lacks '"stats":'

# 3. InertiaProps page: deferred groups, scroll, once with TTL, always, optional, nested dictionaries, shared props.
inertia GET /dashboard
status_is 200
has '"component":"Dashboard"'
has '"url":"/dashboard"'
has '"user":{"name":"Ada","roles":["admin"]}'
has '"feed":{"data":[{"name":"post-1","value":1}],"page":1,"hasMore":true}'
has '"appName":"AotSmoke"'
has '"auth":{"user":null}'
has '"requestShared":true'
has '"sharedProps":["errors","appName","auth","requestShared"]'
has '"deferredProps":{"default":["stats"],"charts":["chart"]}'
has '"mergeProps":["feed.data"]'
has '"scrollProps":{"feed":{"pageName":"page","previousPage":null,"nextPage":2,"currentPage":1,"reset":false}}'
has '"onceProps":{"countries":{"prop":"countries","expiresAt":'
has '"countries":["BE","FR"]'
lacks '"lazy":'
lacks '"flash":'

# 4. Partial reload: only the requested props, plus always props.
inertia GET /dashboard -H 'X-Inertia-Partial-Component: Dashboard' -H 'X-Inertia-Partial-Data: stats,lazy'
status_is 200
has '"stats":{"name":"visits","value":1234}'
has '"lazy":"loaded"'
has '"filters":"all"'
has '"errors":{}'
lacks '"title":'
lacks '"count":'
lacks '"deferredProps"'

# 5. Deferred group load.
inertia GET /dashboard -H 'X-Inertia-Partial-Component: Dashboard' -H 'X-Inertia-Partial-Data: chart'
has '"chart":[1,2,3]'
lacks '"stats":'

# 6. Once prop the client already has: value skipped, metadata kept. Prepend scroll intent.
inertia GET '/dashboard?page=2' -H 'X-Inertia-Except-Once-Props: countries' -H 'X-Inertia-Infinite-Scroll-Merge-Intent: prepend'
lacks '"countries":['
has '"onceProps":{"countries":'
has '"prependProps":["feed.data"]'
has '"scrollProps":{"feed":{"pageName":"page","previousPage":1,"nextPage":3,"currentPage":2,"reset":false}}'
has '"url":"/dashboard?page=2"'

# 7. Stale asset version: 409 with X-Inertia-Location, before the handler.
req GET /dashboard -H 'X-Inertia: true' -H 'X-Inertia-Version: stale'
status_is 409
header_is X-Inertia-Location "$BASE/dashboard"

# 8. PUT redirect becomes 303.
inertia PUT /users/1
status_is 303
header_is Location /dashboard

# 9. Validation failure (AddValidation + WithInertiaValidation): redirect back, errors on the next page, consumed once.
inertia POST /users -H 'Content-Type: application/json' -H "Referer: $BASE/users/create" --data '{"name":""}'
status_is 302
header_is Location "$BASE/users/create"
inertia GET /users/create
status_is 200
has '"errors":{"name":"'
has '"email":"'
inertia GET /users/create
has '"errors":{}'

# Non-Inertia clients still get the 400 problem details.
req POST /users -H 'Content-Type: application/json' --data '{"name":""}'
status_is 400

# 10. Valid form: flash survives the redirect.
inertia POST /users -H 'Content-Type: application/json' --data '{"name":"Ada","email":"ada@example.com"}'
status_is 302
header_is Location /dashboard
inertia GET /dashboard
has '"flash":{"toast":"Created Ada"}'

# 11. Back() with an error bag and flash.
inertia POST /back -H "Referer: $BASE/users/create"
status_is 302
header_is Location "$BASE/users/create"
inertia GET /users/create
has '"errors":{"createUser":{"email":"Taken"}}'
has '"flash":{"toast":"Rejected"}'

# 12. Flash round trip, consumed once.
inertia POST /flash
status_is 302
inertia GET /dashboard
has '"flash":{"toast":"Hello"}'
inertia GET /dashboard
lacks '"flash":'

# 13. Location(): 409 for Inertia requests, 302 otherwise.
inertia GET /external
status_is 409
header_is X-Inertia-Location https://example.com/landing
req GET /external
status_is 302
header_is Location https://example.com/landing

# 14. FastEndpoints (generator-discovered endpoints): page render, validation failure redirects back, flash after success.
inertia GET /fe/page
status_is 200
header_is X-Inertia true
has '"component":"Fe/Page"'
has '"framework":"FastEndpoints"'
has '"deferredProps":{"default":["lazy"]}'
inertia GET /fe/page -H 'X-Inertia-Partial-Component: Fe/Page' -H 'X-Inertia-Partial-Data: lazy'
has '"lazy":7'
inertia POST /fe/contacts -H 'Content-Type: application/json' -H "Referer: $BASE/fe/page" --data '{"name":""}'
status_is 302
header_is Location "$BASE/fe/page"
inertia GET /fe/page
has '"errors":{"name":"Name is required."}'
req POST /fe/contacts -H 'Content-Type: application/json' --data '{"name":""}'
status_is 400
inertia POST /fe/contacts -H 'Content-Type: application/json' --data '{"name":"Ada"}'
status_is 302
header_is Location /fe/page
inertia GET /fe/page
has '"flash":{"toast":"Contact Ada"}'

if [ "$FAILURES" -gt 0 ]; then
  echo "$FAILURES smoke check(s) failed. App log:"
  cat "$T/app.log"
  exit 1
fi
echo "AOT smoke: all checks passed."
