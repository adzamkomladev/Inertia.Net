# syntax=docker/dockerfile:1
# Production image: the docs web app, SSR (bun, supervised by the app), port 8080, health /health.
# Built from the repository root so the docs project can reference src/Inertia.Net.

# 1. Frontend: client bundle (wwwroot/build) + SSR bundle (ssr/)
FROM oven/bun:1-debian AS web
WORKDIR /src/docs
COPY docs/Inertia.Net.Docs/package.json docs/Inertia.Net.Docs/bun.lock ./
RUN bun install --frozen-lockfile
COPY docs/Inertia.Net.Docs/vite.config.ts docs/Inertia.Net.Docs/tsconfig.json ./
COPY docs/Inertia.Net.Docs/ClientApp ./ClientApp
COPY docs/Inertia.Net.Docs/content ./content
COPY docs/Inertia.Net.Docs/scripts ./scripts
RUN bun run build

# 2. Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/Inertia.Net/Inertia.Net.csproj src/Inertia.Net/
COPY docs/Inertia.Net.Docs/Inertia.Net.Docs.csproj docs/Inertia.Net.Docs/
RUN dotnet restore docs/Inertia.Net.Docs/Inertia.Net.Docs.csproj
COPY src/Inertia.Net/ src/Inertia.Net/
COPY docs/Inertia.Net.Docs/ docs/Inertia.Net.Docs/
RUN dotnet publish docs/Inertia.Net.Docs/Inertia.Net.Docs.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# 3. Runtime: ASP.NET + the Bun binary that runs `ssr/ssr.js` (the SSR process Inertia.Net starts).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=web /usr/local/bin/bun /usr/local/bin/bun
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/docs/wwwroot/build ./wwwroot/build
COPY --from=web /src/docs/ssr ./ssr

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    INERTIA_SSR=1 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# No curl in the image; bun is already here for SSR.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
  CMD ["bun", "-e", "fetch('http://127.0.0.1:8080/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]

ENTRYPOINT ["dotnet", "Inertia.Net.Docs.dll"]
