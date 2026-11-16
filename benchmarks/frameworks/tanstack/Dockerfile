# syntax=docker/dockerfile:1
# Runtime-only image: run.sh runs `npm ci && BENCH_MODE=ssr|spa npm run build` on the host first; this copies the Nitro output
# onto Alpine. Build from this folder: docker build .
FROM node:24.22.3-alpine3.24
WORKDIR /app
ENV NODE_ENV=production PORT=8080 HOST=0.0.0.0
COPY .output .output
EXPOSE 8080
CMD ["node", ".output/server/index.mjs"]
