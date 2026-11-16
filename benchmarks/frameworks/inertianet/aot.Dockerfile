# syntax=docker/dockerfile:1
# Native AOT for linux-musl-x64 needs a musl toolchain, so this one build runs in the SDK's Alpine AOT image rather than on the host.
# SDK 11 because src/Inertia.Net also targets net11.0. run.sh exports the result to publish/aot; from the repository root:
#   docker build -f benchmarks/frameworks/inertianet/aot.Dockerfile --output type=local,dest=benchmarks/frameworks/inertianet/publish/aot .
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-alpine3.24-aot AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/Inertia.Net src/Inertia.Net
COPY src/Inertia.Net.Analyzers src/Inertia.Net.Analyzers
COPY src/Inertia.Net.Redis src/Inertia.Net.Redis
COPY benchmarks/frameworks/inertianet benchmarks/frameworks/inertianet
RUN dotnet publish benchmarks/frameworks/inertianet -c Release -r linux-musl-x64 -p:PublishAot=true -o /out && rm -f /out/*.dbg

FROM scratch
COPY --from=build /out /
