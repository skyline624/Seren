# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Seren is a real-time coordination hub for an embodied AI character (VRM/Live2D avatar, multi-surface voice chat), powered by OpenClaw Gateway for LLM/memory/skills. It is an architectural rewrite of the open-source [AIRI](https://github.com/moeru-ai/airi) project.

## Build & Run Commands

### Server (.NET 10, from repo root)

```bash
dotnet restore src/server/Seren.sln
dotnet build src/server/Seren.sln
dotnet run --project src/server/Seren.Server.Api
dotnet test src/server/Seren.sln                          # all server tests
dotnet test src/server/tests/Seren.Domain.Tests            # single test project
dotnet format src/server/Seren.sln --verify-no-changes     # format check (CI + pre-commit)
```

### UI (Vue 3 + pnpm monorepo, from `src/ui/`)

```bash
pnpm install
pnpm dev                  # web dev server (Vite, port 5173)
pnpm dev:desktop          # Tauri desktop
pnpm dev:mobile:ios       # Capacitor iOS
pnpm dev:mobile:android   # Capacitor Android
pnpm build                # build all (turbo)
pnpm build:packages       # build shared packages only
pnpm lint                 # eslint (antfu config)
pnpm lint:fix
pnpm typecheck            # vue-tsc
pnpm test                 # vitest (turbo)
```

To run a single UI package's script: `pnpm -F @seren/web test` (filter by package name).

### Docker (full stack)

```bash
docker compose up -d          # API + Postgres + OpenClaw + Seq + Jaeger
docker compose up -d seren-api  # API only
```

API at `:5000`, Seq logs at `:5341`, Jaeger traces at `:16686`.

## Pre-commit Hooks

Installed via `simple-git-hooks` (root `npm install`). Runs before every commit:
1. `dotnet format src/server/Seren.sln --verify-no-changes`
2. `pnpm -C src/ui lint`
3. `pnpm -C src/ui typecheck`

Commit messages must follow **Conventional Commits**: `type(scope)?: subject`
Allowed types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `build`, `ci`, `perf`, `style`, `revert`.

## Architecture

### Server — Clean Architecture (.NET 10)

Dependency flow: `Server.Api` -> `Infrastructure` -> `Application` -> `Domain`. `Contracts` is shared across layers for WebSocket DTOs.

| Project | Role |
|---------|------|
| **Seren.Domain** | Entities (`Character`, `Peer`), value objects (`PeerId`, `ModuleIdentity`), repository interfaces |
| **Seren.Contracts** | WebSocket protocol: `WebSocketEnvelope`, event types, JSON payloads (chat chunks, audio, lipsync, avatar emotion, heartbeat). TypeGen generates TS types from these. |
| **Seren.Application** | CQRS handlers via source-generated Mediator (not MediatR). Pipeline behaviors: `LoggingBehavior`, `ValidationBehavior`. Abstractions for STT/TTS providers, OpenClaw client, token services. |
| **Seren.Infrastructure** | Implementations: OpenClaw REST+WS client (with Polly resilience), JWT auth, in-memory repositories, WebSocket hub (`SerenWebSocketHub` + `SerenWebSocketSessionProcessor`), security headers, CORS, rate limiting. |
| **Seren.Server.Api** | ASP.NET Core host. Minimal API endpoints, WebSocket upgrade, Serilog, OpenTelemetry tracing, health checks. |

Key patterns:
- **Mediator** (source-generated, scoped lifetime) for command/query dispatch — not MediatR (commercial).
- **FluentValidation** for request validation, wired as a pipeline behavior.
- **Central Package Management** — all NuGet versions pinned in `Directory.Packages.props`.
- `TreatWarningsAsErrors=true` and `AnalysisMode=AllEnabledByDefault` — code must be warning-free.
- Integration tests use `WebApplicationFactory<Program>` (the `Program` class has `public partial class Program;` for this).

### Server Tests

| Project | Scope |
|---------|-------|
| `Seren.Domain.Tests` | Entity/value object logic |
| `Seren.Application.Tests` | Handler logic, validators |
| `Seren.Infrastructure.Tests` | OpenClaw client, auth, WebSocket hub |
| `Seren.Server.Api.IntegrationTests` | Full HTTP/WS integration via `WebApplicationFactory` |

Framework: xUnit v3 + Shouldly + NSubstitute. Test method naming: `Method_Scenario_Result`.

### UI — pnpm Workspace + Turborepo

Three apps share code through workspace packages:

| App | Stack |
|-----|-------|
| `@seren/web` | Vue 3 + Vite + PWA (vite-plugin-pwa) |
| `@seren/desktop` | Same Vue app + Tauri 2 (Rust) |
| `@seren/mobile` | Same Vue app + Capacitor 8 (iOS/Android), includes native `host-websocket` plugin |

Shared packages (under `src/ui/packages/`):

| Package | Purpose |
|---------|---------|
| `@seren/sdk` | WebSocket client, protocol types (TS types generated from `Seren.Contracts` via TypeGen) |
| `@seren/ui-shared` | Shared Vue composables, stores (Pinia), components |
| `@seren/ui-three` | Three.js + three-vrm avatar rendering (via TresJS) |
| `@seren/ui-live2d` | PIXI.js + pixi-live2d-display |
| `@seren/ui-audio` | VAD (voice activity detection) via `@ricky0123/vad-web` |
| `@seren/i18n` | vue-i18n setup |

Build: packages build with `tsdown`, apps build with `vite build`. Turbo handles dependency ordering (`^build`).

Styling: **UnoCSS** (preset-uno), not Tailwind.

ESLint: `@antfu/eslint-config` — single quotes, no semicolons, 2-space indent for TS/Vue/JSON. `ts/consistent-type-definitions: interface` is enforced.

## WebSocket Protocol

Communication uses a JSON envelope (`WebSocketEnvelope` in Contracts):
- Event types defined in `EventTypes.cs` (e.g., `chat:chunk`, `audio:playback`, `lipsync:frame`, `avatar:emotion`, `module:authenticate`)
- Each event type has a corresponding payload class in `Seren.Contracts.Payloads`
- The session processor (`SerenWebSocketSessionProcessor`) handles the full lifecycle: authentication, message routing through Mediator, OpenClaw bridging

## CI Workflows

- **server-ci**: restore, format check, build (Release + /warnaserror), test, vulnerability scan, Docker build, OWASP ZAP baseline
- **ui-ci**: pnpm install (frozen-lockfile), lint, typecheck, audit, build packages, test, build apps
- **release.yml** / **mobile-ci.yml**: release and mobile build pipelines
