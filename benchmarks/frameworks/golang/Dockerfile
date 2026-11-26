# syntax=docker/dockerfile:1
# Runtime-only images: run.sh builds the Vite client and SSR bundles and the Go binary on the host first.
# Build from this folder: docker build --target spa|ssr .  Only the SSR image carries Node (Alpine's musl build), for gonertia's SSR server.

FROM alpine:3.24 AS spa
WORKDIR /app
COPY publish/bench ./bench
COPY app.html ./
COPY public/build public/build
ENV GIN_MODE=release
EXPOSE 8080
ENTRYPOINT ["./bench"]

FROM spa AS ssr
RUN apk add --no-cache nodejs=24.22.3-r0
COPY ssr ssr
COPY start.sh ./
RUN chmod +x start.sh
ENTRYPOINT ["./start.sh"]
