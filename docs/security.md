# Security — Threat Model and Hardening

## Attack Surface Overview

Seren exposes three main entry points:

| Surface | Protocol | Description |
|---------|----------|-------------|
| WebSocket endpoint | `ws://` or `wss://` at `/ws` | Primary real-time channel for UI clients |
| REST API | HTTP at `/health/live`, `/health/ready` | Health probes; future REST endpoints |
| OpenClaw bridge | WebSocket/HTTP to `ws://openclaw:18789` | Outbound connection to the AI gateway |

Additional internal surfaces:

- PostgreSQL connection string (internal network only)
- OTLP telemetry endpoint (internal network only)
- Seq log sink (internal network only)

## Authentication Model

### JWT Bearer Tokens

- Clients authenticate over WebSocket by sending an `module:authenticate` event with a JWT bearer token.
- The `SerenHubOptions.RequireAuthentication` flag gates whether authentication is mandatory. Phase 1 defaults to `false` for local development; must be `true` in production.
- Tokens are validated using `Microsoft.AspNetCore.Authentication.JwtBearer` with the configured signing key.

### Timing-Safe Token Comparison (CWE-208)

All token comparisons use `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` to prevent timing side-channel attacks. This mitigates CWE-208 (Observable Timing Discrepancy).

Conventional `==` or `string.Equals` must never be used for secret comparison.

## Authorization Model

- Role-based authorization: `Admin` and `User` roles.
- Policies enforce that only `Admin` peers can invoke management endpoints.
- WebSocket messages are dispatched through Mediator handlers; each handler validates authorization before execution.
- The `IPeerRegistry` tracks authenticated peers and their roles.

## Rate Limiting

- Sliding-window rate limiter per peer IP, configured via `Seren:RateLimit` options.
- `PermitLimit` (default 100) controls the maximum concurrent permits per peer.
- `Enabled` flag allows disabling rate limiting during development.
- Uses `Microsoft.AspNetCore.RateLimiting` (ASP.NET Core 10 built-in).

## CORS Policy

- Strict origin whitelist in production, configured via `Seren:Cors:AllowedOrigins`.
- Development allows `http://localhost:5173` (Vite dev server) and `http://localhost:1420` (Tauri dev).
- No wildcard (`*`) origins in production.
- Preflight requests are handled automatically by the CORS middleware.

## Secret Management

| Environment | Strategy |
|-------------|----------|
| Development | .NET User Secrets (`dotnet user-secrets`) — never committed |
| Docker | Environment variables in `docker-compose.yml`, sourced from `.env` file |
| Production | Azure Key Vault / AWS Secrets Manager via environment variable injection |

### Rules

- The `.env` file must never be committed. `.gitignore` excludes it.
- `.env.example` is committed with placeholder values only.
- JWT signing keys and OpenClaw auth tokens must be cryptographically random (minimum 256 bits) in production.
- Connection strings with passwords must use environment variable substitution, not hardcoded values.

## WebSocket Security

- Kestrel `KeepAliveInterval` defaults to 15 seconds (`Seren:WebSocket:KeepAliveIntervalSeconds`).
- `ReadTimeoutSeconds` (default 30) drops stale connections that send no frames.
- All inbound frames are validated against the expected schema before processing.
- Unauthenticated peers are disconnected after a grace period when `RequireAuthentication` is enabled.

## Docker Security

- Container runs as a non-root user (`appuser`) created in the runtime stage.
- Minimal base image: `mcr.microsoft.com/dotnet/aspnet:10.0` (no SDK, no shell extras).
- Health checks use the `/health/live` endpoint with `curl --fail`.
- No sensitive files are copied into the image (`.dockerignore` excludes `tests/`, `**/bin/`, `**/obj/`).
- Multi-stage build ensures build tools and source code stay in the build stage only.
- `TreatWarningsAsErrors=true` prevents compilation with security warnings.

## HTTPS and TLS Termination

- Phase 1: Kestrel listens on plain HTTP inside the container.
- Production: TLS termination happens at a reverse proxy (nginx, Caddy, or cloud load balancer) in front of the container.
- The `ASPNETCORE_FORWARDEDHEADERS_ENABLED` environment variable should be set to `true` when behind a proxy.

## Native AOT Publish Status

Native AOT publishing is scaffolded but **deferred to V2**. The project file
accepts `dotnet publish -p:PublishAot=true` which enables `IsAotCompatible`
plus AOT-size-optimization flags, but the current ASP.NET Core 10 codebase
has ten trim/AOT analyzer errors that must be resolved first. Enabling AOT
in Debug/Release would immediately break the build because of
`TreatWarningsAsErrors=true`.

### Current AOT blockers

Collected from `dotnet build -p:IsAotCompatible=true` (April 2026):

| # | Source | Issue |
|---|--------|-------|
| 1 | `Program.cs:43-44` | `ConfigurationBinder.GetValue<T>` (IL2026) — used to read `OpenTelemetry:ServiceName` and `OtlpEndpoint`. Fix: replace with `section["Key"]` string reads. |
| 2 | `Program.cs:87` | `ConfigurationBinder.Bind(AuthOptions)` (IL2026 + IL3050) — binds the Auth section outside the Options pattern to decide on JwtBearer registration. Fix: use a strongly-typed `IConfigurationSection.Get<AuthOptions>()` with a source-generated binder, or manually read the four properties. |
| 3 | `Program.cs:196` | `EndpointRouteBuilderExtensions.MapGet(pattern, Delegate)` (IL2026 + IL3050) — the root `MapGet("/", () => Results.Text(...))` uses the delegate overload which performs reflection for parameter binding. Fix: switch to the `RequestDelegate` overload `MapGet("/", ctx => ctx.Response.WriteAsync(...))`. |
| 4 | `AuthEndpoints.cs:27-28` | Same `MapGet` / `MapPost` delegate overloads in `/auth/logout` and `/auth/whoami`. Fix: migrate both to `RequestDelegate` handlers or annotate with `[RequiresDynamicCode]` suppressions after writing explicit metadata. |
| 5 | Serilog | `UseSerilog((ctx, services, cfg) => cfg.ReadFrom.Configuration(...))` is known reflection-heavy. Fix: programmatic bootstrap logger with explicit sink registrations, or wait for Serilog's upcoming source-gen configuration. |
| 6 | OpenTelemetry OTLP exporter | `OpenTelemetry.Exporter.OpenTelemetryProtocol` is not yet AOT-clean. Fix: wait for the upstream fix or switch to the console exporter in AOT builds. |

### Target for V2

1. Fix the blockers above. Each is a 10–100 line PR.
2. Flip `<IsAotCompatible>true</IsAotCompatible>` to unconditional in the
   csproj.
3. Add a `dotnet publish -p:PublishAot=true -c Release -r linux-x64` job
   to `.github/workflows/server-ci.yml` so regressions fail CI.
4. Update the Dockerfile to use the AOT-published binary which should
   drop the runtime image size from ~200 MB to ~30 MB and cold start
   below 100 ms.

## Future Hardening

| Item | Status |
|------|--------|
| HTTPS termination via reverse proxy | Planned |
| OWASP ZAP scanning in CI | Done (server-ci.yml owasp-zap job) |
| Content Security Policy headers | Done (Seren.Infrastructure.Security.SecurityHeadersMiddleware) |
| Subresource Integrity for web assets | Planned |
| Helmet-style security headers middleware | Done (same middleware as above) |
| Integration tests with authenticated WebSocket clients | Done (WebSocketAuthTests, 5 cases) |
| JWT token revocation (logout) | Done (ITokenRevocationStore + /auth/logout) |
| Native AOT publish | Deferred — see blockers section above |
| Network policies between Docker services | Planned |
| Container image signing and verification | Planned |
| Dependency vulnerability scanning (Dependabot, Snyk) | Planned |