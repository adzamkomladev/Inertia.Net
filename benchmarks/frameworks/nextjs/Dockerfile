# syntax=docker/dockerfile:1
# Runtime-only image: run.sh runs `npm ci && BENCH_MODE=ssr|spa npm run build` on the host first; this copies the standalone output
# onto Alpine. Build from this folder: docker build .
FROM node:24.22.3-alpine3.24
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 PORT=8080 HOSTNAME=0.0.0.0
COPY .next/standalone ./
COPY .next/static .next/static
EXPOSE 8080
CMD ["node", "server.js"]
