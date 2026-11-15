# syntax=docker/dockerfile:1
# Build from benchmarks/frameworks: docker build -f tanstack/Dockerfile --build-arg BENCH_MODE=ssr|spa [--target dev] .
FROM node:24.22.3-bookworm-slim AS source
WORKDIR /app
COPY tanstack/package.json tanstack/package-lock.json ./
RUN npm ci
COPY tanstack/ ./
COPY shared/js src/shared

FROM source AS build
ARG BENCH_MODE=ssr
ENV BENCH_MODE=$BENCH_MODE
RUN npm run build

FROM node:24.22.3-bookworm-slim AS runtime
WORKDIR /app
ENV NODE_ENV=production PORT=8080 HOST=0.0.0.0
COPY --from=build /app/.output .output
EXPOSE 8080
CMD ["node", ".output/server/index.mjs"]

# Dev-server cold start (run.sh --dev).
FROM source AS dev
EXPOSE 8080
CMD ["npx", "vite", "dev"]
