# =============================================================================
# Seren Hub — Multi-stage Dockerfile
# Build context: src/server/
# =============================================================================

# ---------------------------------------------------------------------------
# Stage 1: Build
# ---------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Central Package Management and MSBuild shared props — restored first for
# optimal layer caching. The global.json from the repo root pins the SDK version.
COPY ["Directory.Packages.props", "Directory.Build.props", "./"]

# global.json lives at the repo root (two levels above the build context).
# We copy it here so `dotnet restore` respects the pinned SDK version.
COPY ["../../global.json", "./"]

COPY ["Seren.sln", "./"]
COPY ["Seren.Domain/Seren.Domain.csproj", "Seren.Domain/"]
COPY ["Seren.Application/Seren.Application.csproj", "Seren.Application/"]
COPY ["Seren.Infrastructure/Seren.Infrastructure.csproj", "Seren.Infrastructure/"]
COPY ["Seren.Contracts/Seren.Contracts.csproj", "Seren.Contracts/"]
COPY ["Seren.Server.Api/Seren.Server.Api.csproj", "Seren.Server.Api/"]

RUN dotnet restore Seren.Server.Api/Seren.Server.Api.csproj -a $TARGETARCH

# Full source copy and publish.
COPY . .
RUN dotnet publish Seren.Server.Api/Seren.Server.Api.csproj \
    -a $TARGETARCH \
    --no-restore \
    -c Release \
    -o /app/publish \
    /warnaserror

# ---------------------------------------------------------------------------
# Stage 2: Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for security.
RUN adduser --disabled-password --gecos "" appuser
COPY --from=build /app/publish .
RUN chown -R appuser:appuser /app

USER appuser

# Kestrel default port.
EXPOSE 5000

# Health check against the liveness endpoint.
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl --fail http://localhost:5000/health/live || exit 1

ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Seren.Server.Api.dll"]