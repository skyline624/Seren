# Seren

> Real-time coordination hub for an embodied AI character — multi-surface chat with a VRM or Live2D avatar.

Seren is a thin coordinator in front of an LLM backend. It serves a Vue 3 web PWA (with Tauri desktop and Capacitor mobile targets), terminates WebSocket connections from every surface on a single `.NET 10` hub, and delegates all model traffic to an OpenClaw gateway. The avatar, the chat history, the settings, and the model routing live in this repository; LLM providers are pluggable.

## Feature status

- ✓ Streaming text chat over WebSocket, with emotion and action markers extracted from the stream.
- ✓ Multi-device conversation history, persistent across reloads and peer devices on the same server session.
- ✓ Six-section settings drawer (Connection / Appearance / Avatar / Voice / LLM / Character), persisted per-key in `localStorage`.
- ✓ VRM and Live2D rendering, with eleven live tuning knobs on the VRM path (scale, position, rotation, camera, lighting, eye tracking, toon outline).
- ✓ Server-side model catalogue merging OpenClaw's cloud providers with locally-installed Ollama models at `GET /api/models`.
- ✓ Character CRUD with inline edit of the active character from the settings drawer.
- ◦ Voice pipeline (VAD, STT, TTS) — scaffolded but dormant in the current release.
- ◦ Plugin / module system — on the roadmap.

## Roadmap

**Shipped**

- Persistent WebSocket hub with authenticated handshake (`authenticate → announce → registry:sync`).
- Streaming text chat with chunk-level `<think>` filtering and `<emotion:*>` / `<action:*>` marker extraction.
- Multi-device conversation history, server-side session rotation for "new conversation" (preserves long-term memory).
- Settings drawer with six sections (Connection, Appearance, Avatar, Voice, LLM, Character), persisted per-key in `localStorage`.
- Theme (auto / light / dark) and free primary-hue slider driving CSS custom properties at `:root`.
- Locale switching (fr / en) wired to `vue-i18n`.
- VRM renderer with reactive scale, position, rotation, camera distance / height / FOV, ambient + directional lighting, eye tracking (camera / pointer / off), toon outline (thickness, colour, alpha).
- Live2D fallback viewer for 2D models.
- Character CRUD with inline editable active-character form in Settings.
- `/api/models` endpoint merging OpenClaw's cloud catalog and the local Ollama `GET /api/tags` with a 60-second server cache.
- Chat stream handler detached from the WebSocket receive loop so heartbeats, reset commands, and scroll-back requests stay responsive during long-running LLM responses.
- Docker Compose orchestration bringing up OpenClaw, Seren API, and Seren Web together.

**In progress**

- Light-theme CSS cascade fix across the Settings drawer and `<html lang>` synchronisation with the locale store.
- PWA asset set (192×192, 512×512 icons, maskable variant) and offline precache regression fix.
- Multi-tab synchronisation for user messages (assistant replies are already broadcast; user echo still missing on peer tabs).
- Thinking-mode plumbing from the LLM settings store through OpenClaw's session config.

**Planned — next**

- Voice pipeline end-to-end: VAD threshold + sensitivity, STT provider catalog (confidence-threshold slider for Whisper-family providers), TTS provider catalog (voice, pitch, speed), queued playback with concurrency control, barge-in.
- Avatar emotion motion clips (`.vrma` pack) mapped to the `avatar:emotion` event so the body reacts beyond facial expressions, plus a scene / backdrop selector.
- Conversation export / import (CSV or Markdown) and character card import (Character Card v3: PNG, APNG, JSON with embedded `character_book`).
- Resource island: floating widget showing STT / VAD / VRM asset download progress and dependencies per enabled module.

**Planned — medium-term**

- Module / plugin system: event-bus registration where modules declare capabilities, scopes, required permissions, and UI widgets; delivery modes (broadcast, consumer-group) with selection strategies (round-robin, sticky, priority); hot-reload.
- Permission / approval UI for module scopes (`invoke`, `emit`, `read`, `write`, `subscribe`, `register`, `execute`, `hook`, `process`, `manage`) with i18n-aware reason strings surfaced in the consent dialog.
- Long-term memory UI: distinction between ephemeral session context and per-module persistent storage, with retrieval-augmented recall driven by an embedding model.
- Audio-reactive avatar: beat-sync worklet (spectral flux, adaptive threshold, buffer duration, highpass / lowpass / envelope filters, BPM bounds) and finer-grained lip-sync driven by WebGPU-accelerated ONNX inference.
- Per-agent tool declaration so tool-capable models (Gemma 4, Qwen3) expose curated function schemas rather than relying on the default gateway catalog.

**Later**

- Channel integrations: Discord (voice presence + audio pipeline), Telegram (text + image + sticker + tool calling), Satori for universal bot platforms (Kook, Lark, Slack, DingTalk), Twitter monitoring, Minecraft cognitive agent. Each channel is gated behind its own provider credentials and a feature flag.
- Desktop-shell controls (always-on-top, compact controls island, visible-on-all-workspaces) once the Tauri app stabilises.
- Native mobile recording and playback pipeline (Capacitor plugin) once the voice pipeline is in place.
- In-UI observability: OpenTelemetry trace stream viewer, devtools IO tracer for the WebSocket envelopes.
- Settings / memory backup: unified export / import of `localStorage` + per-module persistent stores + character library into a single signed archive.

## Tech stack

- **Server** — .NET 10 / ASP.NET Core, Clean Architecture (Domain / Application / Infrastructure / Server.Api), Mediator source generator, FluentValidation, Serilog, OpenTelemetry.
- **Web** — Vue 3, Pinia, UnoCSS, `vue-i18n`, Vite + PWA plugin.
- **Desktop** — Tauri 2.
- **Mobile** — Capacitor 8 (iOS, Android).
- **3D avatar** — TresJS (Three.js Vue bindings), `@pixiv/three-vrm`, `@pixiv/three-vrm-animation`.
- **2D avatar** — PIXI.js + `pixi-live2d-display` (Cubism 4).
- **LLM backend** — OpenClaw gateway fronting Ollama, OpenAI, Anthropic, Google, Meta, Qwen, GLM, Mistral and more.
- **Local acceleration** — Ollama with AMD ROCm (kernel 6.8 + `amdgpu-dkms` + ROCm 7) or NVIDIA CUDA.

## Architecture

```
┌─────────────────────────┐
│  Web / Desktop / Mobile │
│   Vue 3 clients (SDK)   │
└───────────┬─────────────┘
            │ WebSocket (/ws)  +  REST (/api)
            ▼
┌─────────────────────────┐
│       Seren Hub         │   .NET 10, WebSocket hub, /api/models, /api/characters
│ (Clean Architecture)    │   In-memory peer registry, session key rotation
└───────────┬─────────────┘
            │ WebSocket (gateway protocol, auth + scopes)
            ▼
┌─────────────────────────┐
│    OpenClaw Gateway     │   LLM routing, persisted transcripts, plugins, agents
└───────────┬─────────────┘
            │
    ┌───────┼────────┬────────────┬───────────┐
    ▼       ▼        ▼            ▼           ▼
 Ollama   OpenAI  Anthropic     Google     Other providers
 (local)                                   (Qwen, Groq, GLM, …)
```

Seren owns the user surface, the realtime transport, the character library, and the per-device identity. OpenClaw owns the LLM session, the memory plugins, the tool execution, and the provider credentials. Both communicate over a single authenticated WebSocket and a handful of REST endpoints.

## Quick start (Docker Compose)

```bash
git clone https://github.com/<your-fork>/seren
cd seren
cp .env.example .env
# Edit .env and set OPENCLAW_GATEWAY_TOKEN to a strong random string.
docker compose up -d --build
```

Wait for the three containers (`openclaw`, `seren-api`, `seren-web`) to report `healthy`, then open `http://localhost:9080`. The web PWA loads the default VRM avatar, connects to the hub, and is ready to chat as soon as at least one provider is configured in `ops/openclaw/openclaw.json` or through environment variables in `.env`.

To route traffic to a locally-installed Ollama, make sure Ollama is bound on `0.0.0.0:11434` on the host (e.g. through a systemd override) and leave `OLLAMA_BASE_URL` at its default (`http://host.docker.internal:11434`). Installed Ollama models appear under the `ollama/` provider in Settings → LLM after the next refresh.

## Local development

Each side builds independently — there is no shared build at the repo root.

**Server**

```bash
dotnet restore src/server/Seren.sln
dotnet build   src/server/Seren.sln
dotnet test    src/server/Seren.sln
dotnet run --project src/server/Seren.Server.Api
```

**UI**

```bash
cd src/ui
pnpm install
pnpm build:packages
pnpm -F @seren/web dev          # PWA on http://localhost:5173
pnpm -F @seren/desktop tauri dev
pnpm -F @seren/mobile cap:android
```

Workspace packages use `"workspace:*"` references and the `catalog:` versions pinned in `src/ui/pnpm-workspace.yaml`. Turbo orchestrates `build` → `typecheck` → `test` chains with `^build` dependencies, so changing a package rebuilds its dependents on demand.

## Testing

```bash
# Server — xUnit v3 + Shouldly + NSubstitute
dotnet test src/server/Seren.sln
dotnet format src/server/Seren.sln --verify-no-changes

# UI — vitest across the pnpm workspace
pnpm -C src/ui test
pnpm -C src/ui typecheck
pnpm -C src/ui lint
```

The server suite currently ships ~220 tests across Domain / Application / Infrastructure / Server.Api.IntegrationTests; the UI suite ships vitest coverage for the shared stores, composables, and SDK.

## User-facing settings

The settings drawer exposes six sections, all persisted per-key in `localStorage` under the `seren/` namespace:

- **Connection** — server URL override, auth token.
- **Appearance** — theme mode (auto / light / dark), primary hue (0-360°), interface locale.
- **Avatar** — mode picker (VRM / Live2D), model scale, position, rotation, camera distance / height / FOV, ambient + directional lighting, eye tracking (camera / pointer / off), toon outline (thickness, colour, alpha).
- **Voice** — voice-detection threshold (remaining knobs ship with the voice pipeline).
- **LLM** — provider picker, model dropdown populated by `/api/models`, custom model ID override, thinking mode (stored).
- **Character** — active character display with inline edit of name, agent ID, system prompt, VRM asset path.

## LLM providers

`GET /api/models` returns a merged catalogue built server-side from two sources:

- **OpenClaw cloud** — Anthropic, Google Bedrock, Meta, Qwen, GLM, Mistral, MiniMax and more (hundreds of entries depending on gateway config).
- **Ollama local** — `ollama/<name>` entries sourced from the Ollama daemon's `GET /api/tags`, covering both locally-installed weights (e.g. `ollama/seren-qwen:latest`, `ollama/seren-gemma:latest`) and cloud-routed Ollama models (`ollama/qwen3.5:cloud`, `ollama/nemotron-3-super:cloud`, …).

For AMD systems running sizable local models (Qwen3 9B, Gemma 4 7.5B, etc.), the ROCm-accelerated Ollama path is supported on a generic Linux 6.8 kernel with `amdgpu-dkms` and ROCm 7. Detailed setup notes live in `./docs/14-guide-reproduction.md`.

## Repository layout

```
Seren/
├── src/
│   ├── server/                 # .NET 10 solution (Seren.sln)
│   │   ├── Seren.Domain/       # Entities, value objects, abstractions — no framework deps
│   │   ├── Seren.Application/  # Use cases as Mediator IRequest / IRequestHandler pairs
│   │   ├── Seren.Contracts/    # Wire-level DTOs + EventTypes shared with the SDK
│   │   ├── Seren.Infrastructure/  # OpenClaw / Ollama adapters, realtime hub, auth
│   │   ├── Seren.Server.Api/   # ASP.NET Core composition root, Dockerfile
│   │   └── tests/              # Domain / Application / Infrastructure / Integration
│   └── ui/                     # pnpm workspace + Turbo
│       ├── apps/
│       │   ├── seren-web/      # Vue 3 PWA (Vite)
│       │   ├── seren-desktop/  # Tauri 2 shell
│       │   └── seren-mobile/   # Capacitor 8 shell
│       └── packages/
│           ├── seren-sdk/      # Typed WebSocket client + contracts
│           ├── seren-ui-shared/   # Vue components + pinia stores
│           ├── seren-ui-three/    # VRM renderer (TresJS + three-vrm)
│           ├── seren-ui-live2d/   # Live2D renderer (PIXI + pixi-live2d-display)
│           ├── seren-ui-audio/    # VAD + audio pipeline primitives
│           └── seren-i18n/     # Shared locale resources
├── ops/
│   └── openclaw/openclaw.json  # OpenClaw gateway configuration (JSON5, read-only mount)
├── docker-compose.yml          # openclaw + seren-api + seren-web stack
├── docs/                       # Reference documentation (17 chapters)
└── tools/                      # One-off scripts (commit-msg hook, …)
```

## Contributing

- Install the root git hooks once: `npm install` at the repo root activates `simple-git-hooks`. Pre-commit runs `dotnet format --verify-no-changes` on the server solution and `pnpm -C src/ui lint` + `pnpm -C src/ui typecheck` on the UI workspace.
- Commit messages follow Conventional Commits; the allowed types (`feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `build`, `ci`, `perf`, `style`, `revert`) are enforced by `tools/scripts/check-conventional-commit.js` via the `commit-msg` hook.
- Code analysis is on with `TreatWarningsAsErrors=true` for the server; avoid `NoWarn` suppressions unless you can justify them inline.
- Design principles: SOLID, DRY, KISS, enterprise-grade. Comments explain *why*, never *what*.

## License

MIT.

## Background

Seren's architecture is inspired by the open-source [AIRI](https://github.com/moeru-ai/airi) project, re-implemented against a different stack (C# hub + OpenClaw backend). The reference documentation for the architectural choices lives under [`./docs/00-sommaire.md`](./docs/00-sommaire.md) as a 17-chapter French series, starting with an overview and descending into protocols, packages, and the reproduction guide.
