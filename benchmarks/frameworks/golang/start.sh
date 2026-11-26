#!/bin/sh
# Container entrypoint: start Inertia's SSR server when BENCH_SSR=1, then serve with Gin on port 8080.
set -e
if [ "${BENCH_SSR:-0}" = 1 ]; then
  node ssr/ssr.js &
fi
exec ./bench
