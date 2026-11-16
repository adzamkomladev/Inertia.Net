# syntax=docker/dockerfile:1
# Build from the repository root (the app references src/Inertia.Net, so it measures the code in this checkout):
#   docker build -f benchmarks/frameworks/inertianet/Dockerfile --target jit .     (or --target aot, --target dev)
FROM node:24.22.3-bookworm-slim AS node

FROM node AS client
WORKDIR /app
COPY benchmarks/frameworks/inertianet/package.json benchmarks/frameworks/inertianet/package-lock.json ./
RUN npm ci
COPY benchmarks/frameworks/inertianet/vite.config.js ./
COPY benchmarks/frameworks/inertianet/ClientApp ClientApp
COPY benchmarks/frameworks/shared/js ClientApp/shared
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:11.0.100 AS source
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/Inertia.Net src/Inertia.Net
COPY src/Inertia.Net.Analyzers src/Inertia.Net.Analyzers
COPY benchmarks/frameworks/inertianet benchmarks/frameworks/inertianet

FROM source AS build-jit
RUN dotnet publish benchmarks/frameworks/inertianet -c Release -o /out

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-aot AS build-aot
WORKDIR /src
COPY --from=source /src /src
RUN dotnet publish benchmarks/frameworks/inertianet -c Release -r linux-x64 -p:PublishAot=true -o /out && rm -f /out/*.dbg

# The managed SSR process needs Node next to the app (only the binary: the SSR bundle inlines its dependencies).
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble AS jit
COPY --from=node /usr/local/bin/node /usr/local/bin/node
WORKDIR /app
COPY --from=build-jit /out ./
COPY --from=client /app/wwwroot wwwroot
COPY --from=client /app/ssr ssr
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "InertiaNetBench.dll"]

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.15-noble AS aot
COPY --from=node /usr/local/bin/node /usr/local/bin/node
WORKDIR /app
COPY --from=build-aot /out ./
COPY --from=client /app/wwwroot wwwroot
COPY --from=client /app/ssr ssr
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["./InertiaNetBench"]

# Dev-server cold start (run.sh --dev): Vite and dotnet watch side by side, as a developer runs them.
FROM source AS dev
COPY --from=node /usr/local/bin/node /usr/local/bin/node
WORKDIR /src/benchmarks/frameworks/inertianet
COPY --from=client /app/node_modules node_modules
COPY benchmarks/frameworks/shared/js ClientApp/shared
# Packages restored, nothing built: the same starting point as node_modules or vendor/ in the other dev images.
RUN dotnet restore
ENV ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_HTTP_PORTS=8080 DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1
EXPOSE 8080
CMD ["sh", "-c", "node node_modules/vite/bin/vite.js & exec dotnet watch run --non-interactive"]
