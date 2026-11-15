# syntax=docker/dockerfile:1
# Build from benchmarks/frameworks: docker build -f nextjs/Dockerfile --build-arg BENCH_MODE=ssr|spa [--target dev] .
FROM node:24.22.3-bookworm-slim AS source
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY nextjs/package.json nextjs/package-lock.json ./
RUN npm ci
COPY nextjs/ ./
COPY shared/js shared

FROM source AS build
ARG BENCH_MODE=ssr
ENV BENCH_MODE=$BENCH_MODE
RUN npm run build

FROM node:24.22.3-bookworm-slim AS runtime
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 PORT=8080 HOSTNAME=0.0.0.0
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static .next/static
EXPOSE 8080
CMD ["node", "server.js"]

# Dev-server cold start (run.sh --dev).
FROM source AS dev
EXPOSE 8080
CMD ["npx", "next", "dev", "-H", "0.0.0.0", "-p", "8080"]
