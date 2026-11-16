#!/usr/bin/env bash
# The framework comparison (README.md next to this file): builds each app on the host and into an Alpine Docker image, runs it next to
# Postgres and Redis with the same CPU and memory limits, checks that every app answers the same thing (--check), then measures throughput, latency, CPU per request,
# memory, cold start and image size, and writes a markdown report.
#
#   run.sh --check                        parity check only
#   run.sh --smoke                        parity check, then very short loads (proves the pipeline, numbers mean nothing)
#   run.sh [options]                      parity check, then the benchmark
#   run.sh --dev                          dev-server cold start only
#
# Options: --frameworks inertianet,inertianet-aot,nextjs,tanstack,nuxt,laravel  --modes ssr,spa
#          (also inertianet-tuned and inertianet-aot-tuned: the same images with BENCH_TUNED=1, Inertia.Net's caches and Node instances on)
#          --routes home,todos25,todos100,dashboard,new,create,invalid,compute,parallel
#          --seconds 10 --warmup 5 --connections 16 --rates 50,200 --cpus 2 --memory 2g --scale-cpus 1,2,4 --port 18080
#          --out artifacts/framework-bench.md --keep-services (leave Postgres and Redis running)
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
DOTNET=${DOTNET:-dotnet}

FRAMEWORKS=inertianet,inertianet-aot,nextjs,tanstack,nuxt,laravel
MODES=ssr,spa
ROUTES=home,todos25,todos100,dashboard,new,create,invalid,compute,parallel
DURATION=10 WARMUP=5 CONNECTIONS=16 RATES=50,200 CPUS=2 MEMORY=2g SCALE=1,2,4 PORT=18080
OUT=$ROOT/artifacts/framework-bench.md
CHECK_ONLY=0 SMOKE=0 DEV=0 KEEP=0
while [ $# -gt 0 ]; do
  case $1 in
    --check) CHECK_ONLY=1 ;;
    --smoke) SMOKE=1 DURATION=2 WARMUP=1 RATES=20 ;;
    --dev) DEV=1 ;;
    --keep-services) KEEP=1 ;;
    --frameworks) FRAMEWORKS=$2; shift ;;
    --modes) MODES=$2; shift ;;
    --routes) ROUTES=$2; shift ;;
    --seconds) DURATION=$2; shift ;;
    --warmup) WARMUP=$2; shift ;;
    --connections) CONNECTIONS=$2; shift ;;
    --rates) RATES=$2; shift ;;
    --cpus) CPUS=$2; shift ;;
    --memory) MEMORY=$2; shift ;;
    --scale-cpus) SCALE=$2; shift ;;
    --port) PORT=$2; shift ;;
    --out) OUT=$2; shift ;;
    *) echo "Unknown option: $1 (see the top of $0)" >&2; exit 2 ;;
  esac
  shift
done

NET=inertia-bench-fw-net PG=inertia-bench-fw-postgres REDIS=inertia-bench-fw-redis APP=inertia-bench-fw-app
BASE=http://127.0.0.1:$PORT
WORK=$(mktemp -d)
log() { echo "[$(date +%T)] $*" >&2; }

cleanup() {
  docker rm -f "$APP" >/dev/null 2>&1 || true
  if [ "$KEEP" = 0 ]; then
    docker rm -f "$PG" "$REDIS" >/dev/null 2>&1 || true
    docker network rm "$NET" >/dev/null 2>&1 || true
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT

if ss -ltn | awk '{print $4}' | grep -q ":$PORT\$" && ! docker ps --format '{{.Names}}' | grep -qx "$APP"; then
  echo "Port $PORT is in use; pick another with --port." >&2
  exit 1
fi

# --- Postgres and Redis: one of each on a dedicated network, seeded from shared/schema.sql ---
services_up() {
  docker network inspect "$NET" >/dev/null 2>&1 || docker network create "$NET" >/dev/null
  if ! docker container inspect "$PG" >/dev/null 2>&1; then
    log "starting Postgres"
    docker run -d --name "$PG" --network "$NET" --cpus 2 --memory 1g -e POSTGRES_USER=bench -e POSTGRES_PASSWORD=bench -e POSTGRES_DB=bench \
      -v "$HERE/shared/schema.sql:/docker-entrypoint-initdb.d/schema.sql:ro" postgres:18.7-bookworm -c max_connections=200 >/dev/null
  fi
  docker container inspect "$REDIS" >/dev/null 2>&1 || docker run -d --name "$REDIS" --network "$NET" --cpus 1 --memory 256m redis:8.8.4-alpine >/dev/null
  for _ in $(seq 120); do
    [ "$(docker exec "$PG" psql -U bench -d bench -tAc 'SELECT count(*) FROM todos' 2>/dev/null)" = 10000 ] && return
    sleep 1
  done
  echo "Postgres did not come up with the seed data." >&2
  exit 1
}

# --- Images ---
# Apps are built on the host (dotnet publish, npm ci && npm run build); the Dockerfiles only copy the output onto Alpine runtime images.
# Exceptions: Native AOT is published in the pinned Alpine AOT SDK image (aot.Dockerfile) so it links against musl, and Laravel's
# composer install runs in a pinned build stage, as the host has no PHP. Dev images (Dockerfile.dev) install everything themselves.
NPM=${NPM:-npm}
declare -A PREPARED=()

tag() { # framework mode -> image tag (mode dev: the dev-server image)
  set -- "${1%-tuned}" "$2" # a tuned app is its plain app's image, started with BENCH_TUNED=1
  case $1 in
    inertianet) [ "$2" = dev ] && echo inertia-bench-fw-inertianet:dev || echo "inertia-bench-fw-inertianet:jit-$2" ;;
    inertianet-aot) echo "inertia-bench-fw-inertianet:aot-$2" ;;
    *) echo "inertia-bench-fw-$1:$2" ;;
  esac
}

app_dir() { case $1 in inertianet*) echo "$HERE/inertianet" ;; *) echo "$HERE/$1" ;; esac; }

prepare() { # framework -> the shared components copied in, npm packages installed, and the mode-independent host builds
  local dir shared
  dir=$(app_dir "$1")
  [ -n "${PREPARED[$1]:-}" ] && return
  log "building $1 on the host"
  if [ -z "${PREPARED[$dir]:-}" ]; then # the JIT and AOT apps share one client build
    case $1 in
      inertianet*) shared=ClientApp/shared ;; nextjs | nuxt) shared=shared ;; tanstack) shared=src/shared ;; laravel) shared=resources/js/shared ;;
    esac
    rm -rf "${dir:?}/$shared" && cp -r "$HERE/shared/ts" "$dir/$shared"
    (cd "$dir" && "$NPM" ci --no-audit --no-fund --loglevel=error >&2)
    [[ $1 == nextjs || $1 == tanstack || $1 == nuxt ]] || (cd "$dir" && "$NPM" run build >&2)
    PREPARED[$dir]=1
  fi
  case $1 in
    inertianet) "$DOTNET" publish "$dir" -c Release -p:UseAppHost=false -o "$dir/publish/jit" -v quiet -nologo >&2 ;;
    inertianet-aot)
      rm -rf "$dir/publish/aot"
      docker build -q -f "$dir/aot.Dockerfile" --output "type=local,dest=$dir/publish/aot" "$ROOT" >/dev/null ;;
  esac
  PREPARED[$1]=1
}

build() { # framework mode
  set -- "${1%-tuned}" "$2"
  local dir target=() file=Dockerfile context
  dir=$(app_dir "$1") context=$dir
  if [ "$2" = dev ]; then
    file=Dockerfile.dev context=$HERE
    [[ $1 == inertianet* ]] && context=$ROOT
  else
    prepare "$1"
    case $1 in
      inertianet) target=(--target "jit-$2") ;;
      inertianet-aot) target=(--target "aot-$2") ;;
      laravel) target=(--target "$2") ;;
      # Next.js, TanStack Start and Nuxt decide SSR or SPA at build time.
      nextjs | tanstack | nuxt) (cd "$dir" && BENCH_MODE=$2 "$NPM" run build >&2) ;;
    esac
  fi
  log "building $(tag "$1" "$2")"
  docker build -q -f "$dir/$file" "${target[@]}" -t "$(tag "$1" "$2")" "$context" >/dev/null
}

app_env() { # framework mode cpus -> docker run -e arguments, one per line
  local ssr=0
  [ "$2" = ssr ] && ssr=1
  case $1 in
    inertianet*) printf -- '-e\n%s\n' "BENCH_SSR=$ssr" "BENCH_TUNED=$([[ $1 == *-tuned ]] && echo 1 || echo 0)" "DATABASE_URL=Host=$PG;Username=bench;Password=bench;Database=bench;Maximum Pool Size=10" "REDIS_URL=$REDIS:6379" ;;
    # Node sizes its worker pool from the host's CPUs, not the container's quota, so the pool gets the CPU limit (rounded up).
    nextjs | tanstack | nuxt) printf -- '-e\n%s\n' "BENCH_WORKERS=$(awk "BEGIN{print int($3 + 0.999)}")" "DATABASE_URL=postgres://bench:bench@$PG/bench" "REDIS_URL=redis://$REDIS:6379" ;;
    # FrankenPHP workers block on I/O, so two per CPU; each keeps one Postgres connection.
    laravel) printf -- '-e\n%s\n' "BENCH_SSR=$ssr" "OCTANE_WORKERS=$(awk "BEGIN{print int($3 * 2 + 0.999)}")" "DB_HOST=$PG" "REDIS_HOST=$REDIS" ;;
  esac
}

now_ms() { echo $(($(date +%s%N) / 1000000)); }

# Starts the app (replacing any previous one) and prints the cold start in ms: docker run until / answers 200.
start_app() { # framework mode cpus [dev]
  local image start env
  image=$(tag "$1" "${4:-$2}")
  # Rebuilt if it disappeared since the build step (a shared host may prune images that no container uses).
  docker image inspect "$image" >/dev/null 2>&1 || build "$1" "${4:-$2}"
  mapfile -t env < <(app_env "$1" "$2" "$3")
  docker rm -f "$APP" >/dev/null 2>&1 || true
  docker exec "$REDIS" redis-cli FLUSHALL >/dev/null
  docker exec "$PG" psql -U bench -d bench -qc 'TRUNCATE submissions RESTART IDENTITY' >/dev/null
  start=$(now_ms)
  docker run -d --name "$APP" --network "$NET" --cpus "$3" --memory "$MEMORY" -p "127.0.0.1:$PORT:8080" "${env[@]}" "$image" >/dev/null
  for _ in $(seq 6000); do
    if [ "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/")" = 200 ]; then
      echo $(($(now_ms) - start))
      # The Laravel SSR server starts beside the app: wait until pages are server-rendered before measuring anything.
      if [ "$2" = ssr ]; then
        for _ in $(seq 600); do curl -s "$BASE/" | grep -q 'data-bench=' && return; sleep 0.1; done
        echo "$1 never server-rendered /" >&2
        docker logs --tail 30 "$APP" >&2
        exit 1
      fi
      return
    fi
    sleep 0.05
  done
  echo "$1 ($2) did not answer 200 on / within 5 minutes" >&2
  docker logs --tail 50 "$APP" >&2
  exit 1
}

cgroup() {
  local id
  id=$(docker inspect -f '{{.Id}}' "$APP")
  for dir in "/sys/fs/cgroup/system.slice/docker-$id.scope" "/sys/fs/cgroup/docker/$id"; do [ -d "$dir" ] && echo "$dir" && return; done
  echo "No cgroup v2 directory for $APP" >&2
  exit 1
}

route_path() {
  case $1 in
    home) echo / ;; todos25) echo '/todos?perPage=25' ;; todos100) echo '/todos?perPage=100' ;; dashboard) echo /dashboard ;;
    compute) echo '/compute?n=20000' ;; parallel) echo '/compute/parallel?n=20000' ;; new) echo /todos/new ;;
    *) echo "Unknown route $1" >&2; exit 2 ;;
  esac
}

route_page() { case $1 in todos*) echo todos ;; parallel) echo compute ;; *) echo "$1" ;; esac; }

is_inertia() { [[ $1 == inertianet* || $1 == laravel ]]; }

# --- The new-todo form: POST /todos (Inertia apps: 303 back to the form) or POST /api/todos (Node apps: 201, or 422 with errors) ---
VALID='{"title":"Buy milk","priority":2,"assignee":"ada","due":"2026-06-01"}'
INVALID='{"title":"x","priority":9,"assignee":"bob","due":"2027-02-30"}' # fails all four rules
post_path() { is_inertia "$1" && echo /todos || echo /api/todos; }
post_status() { # framework valid|invalid -> the status that counts as success
  # Inertia only needs a 303 after PUT, PATCH and DELETE, so Inertia.Net (both posts) and Laravel (its validation redirect) answer a POST with 302.
  if [[ $1 == inertianet* ]] || { [ "$1" = laravel ] && [ "$2" = invalid ]; }; then echo 302; elif is_inertia "$1"; then echo 303; elif [ "$2" = valid ]; then echo 201; else echo 422; fi
}
# A session as a browser has it after opening the form: the cookies GET /todos/new set, and the headers an Inertia form post
# sends (with X-XSRF-TOKEN when the app set an XSRF-TOKEN cookie). The load client reuses them for every post of a run.
form_session() { # framework -> fills $WORK/jar and FORM_HEADERS
  local fw=$1 cookies xsrf
  rm -f "$WORK/jar"
  curl -s -o /dev/null -c "$WORK/jar" -H 'Accept: text/html, application/xhtml+xml' "$BASE/todos/new"
  FORM_HEADERS=("Content-Type: application/json" "Accept: text/html, application/xhtml+xml" "X-Requested-With: XMLHttpRequest" "Referer: $BASE/todos/new")
  if is_inertia "$fw"; then FORM_HEADERS+=("X-Inertia: true" "X-Inertia-Version: $(inertia_version)"); fi
  cookies=$(awk -F'\t' 'NF >= 7 && ($1 !~ /^#/ || $1 ~ /^#HttpOnly_/) {printf "%s%s=%s", (n++ ? "; " : ""), $6, $7}' "$WORK/jar")
  [ -n "$cookies" ] && FORM_HEADERS+=("Cookie: $cookies")
  xsrf=$(awk -F'\t' 'NF >= 7 && $6 == "XSRF-TOKEN" {print $7}' "$WORK/jar")
  [ -n "$xsrf" ] && FORM_HEADERS+=("X-XSRF-TOKEN: $(printf '%b' "${xsrf//%/\\x}")")
  true
}
form_args() { local h; for h in "${FORM_HEADERS[@]}"; do printf -- '--header\n%s\n' "$h"; done; }
post_form() { # framework body -> the status code; the response body in $WORK/post, cookies it sets (e.g. flashed errors) in the jar
  local args=() h
  for h in "${FORM_HEADERS[@]}"; do [[ $h == Cookie:* ]] || args+=(-H "$h"); done
  curl -s -o "$WORK/post" -w '%{http_code}' -b "$WORK/jar" -c "$WORK/jar" -X POST "${args[@]}" --data "$2" "$BASE$(post_path "$1")"
}
# The errors the user sees after an invalid post: the next Inertia visit to the form (same session), or the 422 body.
form_errors() { # framework
  local args=() h
  if is_inertia "$1"; then
    for h in "${FORM_HEADERS[@]}"; do [[ $h == X-Inertia* ]] && args+=(-H "$h"); done
    curl -s -b "$WORK/jar" -c "$WORK/jar" "${args[@]}" "$BASE/todos/new" | jq -S '.props.errors'
  else
    jq -S '.errors' "$WORK/post"
  fi
}

# The page's props as JSON: an Inertia visit (X-Inertia) for the Inertia apps, the /api route for the others.
json_url() { if is_inertia "$1" || [ "$2" != / ]; then echo "$BASE$(is_inertia "$1" || echo /api)$2"; else echo "$BASE/api/home"; fi; }
json_headers() { is_inertia "$1" && printf '%s\n' --header "X-Inertia: true" --header "X-Inertia-Version: $(inertia_version)"; true; }
inertia_version() { curl -s "$BASE/" | grep -o '"version":"[^"]*"' | head -1 | cut -d'"' -f4; }
props() { # framework path -> the props, normalized
  local headers=()
  is_inertia "$1" && headers=(-H "X-Inertia: true" -H "X-Inertia-Version: $(inertia_version)")
  curl -sf "${headers[@]}" "$(json_url "$1" "$2")" | jq -S "$(is_inertia "$1" && echo '.props | del(.errors)' || echo .)"
}

# --- Parity check ---
FAILED=0
fail() { echo "FAIL $*" >&2; FAILED=1; }

check_app() { # framework mode
  local fw=$1 mode=$2 path page html rows want status
  start_app "$fw" "$mode" "$CPUS" >/dev/null
  for route in home todos25 todos100 dashboard compute parallel; do
    path=$(route_path "$route") page=$(route_page "$route")
    html=$(curl -s -w '\n%{http_code}' "$BASE$path")
    [ "${html##*$'\n'}" = 200 ] || fail "$fw $mode $path answered ${html##*$'\n'}"
    rows=$(grep -o 'data-todo=' <<<"$html" | wc -l || true)
    if [ "$mode" = ssr ]; then
      grep -q "data-bench=\"$page\"" <<<"$html" || fail "$fw $mode $path is not server-rendered"
      want=0
      [ "$route" = todos25 ] && want=25
      [ "$route" = todos100 ] && want=100
      [ "$rows" = "$want" ] || fail "$fw $mode $path rendered $rows rows, expected $want"
      [[ $page != compute ]] || grep -q 'data-checksum="[0-9a-f]\{64\}"' <<<"$html" || fail "$fw $mode $path has no checksum"
    else
      grep -q 'data-bench=' <<<"$html" && fail "$fw $mode $path is server-rendered in SPA mode"
      [ "$rows" = 0 ] || fail "$fw $mode $path rendered rows in SPA mode"
    fi
    props "$fw" "$path" >"$WORK/$fw-$mode-$route.json" || fail "$fw $mode JSON for $path"
  done
  html=$(curl -s "$BASE/todos/new")
  if [ "$mode" = ssr ]; then grep -q 'data-bench="new"' <<<"$html" || fail "$fw $mode /todos/new is not server-rendered"
  else ! grep -q 'data-bench=' <<<"$html" || fail "$fw $mode /todos/new is server-rendered in SPA mode"; fi
  # The form: one invalid post (nothing written, the errors shown), then valid posts on the same session, which each add a row.
  form_session "$fw"
  status=$(post_form "$fw" "$INVALID")
  [ "$status" = "$(post_status "$fw" invalid)" ] || fail "$fw $mode invalid post answered $status"
  form_errors "$fw" >"$WORK/$fw-$mode-errors.json" || fail "$fw $mode shows no errors after an invalid post"
  for _ in 1 2 3; do
    status=$(post_form "$fw" "$VALID")
    [ "$status" = "$(post_status "$fw" valid)" ] || fail "$fw $mode valid post answered $status"
  done
  [ "$(docker exec "$PG" psql -U bench -d bench -tAc "SELECT count(*) FROM submissions WHERE title = 'Buy milk' AND due_at = '2026-06-01'")" = 3 ] ||
    fail "$fw $mode: the three valid posts did not each write one row (or the invalid one wrote)"
  # What every app must agree on, byte for byte: the features, the rows, the dashboard aggregates, both checksums and the form errors.
  jq -S -s '{home: .[0].features, todos25: .[1].todos, todos100: .[2].todos, page: [.[1].page, .[1].perPage], dashboard: .[3].stats,
    compute: .[4].checksum, parallel: .[5].checksum, errors: .[6]}' \
    "$WORK/$fw-$mode-"{home,todos25,todos100,dashboard,compute,parallel,errors}.json >"$WORK/parity-$fw-$mode.json"
  log "checked $fw $mode: $(jq -r '"\(.todos25 | length) + \(.todos100 | length) rows, checksum \(.compute[:12])…"' "$WORK/parity-$fw-$mode.json")"
}

run_check() {
  local first=""
  for fw in ${FRAMEWORKS//,/ }; do
    for mode in ${MODES//,/ }; do
      check_app "$fw" "$mode"
      if [ -z "$first" ]; then
        first=$WORK/parity-$fw-$mode.json
        jq -e '(.todos25 | length) == 25 and (.todos100 | length) == 100 and .compute == .parallel and (.compute | length) == 64 and
          (.errors | keys) == ["assignee", "due", "priority", "title"]' "$first" >/dev/null ||
          fail "$fw $mode: wrong row counts or checksums"
      elif ! cmp -s "$first" "$WORK/parity-$fw-$mode.json"; then
        fail "$fw $mode differs from $(basename "$first" .json):"
        diff "$first" "$WORK/parity-$fw-$mode.json" | head -20 >&2 || true
      fi
    done
  done
  [ "$FAILED" = 0 ] || { echo "Parity check failed." >&2; exit 1; }
  log "parity check passed: every route answers 200, SSR pages hold the rendered rows, SPA pages do not, and all apps agree"
}

# --- Load ---
LOAD_DLL=$ROOT/tests/Inertia.Net.HostingProbe/bin/Release/net10.0/Inertia.Net.HostingProbe.dll
load() { "$DOTNET" "$LOAD_DLL" load "$@"; } # prints {"rps","p50Ms","p95Ms","p99Ms","p999Ms","errors","cpuMicrosPerRequest","peakMb"}

name() {
  case $1 in
    inertianet) echo "Inertia.Net (JIT)" ;; inertianet-aot) echo "Inertia.Net (Native AOT)" ;; nextjs) echo Next.js ;;
    inertianet-tuned) echo "Inertia.Net (JIT, tuned)" ;; inertianet-aot-tuned) echo "Inertia.Net (Native AOT, tuned)" ;;
    tanstack) echo "TanStack Start" ;; nuxt) echo Nuxt ;; laravel) echo "Laravel (Octane, FrankenPHP)" ;;
  esac
}

mode_name() {
  if [ "$2" = ssr ]; then echo SSR
  elif [ "$1" = nextjs ]; then echo "SPA (nearest)"
  else echo SPA
  fi
}

mb() { awk "BEGIN{printf \"%.0f\", $1 / 1048576}"; }
OVERLOADED="overloaded"

bench_app() { # framework mode
  local fw=$1 mode=$2 cold cg rest size path url result rates line
  cold=$(start_app "$fw" "$mode" "$CPUS")
  cg=$(cgroup)
  # Every route once in both forms, so all code paths are loaded before memory at rest is read.
  form_session "$fw"
  for route in ${ROUTES//,/ }; do
    case $route in
      create) post_form "$fw" "$VALID" >/dev/null ;;
      invalid) post_form "$fw" "$INVALID" >/dev/null ;;
      *) path=$(route_path "$route"); curl -s -o /dev/null "$BASE$path"; props "$fw" "$path" >/dev/null || true ;;
    esac
  done
  sleep 2
  rest=$(mb "$(cat "$cg/memory.current")")
  size=$(mb "$(docker image inspect -f '{{.Size}}' "$(tag "$fw" "$mode")")")
  echo "| $(name "$fw") | $(mode_name "$fw" "$mode") | $size | $cold | $rest |" >>"$WORK/apps.md"
  for route in ${ROUTES//,/ }; do
    if [[ $route == create || $route == invalid ]]; then
      bench_post "$fw" "$mode" "$route" "$cg"
      continue
    fi
    path=$(route_path "$route")
    for variant in page json; do
      # The Node apps' form page has no data to fetch, so it has no JSON form.
      [ "$route" = new ] && [ "$variant" = json ] && ! is_inertia "$fw" && continue
      local args=()
      if [ "$variant" = page ]; then
        url=$BASE$path
        # SSR rows count a page that was not server-rendered as an error.
        [ "$mode" = ssr ] && args=(--expect "data-bench=\"$(route_page "$route")\"")
      else
        url=$(json_url "$fw" "$path")
        mapfile -t args < <(json_headers "$fw")
      fi
      result=$(load "$url" --connections "$CONNECTIONS" --warmup "$WARMUP" --seconds "$DURATION" --cgroup "$cg" "${args[@]}")
      rates=""
      for rate in ${RATES//,/ }; do
        local at
        at=$(load "$url" --connections "$CONNECTIONS" --rate "$rate" --warmup 1 --seconds "$DURATION" "${args[@]}")
        rates+=" $(jq -r --arg o "$OVERLOADED" 'if .errors > 0 then $o else "\(.p50Ms) / \(.p99Ms)" end' <<<"$at") |"
      done
      line=$(jq -r --arg o "$OVERLOADED" 'if .errors > 0 then "\($o) (\(.errors) errors) | | | | |" else
        "\(.rps | round) | \(.p50Ms) | \(.p99Ms) | \(.cpuMicrosPerRequest | round) | \(.peakMb | round) |" end' <<<"$result")
      echo "| $(name "$fw") | $(mode_name "$fw" "$mode") | \`$path\` | $variant | $line$rates" >>"$WORK/routes.md"
      log "$fw $mode $path $variant: $line$rates"
    done
  done
}

# A form post under load: every request is the same post on one session; success is the expected status (the redirect is not followed).
bench_post() { # framework mode create|invalid cgroup
  local fw=$1 kind=valid body=$VALID args result rates line at
  [ "$3" = invalid ] && kind=invalid body=$INVALID
  form_session "$fw"
  mapfile -t args < <(form_args)
  args+=(--method POST --body "$body" --status "$(post_status "$fw" "$kind")")
  result=$(load "$BASE$(post_path "$fw")" --connections "$CONNECTIONS" --warmup "$WARMUP" --seconds "$DURATION" --cgroup "$4" "${args[@]}")
  rates=""
  for rate in ${RATES//,/ }; do
    at=$(load "$BASE$(post_path "$fw")" --connections "$CONNECTIONS" --rate "$rate" --warmup 1 --seconds "$DURATION" "${args[@]}")
    rates+=" $(jq -r --arg o "$OVERLOADED" 'if .errors > 0 then $o else "\(.p50Ms) / \(.p99Ms)" end' <<<"$at") |"
  done
  line=$(jq -r --arg o "$OVERLOADED" 'if .errors > 0 then "\($o) (\(.errors) errors) | | | | |" else
    "\(.rps | round) | \(.p50Ms) | \(.p99Ms) | \(.cpuMicrosPerRequest | round) | \(.peakMb | round) |" end' <<<"$result")
  echo "| $(name "$fw") | $(mode_name "$fw" "$mode") | \`POST $(post_path "$fw")\` | $kind | $line$rates" >>"$WORK/routes.md"
  log "$fw $2 POST $kind: $line$rates"
}

# Multi-core scaling: the compute JSON routes (no rendering) at each CPU limit, as throughput and single-request latency.
bench_scaling() { # framework
  local fw=$1 url single multi
  for cpus in ${SCALE//,/ }; do
    start_app "$fw" ssr "$cpus" >/dev/null
    for route in compute parallel; do
      url=$(json_url "$fw" "$(route_path "$route")")
      mapfile -t args < <(json_headers "$fw")
      single=$(load "$url" --connections 1 --warmup "$WARMUP" --seconds "$DURATION" "${args[@]}")
      multi=$(load "$url" --connections "$CONNECTIONS" --warmup "$WARMUP" --seconds "$DURATION" "${args[@]}")
      echo "| $(name "$fw") | \`$(route_path "$route")\`$([ "$fw" = laravel ] && [ "$route" = parallel ] && echo ' (sequential in PHP)') | $cpus |" \
        "$(jq -r --arg o "$OVERLOADED" 'if .errors > 0 then $o else "\(.rps | round)" end' <<<"$multi") |" \
        "$(jq -r --arg o "$OVERLOADED" 'if .errors > 0 then $o else "\(.p50Ms)" end' <<<"$single") |" >>"$WORK/scaling.md"
    done
    log "$fw at $cpus CPUs done"
  done
}

bench_dev() {
  echo "| Framework | Dev command | Cold start to first 200 (ms) |" >"$WORK/dev.md"
  echo "|---|---|---:|" >>"$WORK/dev.md"
  for fw in ${FRAMEWORKS//,/ }; do
    [ "$fw" = inertianet-aot ] && continue
    local cmd ms
    case $fw in
      inertianet) cmd="dotnet watch + vite" ;; nextjs) cmd="next dev" ;; tanstack) cmd="vite dev" ;; nuxt) cmd="nuxt dev" ;; laravel) cmd="php artisan serve + vite" ;;
    esac
    ms=$(start_app "$fw" spa "$CPUS" dev)
    echo "| $(name "$fw") | $cmd | $ms |" >>"$WORK/dev.md"
    log "$fw dev server up"
  done
}

services_up
if [ "$DEV" = 1 ]; then
  for fw in ${FRAMEWORKS//,/ }; do [ "$fw" = inertianet-aot ] || build "$fw" dev; done
  bench_dev
  mkdir -p "$(dirname "$OUT")"
  { echo "## Dev-server cold start"; echo; cat "$WORK/dev.md"; } | tee "$OUT"
  exit 0
fi

for fw in ${FRAMEWORKS//,/ }; do
  for mode in ${MODES//,/ }; do build "$fw" "$mode"; done
done
run_check
[ "$CHECK_ONLY" = 1 ] && exit 0

"$DOTNET" build "$ROOT/tests/Inertia.Net.HostingProbe" -c Release -v quiet -nologo >/dev/null
echo "| Framework | Mode | Image MB | Cold start ms | Memory at rest MB |" >"$WORK/apps.md"
echo "|---|---|---:|---:|---:|" >>"$WORK/apps.md"
rate_headers="" rate_rule=""
for rate in ${RATES//,/ }; do rate_headers+=" p50 / p99 ms at $rate req/s |" rate_rule+="---:|"; done
echo "| Framework | Mode | Route | Response | Req/s | p50 ms | p99 ms | CPU µs per request | Peak memory MB |$rate_headers" >"$WORK/routes.md"
echo "|---|---|---|---|---:|---:|---:|---:|---:|$rate_rule" >>"$WORK/routes.md"
echo "| Framework | Route | CPUs | Req/s ($CONNECTIONS connections) | p50 ms (1 connection) |" >"$WORK/scaling.md"
echo "|---|---|---:|---:|---:|" >>"$WORK/scaling.md"
for fw in ${FRAMEWORKS//,/ }; do
  for mode in ${MODES//,/ }; do bench_app "$fw" "$mode"; done
done
if [[ ,$ROUTES, == *,compute,* ]]; then # the scaling runs both compute routes, so a split run does it once, in the compute part
  for fw in ${FRAMEWORKS//,/ }; do bench_scaling "$fw"; done
fi

mkdir -p "$(dirname "$OUT")"
{
  [ "$SMOKE" = 1 ] && echo "> Smoke run (--smoke): loads of $DURATION s, numbers are not meaningful." && echo
  echo "Each app in Docker with --cpus $CPUS --memory $MEMORY; $CONNECTIONS keep-alive connections, $WARMUP s warmup, $DURATION s measured."
  echo "Host: $(nproc) CPUs, $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs). \"$OVERLOADED\": at least one request failed or (SSR) was not server-rendered."
  echo
  echo "## Apps"; echo; cat "$WORK/apps.md"; echo
  echo "## Routes"; echo
  echo "\`page\` is a first visit (HTML); \`json\` is the data a client-side navigation fetches (an Inertia visit, or the app's /api route)."
  echo "\`POST\` rows are the new-todo form: \`valid\` validates and inserts a row, \`invalid\` fails validation on all four fields."
  echo "CPU and peak memory are the whole container's (cgroup): every process, including Node SSR servers and PHP workers."
  echo; cat "$WORK/routes.md"; echo
  if [ -s "$WORK/scaling.md" ] && [ "$(wc -l <"$WORK/scaling.md")" -gt 2 ]; then echo "## Multi-core scaling"; echo; cat "$WORK/scaling.md"; fi
} | tee "$OUT"
