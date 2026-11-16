# syntax=docker/dockerfile:1
# Runtime-only images: run.sh builds everything on the host first (npm run build, dotnet publish; aot.Dockerfile for Native AOT)
# and this file copies the output onto Alpine. Build from this folder:
#   docker build --target jit-ssr|jit-spa|aot-ssr|aot-spa .
# Only the SSR images carry Node (Alpine's musl build), for the SSR process Inertia.Net starts; SPA mode never runs it.

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-alpine3.24 AS jit-spa
WORKDIR /app
COPY publish/jit ./
COPY wwwroot/build wwwroot/build
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "InertiaNetBench.dll"]

FROM jit-spa AS jit-ssr
RUN apk add --no-cache nodejs=24.22.3-r0
COPY ssr ssr

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.15-alpine3.24 AS aot-spa
WORKDIR /app
COPY publish/aot ./
COPY wwwroot/build wwwroot/build
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["./InertiaNetBench"]

FROM aot-spa AS aot-ssr
RUN apk add --no-cache nodejs=24.22.3-r0
COPY ssr ssr
