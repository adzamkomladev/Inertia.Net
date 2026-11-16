#!/bin/sh
# Container entrypoint: cache the config for this environment, start Inertia's SSR server when BENCH_SSR=1, then serve with Octane on FrankenPHP.
set -e
export INERTIA_SSR_ENABLED="${BENCH_SSR:-0}"
php artisan optimize --quiet
if [ "$INERTIA_SSR_ENABLED" = 1 ]; then php artisan inertia:start-ssr & fi
exec php artisan octane:frankenphp --host=0.0.0.0 --port=8080 --workers="${OCTANE_WORKERS:-auto}"
