# syntax=docker/dockerfile:1

ARG BUN_VERSION=1.4.4
ARG DOTNET_VERSION=10.0

# 1. Build the client and the self-contained SSR bundle with Bun.
FROM oven/bun:${BUN_VERSION} AS client
WORKDIR /src/docs

COPY docs/Inertia.Net.Docs/package.json docs/Inertia.Net.Docs/bun.lock ./
RUN bun install --frozen-lockfile

COPY docs/Inertia.Net.Docs/ ./
RUN bun run build

# 2. Publish the ASP.NET Core app with the built assets baked in.
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/Inertia.Net/Inertia.Net.csproj src/Inertia.Net/
COPY docs/Inertia.Net.Docs/Inertia.Net.Docs.csproj docs/Inertia.Net.Docs/
RUN dotnet restore docs/Inertia.Net.Docs/Inertia.Net.Docs.csproj

COPY src/Inertia.Net/ src/Inertia.Net/
COPY docs/Inertia.Net.Docs/ docs/Inertia.Net.Docs/
COPY --from=client /src/docs/wwwroot/build docs/Inertia.Net.Docs/wwwroot/build
COPY --from=client /src/docs/ssr docs/Inertia.Net.Docs/ssr
RUN dotnet publish docs/Inertia.Net.Docs/Inertia.Net.Docs.csproj \
    -c Release --no-restore -o /app/publish /p:UseAppHost=false

# 3. Runtime: ASP.NET Core plus Bun for the optional managed SSR server.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    INERTIA_SSR=1 \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=client /usr/local/bin/bun /usr/local/bin/bun
COPY --from=build /app/publish/ ./

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Inertia.Net.Docs.dll"]
