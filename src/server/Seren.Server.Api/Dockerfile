# =============================================================================
# Seren Hub — Multi-stage Dockerfile
# Build context: repository root (.)
# =============================================================================

# ---------------------------------------------------------------------------
# Stage 1: Build
# ---------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# global.json from repo root
COPY ["global.json", "./"]

# Central Package Management and MSBuild shared props
COPY ["src/server/Directory.Packages.props", "src/server/Directory.Build.props", "src/server/"]

# Project files for restore caching. Each module assembly transitively
# referenced by Seren.Server.Api needs its csproj here so `dotnet restore`
# can resolve the dependency graph before the full source copy.
COPY ["src/server/Seren.sln", "src/server/"]
COPY ["src/server/Seren.Domain/Seren.Domain.csproj", "src/server/Seren.Domain/"]
COPY ["src/server/Seren.Application/Seren.Application.csproj", "src/server/Seren.Application/"]
COPY ["src/server/Seren.Infrastructure/Seren.Infrastructure.csproj", "src/server/Seren.Infrastructure/"]
COPY ["src/server/Seren.Contracts/Seren.Contracts.csproj", "src/server/Seren.Contracts/"]
COPY ["src/server/Seren.Modules.Audio/Seren.Modules.Audio.csproj", "src/server/Seren.Modules.Audio/"]
COPY ["src/server/Seren.Modules.Characters/Seren.Modules.Characters.csproj", "src/server/Seren.Modules.Characters/"]
COPY ["src/server/Seren.Modules.ChatAttachments/Seren.Modules.ChatAttachments.csproj", "src/server/Seren.Modules.ChatAttachments/"]
COPY ["src/server/Seren.Modules.OpenClaw/Seren.Modules.OpenClaw.csproj", "src/server/Seren.Modules.OpenClaw/"]
COPY ["src/server/Seren.Server.Api/Seren.Server.Api.csproj", "src/server/Seren.Server.Api/"]

# `-p:NuGetAudit=false` workarounds a pre-existing moderate vulnerability
# in OpenTelemetry.Api 1.11.2 that surfaces as a restore-blocking
# warning-as-error. The audit is re-enabled once the dep is bumped
# (tracked outside this chantier).
RUN dotnet restore src/server/Seren.Server.Api/Seren.Server.Api.csproj \
    -a $TARGETARCH \
    -p:NuGetAudit=false

# Full source copy and publish
COPY src/server/ src/server/
RUN dotnet publish src/server/Seren.Server.Api/Seren.Server.Api.csproj \
    -a $TARGETARCH \
    --no-restore \
    -c Release \
    -o /app/publish \
    -p:SkipTypeGen=true \
    -p:NuGetAudit=false \
    /warnaserror

# ---------------------------------------------------------------------------
# Stage 2: Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for security (useradd for non-Debian base images)
RUN groupadd -r appuser && useradd -r -g appuser -s /sbin/nologin appuser

# curl for the Docker healthcheck (the aspnet:10.0 runtime image ships
# without it). ~3 MB layer; the container has no other outbound HTTP needs.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Data directory for SQLite database (volume mount point)
RUN mkdir -p /data && chown -R appuser:appuser /app /data

USER appuser

EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Seren.Server.Api.dll"]
