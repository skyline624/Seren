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

# Project files for restore caching
COPY ["src/server/Seren.sln", "src/server/"]
COPY ["src/server/Seren.Domain/Seren.Domain.csproj", "src/server/Seren.Domain/"]
COPY ["src/server/Seren.Application/Seren.Application.csproj", "src/server/Seren.Application/"]
COPY ["src/server/Seren.Infrastructure/Seren.Infrastructure.csproj", "src/server/Seren.Infrastructure/"]
COPY ["src/server/Seren.Contracts/Seren.Contracts.csproj", "src/server/Seren.Contracts/"]
COPY ["src/server/Seren.Server.Api/Seren.Server.Api.csproj", "src/server/Seren.Server.Api/"]

RUN dotnet restore src/server/Seren.Server.Api/Seren.Server.Api.csproj -a $TARGETARCH

# Full source copy and publish
COPY src/server/ src/server/
RUN dotnet publish src/server/Seren.Server.Api/Seren.Server.Api.csproj \
    -a $TARGETARCH \
    --no-restore \
    -c Release \
    -o /app/publish \
    -p:SkipTypeGen=true \
    /warnaserror

# ---------------------------------------------------------------------------
# Stage 2: Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for security (useradd for non-Debian base images)
RUN groupadd -r appuser && useradd -r -g appuser -s /sbin/nologin appuser
COPY --from=build /app/publish .

# Data directory for SQLite database (volume mount point)
RUN mkdir -p /data && chown -R appuser:appuser /app /data

USER appuser

EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Seren.Server.Api.dll"]
