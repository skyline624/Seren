# Seren

> Real-time coordination hub for an embodied AI character — multi-surface chat with a Live2D avatar.

Seren is a thin coordinator in front of an LLM backend. It serves a Vue 3 web PWA (with Tauri desktop and Capacitor mobile targets), terminates WebSocket connections from every surface on a single `.NET 10` hub, and delegates all model traffic to an OpenClaw gateway. The avatar, the chat history, the settings, and the model routing live in this repository; LLM providers are pluggable.

## Feature status

- ✓ Streaming text chat over WebSocket, with emotion and action markers extracted from the stream.
- ✓ Multi-device conversation history, persistent across reloads and peer devices on the same server session.
- ✓ Five-section settings drawer (Connection / Appearance / Animation IA / Voice / LLM / Character), persisted per-key in `localStorage`.
- ✓ **Live2D Cubism 4 renderer** — rigged 2D avatar (Hiyori bundled by default), with motion groups, expressions, physics and viseme-driven lip-sync powered by `pixi-live2d-display`.
- ✓ **Phase-based avatar state machine** — `idle` / `listening` / `thinking` / `talking` / `reactive` derived from the chat store, ready to drive Live2D motion groups and expression presets (wiring planned for the next iteration).
- ✓ Server-side model catalogue merging OpenClaw's cloud providers with locally-installed Ollama models at `GET /api/models`.
- ✓ Character library — CRUD + Character Card v3 import (JSON / PNG / APNG) + OpenClaw-workspace persona capture + one-click JSON download for backup and transfer.
- ✓ Typed error popup on chat failures (idle timeout, auth, model-not-found, …) — no cross-model silent fallback, the user's chosen model is respected.
- ◦ Voice pipeline (VAD, STT, TTS) — scaffolded but dormant in the current release.
- ◦ Plugin / module system — on the roadmap.

## Roadmap

**Shipped**

- Persistent WebSocket hub with authenticated handshake (`authenticate → announce → registry:sync`).
- Streaming text chat with chunk-level `<think>` filtering and `<emotion:*>` / `<action:*>` marker extraction.
- Multi-device conversation history, server-side session rotation for "new conversation" (preserves long-term memory).
- Settings drawer with five sections (Connection, Appearance, Animation IA, Voice, LLM, Character), persisted per-key in `localStorage`.
- Theme (auto / light / dark) and free primary-hue slider driving CSS custom properties at `:root`.
- Locale switching (fr / en) wired to `vue-i18n`.
- Live2D Cubism 4 renderer backed by `pixi-live2d-display` — default Hiyori model + motions bundled in `public/avatars/live2d/`, model overridable per-character via `avatarModelPath` (`.model3.json`).
- Renderer-agnostic avatar state machine (`useAvatarStateStore` + `PHASE_GAINS`) exposing `phase` / `gains` refs for the next wiring chantier (motion-group + expression driving).
- Text-emotion classifier (on-device ONNX, ~66 MB one-time download) inferring an emotion from the reply when the character does not emit explicit `<emotion:*>` markers.
- Character library — CRUD with inline active-character editor, Character Card v3 import (JSON / PNG / APNG with embedded `character_book`), OpenClaw-workspace persona capture (`POST /api/characters/capture`), per-character JSON download (`GET /api/characters/{id}/download`).
- Chat resilience — typed error popup dialog with localised per-code messaging (idle timeout, auth, model-not-found, …), retry affordance on transient failures, no cross-model silent fallback.
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
- Wire `useAvatarStateStore.phase` to Live2D motion groups (e.g. `thinking` → reflective motion, `talking` → speaking motion) and expressions, so the avatar reacts to the chat phase without per-frame code.
- Scene / backdrop selector for the avatar stage.
- Conversation export / import (CSV or Markdown).
- Resource island: floating widget showing STT / VAD / Live2D asset download progress and dependencies per enabled module.

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
- **Avatar** — PIXI.js + `pixi-live2d-display` (Live2D Cubism 4), rigged 2D model with motion groups, expressions, physics, viseme lip-sync.
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

Wait for the three containers (`openclaw`, `seren-api`, `seren-web`) to report `healthy`, then open `http://localhost:9080`. The web PWA loads the default Hiyori Live2D avatar, connects to the hub, and is ready to chat as soon as at least one provider is configured in `ops/openclaw/openclaw.json` or through environment variables in `.env`.

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

The server suite currently ships ~180 tests across Domain / Application / Infrastructure / Server.Api.IntegrationTests; the UI suite ships ~100 vitest tests covering the shared stores (chat, avatar state, settings, character), composables (idle scheduler, idle animation catalog, emotion classifier, emotion mapping, persisted ref) and the Live2D / SDK clients. Timers and random sources are injected into every scheduler-style composable so tests pin outcomes deterministically without touching global time.

## User-facing settings

The settings drawer exposes five sections, all persisted per-key in `localStorage` under the `seren/` namespace:

- **Connection** — server URL override, auth token.
- **Appearance** — theme mode (auto / light / dark), primary hue (0-360°), interface locale.
- **Animation IA** — idle-scheduler cadence (slow / normal / fast), in-browser text-emotion classifier toggle (~66 MB one-time download, opt-in) with confidence threshold.
- **Voice** — voice-detection threshold (remaining knobs ship with the voice pipeline).
- **LLM** — provider picker, model dropdown populated by `/api/models`, custom model ID override, thinking mode (stored).
- **Character** — active character display with inline edit of name, agent ID, system prompt ; "Import CCv3 card" button (JSON/PNG/APNG), "Capture OpenClaw persona" button (`POST /api/characters/capture`), "Download" button on the active character (JSON serialised via the source-gen context at `GET /api/characters/{id}/download`). Per-character avatar model path (`.model3.json`) is stored on the `Character` record and used by the renderer when present; the default Hiyori model is loaded otherwise.

## LLM providers

`GET /api/models` returns a merged catalogue built server-side from two sources:

- **OpenClaw cloud** — Anthropic, Google Bedrock, Meta, Qwen, GLM, Mistral, MiniMax and more (hundreds of entries depending on gateway config).
- **Ollama local** — `ollama/<name>` entries sourced from the Ollama daemon's `GET /api/tags`, covering both locally-installed weights (e.g. `ollama/seren-qwen:latest`, `ollama/seren-gemma:latest`) and cloud-routed Ollama models (`ollama/qwen3.5:cloud`, `ollama/nemotron-3-super:cloud`, …).

For AMD systems running sizable local models (Qwen3 9B, Gemma 4 7.5B, etc.), the ROCm-accelerated Ollama path is supported on a generic Linux 6.8 kernel with `amdgpu-dkms` and ROCm 7. Detailed setup notes live in `./docs/14-guide-reproduction.md`.

### Chat resilience

Seren deliberately does **not** cascade to a different model on failure — the user's chosen provider is respected. Transient stream hiccups trigger at most one same-model retry (`OpenClaw:Chat:Resilience:RetryOnIdleBeforeFirstChunk`) ; anything further surfaces through a first-class `ChatErrorDialog` popup with a typed error code (`idle_timeout`, `total_timeout`, `auth`, `model_not_found`, `unknown`), a localised remediation hint, a "Change model" shortcut that opens the Settings drawer, and — on transient errors — a one-click "Retry" that resends the last user message. Error taxonomy lives in the `ErrorPayload` wire contract ; UI mapping is in `src/ui/packages/seren-ui-shared/src/components/ChatErrorDialog.vue`.

## Avatar animation pipeline

Seren renders a single Live2D Cubism 4 avatar through `pixi-live2d-display`. The rig does the heavy lifting — the runtime only has to pick which motion group and which expression to surface :

- **Motion groups** — pre-authored `.motion3.json` loops keyed by group name (`Idle`, `TapBody`, …). The renderer picks a motion with `model.motion(group, index, priority)` and lets the Cubism engine cross-fade into it.
- **Expressions** — blendshape presets keyed by name. Set with `model.expression(name)`, re-applied on every motion transition by the Cubism runtime.
- **Physics** — hair / skirt / accessory secondary motion is authored in the `.physics3.json` file and evaluated natively by the runtime. Nothing to wire in code.
- **Lip-sync** — viseme frames emitted by the TTS pipeline are pushed to the renderer as timed mouth-shape updates ; no per-frame audio DSP required.

The chat store exposes a phase (`idle` / `listening` / `thinking` / `talking` / `reactive`) through `useAvatarStateStore`. `PHASE_GAINS` keeps a renderer-neutral gain table — the next iteration binds that phase to motion-group selection and expression presets on the Live2D model, so `thinking` and `talking` pick motions consistent with the character rather than a fixed idle loop.

Default model: **Hiyori** (bundled under `src/ui/apps/seren-web/public/avatars/live2d/hiyori/`). To ship a different Live2D character, drop the full model folder (`.model3.json` entry + `.moc3` + textures + motions + expressions + physics) under `public/avatars/live2d/<character>/` and set `avatarModelPath` on the `Character` record to point at its `.model3.json`. See the [`pixi-live2d-display` docs](https://guansss.github.io/pixi-live2d-display/) for the file layout Cubism expects.

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
